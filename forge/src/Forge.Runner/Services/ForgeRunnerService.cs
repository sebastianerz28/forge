using System.Diagnostics;
using System.Net;
using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Runner.Services;

public class ForgeRunnerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RunnerIdProvider _runnerIdProvider;
    private readonly IGitHubService _github;
    private readonly ForgeOptions _options;
    private readonly string _owner;
    private readonly Dictionary<string, TargetRepoOptions> _repoLookup;
    private readonly ILogger<ForgeRunnerService> _logger;

    public ForgeRunnerService(
        IServiceScopeFactory scopeFactory,
        RunnerIdProvider runnerIdProvider,
        IGitHubService github,
        IOptions<ForgeOptions> options,
        ILogger<ForgeRunnerService> logger)
    {
        _scopeFactory = scopeFactory;
        _runnerIdProvider = runnerIdProvider;
        _github = github;
        _options = options.Value;
        _owner = _options.GitHub.Owner;
        _repoLookup = _options.TargetRepos.ToDictionary(
            r => $"{_owner}/{r.Name}", r => r, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register runner
        using (var scope = _scopeFactory.CreateScope())
        {
            var coordination = scope.ServiceProvider.GetRequiredService<ICoordinationService>();
            await coordination.RegisterRunnerAsync(
                _runnerIdProvider.RunnerId, Environment.MachineName, _runnerIdProvider.RunnerName, stoppingToken);
        }

        _logger.LogInformation("Runner {RunnerId} ({RunnerName}) registered on {Hostname}",
            _runnerIdProvider.RunnerId, _runnerIdProvider.RunnerName, Environment.MachineName);

        await EnsureLabelsAsync(stoppingToken);

        var pollInterval = _options.GitHub.PollIntervalSeconds;
        var reviewInterval = _options.GitHub.ReviewPollIntervalSeconds;
        var deadTimeout = TimeSpan.FromSeconds(_options.Runner.DeadTimeoutSeconds);

        var lastPoll = DateTime.MinValue;
        var lastReviewCheck = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // Reap dead runners
            await ReapDeadRunnersAsync(deadTimeout, stoppingToken);

            // Poll for new issues
            if ((now - lastPoll).TotalSeconds >= pollInterval)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var poller = scope.ServiceProvider.GetRequiredService<IPoller>();
                    await poller.PollAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error during GitHub poll");
                }
                lastPoll = now;
            }

            // Try to claim and process a task
            try
            {
                await TryClaimAndProcessAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during claim/process");
            }

            // Check for review comments
            if ((now - lastReviewCheck).TotalSeconds >= reviewInterval)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var reviewer = scope.ServiceProvider.GetRequiredService<IReviewer>();
                    await reviewer.CheckAndAddressReviewsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error during review check");
                }
                lastReviewCheck = now;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("Runner {RunnerId} shutting down", _runnerIdProvider.RunnerId);
    }

    private async Task TryClaimAndProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var coordination = scope.ServiceProvider.GetRequiredService<ICoordinationService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var metrics = scope.ServiceProvider.GetRequiredService<IMetricsService>();

        var task = await coordination.ClaimTaskAsync(_runnerIdProvider.RunnerId, ct);
        if (task is null)
            return;

        var (owner, repoName) = ParseRepo(task.GitHubRepo);

        _logger.LogInformation("Processing issue #{IssueNumber} in {Repo} (task {TaskId})",
            task.GitHubIssueNumber, task.GitHubRepo, task.Id);

        // Claim the issue: remove forge-ready, apply runner label
        var label = GetRepoLabel(task.GitHubRepo);
        await RemoveLabelAsync(owner, repoName, task.GitHubIssueNumber, label, ct);
        await ApplyLabelAsync(owner, repoName, task.GitHubIssueNumber, _runnerIdProvider.RunnerLabel, ct);

        // Record run
        var run = await metrics.RecordRunStartAsync(
            task.Id, _runnerIdProvider.RunnerId, RunType.Initial,
            $"initial-{task.GitHubIssueNumber}", ct);

        // Execute
        var result = await dispatcher.DispatchInitialAsync(task, ct);
        await metrics.RecordRunFinishAsync(run.Id, result.Success, result.ErrorMessage, result.TokenUsage, ct);

        if (!result.Success)
        {
            await coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.Failed, ct: ct);
            await OnTaskFailedAsync(task, ct);
            _logger.LogError("Task {TaskId} failed: {Error}", task.Id, result.ErrorMessage);
            return;
        }

        // Push branch and open PR
        try
        {
            var pr = await OpenPrAsync(task, ct);
            await coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.PrOpened, prNumber: pr.Number, ct: ct);
            _logger.LogInformation("Opened PR #{PrNumber} for issue #{IssueNumber}",
                pr.Number, task.GitHubIssueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push/open PR for task {TaskId}", task.Id);
            await coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.Failed, ct: ct);
            await OnTaskFailedAsync(task, ct);
        }
    }

    private async Task<GitHubPullRequest> OpenPrAsync(ForgeTask task, CancellationToken ct)
    {
        var (owner, repoName) = ParseRepo(task.GitHubRepo);
        var repoDir = GetWorkDir(task.GitHubRepo);

        // Get current branch
        var branchResult = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            ArgumentList = { "rev-parse", "--abbrev-ref", "HEAD" },
            WorkingDirectory = repoDir,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        var branch = (await branchResult!.StandardOutput.ReadToEndAsync(ct)).Trim();
        await branchResult.WaitForExitAsync(ct);

        // Push
        var pushProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            ArgumentList = { "push", "-u", "origin", branch },
            WorkingDirectory = repoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        await pushProcess!.WaitForExitAsync(ct);
        if (pushProcess.ExitCode != 0)
        {
            var stderr = await pushProcess.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"git push failed: {stderr}");
        }

        // Open PR
        var issue = await _github.GetIssueAsync(owner, repoName, task.GitHubIssueNumber, ct);
        return await _github.CreatePullRequestAsync(
            owner, repoName,
            title: $"#{task.GitHubIssueNumber}: {issue.Title}",
            body: $"Resolves #{task.GitHubIssueNumber}\n\n---\n" +
                  $"Automated by Forge Runner `{_runnerIdProvider.RunnerName}` (`{_runnerIdProvider.RunnerId}`)",
            head: branch,
            ct: ct);
    }

    private async Task ReapDeadRunnersAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var coordination = scope.ServiceProvider.GetRequiredService<ICoordinationService>();

        var dead = await coordination.MarkDeadRunnersAsync(timeout, ct);
        if (dead.Count == 0)
            return;

        var deadIds = dead.Select(d => d.RunnerId).ToList();
        var released = await coordination.ReleaseDeadRunnerTasksAsync(deadIds, ct);

        foreach (var (issueNumber, repo, oldRunnerName) in released)
        {
            var (owner, repoName) = ParseRepo(repo);
            if (!string.IsNullOrEmpty(oldRunnerName))
                await RemoveLabelAsync(owner, repoName, issueNumber, $"na#{oldRunnerName}", ct);
        }
    }

    private async Task OnTaskFailedAsync(ForgeTask task, CancellationToken ct)
    {
        var (owner, repoName) = ParseRepo(task.GitHubRepo);
        await RemoveLabelAsync(owner, repoName, task.GitHubIssueNumber, _runnerIdProvider.RunnerLabel, ct);
        await ApplyLabelAsync(owner, repoName, task.GitHubIssueNumber, "forge-failed", ct);
    }

    private async Task EnsureLabelsAsync(CancellationToken ct)
    {
        var labels = new (string Name, string Color)[]
        {
            ("forge-ready", "e4e669"),
            (_runnerIdProvider.RunnerLabel, "1d76db"),
            ("done", "0e8a16"),
            ("forge-failed", "d93f0b")
        };

        foreach (var repo in _options.TargetRepos)
        {
            foreach (var (name, color) in labels)
            {
                try
                {
                    await _github.EnsureLabelAsync(_owner, repo.Name, name, color, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ensure label {Label} on {Repo}", name, $"{_owner}/{repo.Name}");
                }
            }
        }
    }

    private async Task ApplyLabelAsync(string owner, string repoName, int issueNumber, string label, CancellationToken ct)
    {
        try
        {
            await _github.AddLabelAsync(owner, repoName, issueNumber, label, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply label {Label} to issue #{IssueNumber}", label, issueNumber);
        }
    }

    private async Task RemoveLabelAsync(string owner, string repoName, int issueNumber, string label, CancellationToken ct)
    {
        try
        {
            await _github.RemoveLabelAsync(owner, repoName, issueNumber, label, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove label {Label} from issue #{IssueNumber}", label, issueNumber);
        }
    }

    private string GetWorkDir(string fullRepoName)
    {
        if (_repoLookup.TryGetValue(fullRepoName, out var repo))
            return repo.ClonePath;
        return _options.Executor.Cli.WorkDir ?? Directory.GetCurrentDirectory();
    }

    private string GetRepoLabel(string fullRepoName)
    {
        if (_repoLookup.TryGetValue(fullRepoName, out var repo))
            return repo.Label;
        return "forge-ready";
    }

    private static (string Owner, string RepoName) ParseRepo(string fullRepoName)
    {
        var parts = fullRepoName.Split('/', 2);
        return (parts[0], parts[1]);
    }
}
