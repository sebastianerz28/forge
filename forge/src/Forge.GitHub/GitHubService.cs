using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace Forge.GitHub;

public class GitHubService : IGitHubService
{
    private readonly GitHubClient _client;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IOptions<ForgeOptions> options, ILogger<GitHubService> logger)
    {
        _logger = logger;
        var ghOptions = options.Value.GitHub;

        var token = Environment.GetEnvironmentVariable(ghOptions.TokenEnv)
                    ?? throw new InvalidOperationException($"Environment variable {ghOptions.TokenEnv} is not set");

        _client = new GitHubClient(new ProductHeaderValue("Forge"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<List<GitHubIssue>> ListOpenIssuesAsync(string owner, string repoName, string? creator = null, List<string>? labels = null, CancellationToken ct = default)
    {
        var request = new RepositoryIssueRequest
        {
            State = ItemStateFilter.Open,
        };

        if (creator is not null)
            request.Creator = creator;

        if (labels is not null)
            foreach (var label in labels)
                request.Labels.Add(label);

        var issues = await _client.Issue.GetAllForRepository(owner, repoName, request);

        var result = new List<GitHubIssue>();
        foreach (var item in issues)
        {
            if (item.PullRequest is not null)
                continue;

            result.Add(new GitHubIssue(
                Number: item.Number,
                Title: item.Title,
                Body: item.Body ?? "",
                Author: item.User?.Login ?? "",
                Labels: item.Labels?.Select(l => l.Name).ToList(),
                State: item.State.StringValue));
        }

        _logger.LogDebug("Fetched {Count} open issues from {Repo} (creator={Creator})",
            result.Count, $"{owner}/{repoName}", creator);

        return result;
    }

    public async Task<GitHubIssue> GetIssueAsync(string owner, string repoName, int issueNumber, CancellationToken ct = default)
    {
        var item = await _client.Issue.Get(owner, repoName, issueNumber);
        return new GitHubIssue(
            Number: item.Number,
            Title: item.Title,
            Body: item.Body ?? "",
            Author: item.User?.Login ?? "",
            Labels: item.Labels?.Select(l => l.Name).ToList(),
            State: item.State.StringValue);
    }

    public async Task<GitHubPullRequest> CreatePullRequestAsync(string owner, string repoName, string title, string body, string head, string baseBranch = "main", CancellationToken ct = default)
    {
        var pr = await _client.PullRequest.Create(owner, repoName,
            new NewPullRequest(title, head, baseBranch) { Body = body });

        _logger.LogInformation("Created PR #{Number}: {Title} in {Repo}", pr.Number, pr.Title, $"{owner}/{repoName}");

        return new GitHubPullRequest(
            Number: pr.Number,
            Title: pr.Title,
            HeadBranch: pr.Head.Ref,
            State: pr.State.StringValue,
            Body: pr.Body ?? "");
    }

    public async Task<List<GitHubPullRequest>> ListOpenPrsAsync(string owner, string repoName, string? author = null, CancellationToken ct = default)
    {
        var prs = await _client.PullRequest.GetAllForRepository(owner, repoName,
            new PullRequestRequest { State = ItemStateFilter.Open });

        var result = new List<GitHubPullRequest>();
        foreach (var item in prs)
        {
            if (author is not null && item.User?.Login != author)
                continue;

            result.Add(new GitHubPullRequest(
                Number: item.Number,
                Title: item.Title,
                HeadBranch: item.Head.Ref,
                State: item.State.StringValue,
                Body: item.Body ?? ""));
        }

        return result;
    }

    public async Task<List<GitHubReviewComment>> GetReviewCommentsAsync(string owner, string repoName, int prNumber, CancellationToken ct = default)
    {
        var comments = await _client.PullRequest.ReviewComment.GetAll(owner, repoName, prNumber);
        return comments.Select(c => new GitHubReviewComment(
            Id: c.Id,
            Body: c.Body,
            User: c.User?.Login ?? "",
            Path: c.Path,
            Line: c.OriginalPosition,
            CreatedAt: c.CreatedAt.ToString("o"))).ToList();
    }

    public async Task<List<GitHubReviewComment>> GetIssueCommentsAsync(string owner, string repoName, int issueOrPrNumber, CancellationToken ct = default)
    {
        var comments = await _client.Issue.Comment.GetAllForIssue(owner, repoName, issueOrPrNumber);
        return comments.Select(c => new GitHubReviewComment(
            Id: c.Id,
            Body: c.Body,
            User: c.User?.Login ?? "",
            CreatedAt: c.CreatedAt.ToString("o"))).ToList();
    }

    public async Task<string> GetPrDiffAsync(string owner, string repoName, int prNumber, CancellationToken ct = default)
    {
        var response = await _client.Connection.Get<string>(
            new Uri($"repos/{owner}/{repoName}/pulls/{prNumber}", UriKind.Relative),
            new Dictionary<string, string>(),
            "application/vnd.github.diff");

        return response.Body ?? "";
    }

    public async Task EnsureLabelAsync(string owner, string repoName, string name, string color = "5319e7", CancellationToken ct = default)
    {
        try
        {
            await _client.Issue.Labels.Create(owner, repoName, new NewLabel(name, color));
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Label already exists
        }
    }

    public async Task AddLabelAsync(string owner, string repoName, int issueNumber, string label, CancellationToken ct = default)
    {
        await _client.Issue.Labels.AddToIssue(owner, repoName, issueNumber, [label]);
    }

    public async Task RemoveLabelAsync(string owner, string repoName, int issueNumber, string label, CancellationToken ct = default)
    {
        try
        {
            await _client.Issue.Labels.RemoveFromIssue(owner, repoName, issueNumber, label);
        }
        catch (NotFoundException)
        {
            // Label wasn't on the issue
        }
    }

    public async Task CloseIssueAsync(string owner, string repoName, int issueNumber, CancellationToken ct = default)
    {
        await _client.Issue.Update(owner, repoName, issueNumber,
            new IssueUpdate { State = ItemState.Closed });
    }
}
