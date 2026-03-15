using System.Diagnostics;
using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Runner.Services;

public class DispatcherService : IDispatcher
{
    private const int FileTreeLimit = 5000;
    private const int MemoryLimit = 10000;
    private const int DiffLimit = 20000;

    private readonly IGitHubService _github;
    private readonly IAgentRunner _agentRunner;
    private readonly string _owner;
    private readonly Dictionary<string, TargetRepoOptions> _repoLookup;
    private readonly string? _fallbackWorkDir;
    private readonly ILogger<DispatcherService> _logger;

    public DispatcherService(
        IGitHubService github,
        IAgentRunner agentRunner,
        IOptions<ForgeOptions> options,
        ILogger<DispatcherService> logger)
    {
        _github = github;
        _agentRunner = agentRunner;
        var opts = options.Value;
        _owner = opts.GitHub.Owner;
        _repoLookup = opts.TargetRepos.ToDictionary(
            r => $"{_owner}/{r.Name}", r => r, StringComparer.OrdinalIgnoreCase);
        _fallbackWorkDir = opts.Executor.Cli.WorkDir;
        _logger = logger;
    }

    public async Task<AgentResult> DispatchInitialAsync(ForgeTask task, CancellationToken ct = default)
    {
        var (owner, repoName) = ParseRepo(task.GitHubRepo);
        var repoDir = GetWorkDir(task.GitHubRepo);

        var issue = await _github.GetIssueAsync(owner, repoName, task.GitHubIssueNumber, ct);
        var context = GatherContext(repoDir);
        var prompt = BuildInitialPrompt(issue.Title, issue.Body, context);

        _logger.LogInformation("Dispatching initial run for issue #{IssueNumber} in {Repo} ({PromptLength} chars)",
            task.GitHubIssueNumber, task.GitHubRepo, prompt.Length);

        return await _agentRunner.RunAsync(prompt, repoDir, ct);
    }

    public async Task<AgentResult> DispatchReviewAsync(ForgeTask task, List<GitHubReviewComment> comments, string prDiff, CancellationToken ct = default)
    {
        var (owner, repoName) = ParseRepo(task.GitHubRepo);
        var repoDir = GetWorkDir(task.GitHubRepo);

        var issue = await _github.GetIssueAsync(owner, repoName, task.GitHubIssueNumber, ct);
        var prompt = BuildReviewPrompt(issue.Title, issue.Body, comments, prDiff);

        _logger.LogInformation("Dispatching review run for issue #{IssueNumber} PR #{PrNumber} in {Repo} ({PromptLength} chars, {CommentCount} comments)",
            task.GitHubIssueNumber, task.PrNumber, task.GitHubRepo, prompt.Length, comments.Count);

        return await _agentRunner.RunAsync(prompt, repoDir, ct);
    }

    private string GatherContext(string repoDir)
    {
        var parts = new List<string>();

        var tree = GetFileTree(repoDir);
        if (!string.IsNullOrEmpty(tree))
            parts.Add($"## Repository file tree\n```\n{tree[..Math.Min(tree.Length, FileTreeLimit)]}\n```");

        var memory = GetMemoryFiles(repoDir);
        if (!string.IsNullOrEmpty(memory))
            parts.Add($"## Project memory\n{memory[..Math.Min(memory.Length, MemoryLimit)]}");

        var claudeMd = ReadFile(repoDir, "CLAUDE.md");
        if (!string.IsNullOrEmpty(claudeMd))
            parts.Add($"## CLAUDE.md (project instructions)\n{claudeMd}");

        return string.Join("\n\n", parts);
    }

    private static string BuildInitialPrompt(string title, string body, string context) =>
        $"""
        You are a software engineer working on a task from a GitHub issue.
        Your job is to implement the changes described in the issue, commit them to a new branch, and prepare the code for a pull request.

        ## Issue: {title}

        {body}

        ---

        {context}

        ---

        ## Instructions
        1. Read and understand the issue requirements carefully.
        2. Explore the codebase to understand the relevant code.
        3. Implement the changes described in the issue.
        4. Create a new git branch named after the issue (e.g., `issue-42-short-description`).
        5. Commit your changes with clear commit messages.
        6. Make sure your changes are complete and correct before finishing.

        Do NOT push to remote — the forge runner will handle that.
        """;

    private static string BuildReviewPrompt(string title, string body, List<GitHubReviewComment> comments, string diff)
    {
        var commentsText = string.Join("\n\n", comments.Select(c =>
            $"**{c.User}**"
            + (c.Path is not null ? $" on `{c.Path}:{c.Line}`" : "")
            + $":\n{c.Body}"));

        return $"""
            You are addressing review feedback on a pull request.

            ## Original Issue: {title}

            {body}

            ---

            ## PR Diff
            ```diff
            {diff[..Math.Min(diff.Length, DiffLimit)]}
            ```

            ## Review Comments
            {commentsText}

            ---

            ## Instructions
            1. Read each review comment carefully.
            2. Make the requested changes.
            3. Commit your changes with a clear message referencing the review feedback.

            Do NOT push to remote — the forge runner will handle that.
            """;
    }

    private static string GetFileTree(string repoDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "find",
                ArgumentList = { ".", "-type", "f",
                    "-not", "-path", "./.git/*",
                    "-not", "-path", "./node_modules/*",
                    "-not", "-path", "./.next/*",
                    "-not", "-name", "*.lock" },
                WorkingDirectory = repoDir,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return "";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output;
        }
        catch
        {
            return "";
        }
    }

    private static string GetMemoryFiles(string repoDir)
    {
        var memoryDir = Path.Combine(repoDir, ".claude", "memory");
        if (!Directory.Exists(memoryDir))
            memoryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "memory");
        if (!Directory.Exists(memoryDir))
            return "";

        var parts = new List<string>();
        foreach (var file in Directory.GetFiles(memoryDir, "*.md").Order())
        {
            var content = File.ReadAllText(file);
            parts.Add($"### {Path.GetFileName(file)}\n{content}");
        }
        return string.Join("\n\n", parts);
    }

    private static string ReadFile(string repoDir, string relativePath)
    {
        var fullPath = Path.Combine(repoDir, relativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    }

    private string GetWorkDir(string fullRepoName)
    {
        if (_repoLookup.TryGetValue(fullRepoName, out var repo))
            return repo.ClonePath;
        return _fallbackWorkDir ?? Directory.GetCurrentDirectory();
    }

    private static (string Owner, string RepoName) ParseRepo(string fullRepoName)
    {
        var parts = fullRepoName.Split('/', 2);
        return (parts[0], parts[1]);
    }
}
