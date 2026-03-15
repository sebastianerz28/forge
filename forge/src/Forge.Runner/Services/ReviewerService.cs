using System.Collections.Concurrent;
using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Runner.Services;

public class ReviewerService : IReviewer
{
    private readonly IGitHubService _github;
    private readonly ICoordinationService _coordination;
    private readonly IDispatcher _dispatcher;
    private readonly IMetricsService _metrics;
    private readonly RunnerIdProvider _runnerIdProvider;
    private readonly string _ownerUsername;
    private readonly ILogger<ReviewerService> _logger;

    // Track seen comment IDs per PR
    private readonly ConcurrentDictionary<int, HashSet<long>> _seenCommentIds = new();

    public ReviewerService(
        IGitHubService github,
        ICoordinationService coordination,
        IDispatcher dispatcher,
        IMetricsService metrics,
        RunnerIdProvider runnerIdProvider,
        IOptions<ForgeOptions> options,
        ILogger<ReviewerService> logger)
    {
        _github = github;
        _coordination = coordination;
        _dispatcher = dispatcher;
        _metrics = metrics;
        _runnerIdProvider = runnerIdProvider;
        _ownerUsername = options.Value.GitHub.OwnerUsername;
        _logger = logger;
    }

    public async Task CheckAndAddressReviewsAsync(CancellationToken ct = default)
    {
        var tasks = await _coordination.GetTasksByStatusAsync(ct,
            ForgeTaskStatus.PrOpened, ForgeTaskStatus.InReview);

        foreach (var task in tasks)
        {
            if (task.PrNumber is null)
                continue;
            await CheckTaskReviewsAsync(task, ct);
        }
    }

    private async Task CheckTaskReviewsAsync(ForgeTask task, CancellationToken ct)
    {
        var prNum = task.PrNumber!.Value;
        var (owner, repoName) = ParseRepo(task.GitHubRepo);

        List<GitHubReviewComment> allComments;
        try
        {
            var reviewComments = await _github.GetReviewCommentsAsync(owner, repoName, prNum, ct);
            var issueComments = await _github.GetIssueCommentsAsync(owner, repoName, prNum, ct);
            allComments = [.. reviewComments, .. issueComments];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch comments for PR #{PrNumber} in {Repo}", prNum, task.GitHubRepo);
            return;
        }

        // Filter to owner's comments only
        if (!string.IsNullOrEmpty(_ownerUsername))
            allComments = allComments.Where(c => c.User == _ownerUsername).ToList();

        // Find new comments
        var seen = _seenCommentIds.GetOrAdd(prNum, _ => []);
        var newComments = allComments.Where(c => !seen.Contains(c.Id)).ToList();

        if (newComments.Count == 0)
            return;

        // Mark all current comments as seen
        _seenCommentIds[prNum] = allComments.Select(c => c.Id).ToHashSet();

        // First scan: just record existing comments without triggering a run
        if (seen.Count == 0)
        {
            _logger.LogInformation("First scan of PR #{PrNumber} — recorded {Count} existing comments",
                prNum, allComments.Count);
            if (task.Status == ForgeTaskStatus.PrOpened)
                await _coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.InReview, ct: ct);
            return;
        }

        _logger.LogInformation("PR #{PrNumber} has {Count} new review comments — triggering follow-up",
            prNum, newComments.Count);

        await _coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.AddressingReview, ct: ct);

        string diff;
        try
        {
            diff = await _github.GetPrDiffAsync(owner, repoName, prNum, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get diff for PR #{PrNumber}", prNum);
            diff = "";
        }

        var promptText = $"review-{prNum}-{newComments.Count}";
        var run = await _metrics.RecordRunStartAsync(task.Id, _runnerIdProvider.RunnerId, RunType.ReviewAddress, promptText, ct);

        var result = await _dispatcher.DispatchReviewAsync(task, newComments, diff, ct);
        await _metrics.RecordRunFinishAsync(run.Id, result.Success, result.ErrorMessage, result.TokenUsage, ct);

        if (result.Success)
        {
            await _coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.InReview, ct: ct);
            _logger.LogInformation("Review address succeeded for PR #{PrNumber}", prNum);
        }
        else
        {
            await _coordination.UpdateTaskStatusAsync(task.Id, ForgeTaskStatus.Failed, ct: ct);
            await OnReviewFailedAsync(task, ct);
            _logger.LogError("Review address failed for PR #{PrNumber}: {Error}", prNum, result.ErrorMessage);
        }
    }

    private async Task OnReviewFailedAsync(ForgeTask task, CancellationToken ct)
    {
        var (owner, repoName) = ParseRepo(task.GitHubRepo);

        try
        {
            await _github.RemoveLabelAsync(owner, repoName, task.GitHubIssueNumber, _runnerIdProvider.RunnerLabel, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove label {Label} from issue #{IssueNumber}",
                _runnerIdProvider.RunnerLabel, task.GitHubIssueNumber);
        }

        try
        {
            await _github.AddLabelAsync(owner, repoName, task.GitHubIssueNumber, "forge-failed", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply forge-failed label to issue #{IssueNumber}",
                task.GitHubIssueNumber);
        }
    }

    private static (string Owner, string RepoName) ParseRepo(string fullRepoName)
    {
        var parts = fullRepoName.Split('/', 2);
        return (parts[0], parts[1]);
    }
}
