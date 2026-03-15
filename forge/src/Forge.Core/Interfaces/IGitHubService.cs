namespace Forge.Core.Interfaces;

public interface IGitHubService
{
    Task<List<GitHubIssue>> ListOpenIssuesAsync(string owner, string repoName, string? creator = null, List<string>? labels = null, CancellationToken ct = default);
    Task<GitHubIssue> GetIssueAsync(string owner, string repoName, int issueNumber, CancellationToken ct = default);
    Task<GitHubPullRequest> CreatePullRequestAsync(string owner, string repoName, string title, string body, string head, string baseBranch = "main", CancellationToken ct = default);
    Task<List<GitHubPullRequest>> ListOpenPrsAsync(string owner, string repoName, string? author = null, CancellationToken ct = default);
    Task<List<GitHubReviewComment>> GetReviewCommentsAsync(string owner, string repoName, int prNumber, CancellationToken ct = default);
    Task<List<GitHubReviewComment>> GetIssueCommentsAsync(string owner, string repoName, int issueOrPrNumber, CancellationToken ct = default);
    Task<string> GetPrDiffAsync(string owner, string repoName, int prNumber, CancellationToken ct = default);
    Task EnsureLabelAsync(string owner, string repoName, string name, string color = "5319e7", CancellationToken ct = default);
    Task AddLabelAsync(string owner, string repoName, int issueNumber, string label, CancellationToken ct = default);
    Task RemoveLabelAsync(string owner, string repoName, int issueNumber, string label, CancellationToken ct = default);
    Task CloseIssueAsync(string owner, string repoName, int issueNumber, CancellationToken ct = default);
}

public record GitHubIssue(int Number, string Title, string Body, string Author = "", List<string>? Labels = null, string State = "open");
public record GitHubPullRequest(int Number, string Title, string HeadBranch, string State, string Body = "");
public record GitHubReviewComment(long Id, string Body, string User, string? Path = null, int? Line = null, string CreatedAt = "");
