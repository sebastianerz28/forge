using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Runner.Services;

public class PollerService : IPoller
{
    private readonly IGitHubService _github;
    private readonly ICoordinationService _coordination;
    private readonly string _owner;
    private readonly string _ownerUsername;
    private readonly List<TargetRepoOptions> _targetRepos;
    private readonly ILogger<PollerService> _logger;

    public PollerService(
        IGitHubService github,
        ICoordinationService coordination,
        IOptions<ForgeOptions> options,
        ILogger<PollerService> logger)
    {
        _github = github;
        _coordination = coordination;
        _owner = options.Value.GitHub.Owner;
        _ownerUsername = options.Value.GitHub.OwnerUsername;
        _targetRepos = options.Value.TargetRepos;
        _logger = logger;
    }

    public async Task<List<ForgeTask>> PollAsync(CancellationToken ct = default)
    {
        var newTasks = new List<ForgeTask>();

        foreach (var repo in _targetRepos)
        {
            var issues = await _github.ListOpenIssuesAsync(
                _owner, repo.Name,
                creator: _ownerUsername,
                labels: [repo.Label],
                ct: ct);

            var fullName = $"{_owner}/{repo.Name}";

            foreach (var issue in issues)
            {
                var task = await _coordination.UpsertTaskAsync(issue.Number, fullName, ct);
                if (task.Status == ForgeTaskStatus.Pending && task.ClaimedBy is null)
                    newTasks.Add(task);
            }

            if (issues.Count > 0)
            {
                _logger.LogDebug("Poll: {Repo} — {IssueCount} issues by {Owner}",
                    fullName, issues.Count, _ownerUsername);
            }
        }

        if (newTasks.Count > 0)
        {
            _logger.LogInformation("Poll found {NewCount} new pending tasks across {RepoCount} repos",
                newTasks.Count, _targetRepos.Count);
        }

        return newTasks;
    }
}
