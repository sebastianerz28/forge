using System.Text.Json;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Forge.Coordination.Services;

public class CoordinationService : ICoordinationService
{
    private readonly ForgeDbContext _db;
    private readonly ILogger<CoordinationService> _logger;

    public CoordinationService(ForgeDbContext db, ILogger<CoordinationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ForgeRunner> RegisterRunnerAsync(Guid runnerId, string hostname, string name, CancellationToken ct = default)
    {
        // Upsert: insert or update on conflict
        var now = DateTime.UtcNow;
        var runner = await _db.Runners.FindAsync([runnerId], ct);

        if (runner is null)
        {
            runner = new ForgeRunner
            {
                Id = runnerId,
                Hostname = hostname,
                Name = name,
                LastHeartbeat = now,
                RegisteredAt = now,
                Status = RunnerStatus.Active
            };
            _db.Runners.Add(runner);
        }
        else
        {
            runner.Hostname = hostname;
            runner.Name = name;
            runner.LastHeartbeat = now;
            runner.Status = RunnerStatus.Active;
        }

        await _db.SaveChangesAsync(ct);
        return runner;
    }

    public async Task HeartbeatAsync(Guid runnerId, CancellationToken ct = default)
    {
        await _db.Runners
            .Where(r => r.Id == runnerId && r.Status == RunnerStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastHeartbeat, DateTime.UtcNow), ct);
    }

    public async Task<List<(Guid RunnerId, string RunnerName)>> MarkDeadRunnersAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - timeout;

        var deadRunners = await _db.Runners
            .Where(r => r.Status == RunnerStatus.Active && r.LastHeartbeat < cutoff)
            .ToListAsync(ct);

        if (deadRunners.Count == 0)
            return [];

        var result = deadRunners.Select(r => (r.Id, r.Name)).ToList();

        foreach (var runner in deadRunners)
            runner.Status = RunnerStatus.Dead;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Marked {Count} runners as dead: {Runners}", deadRunners.Count, result);
        return result;
    }

    public async Task<List<(int IssueNumber, string Repo, string RunnerName)>> ReleaseDeadRunnerTasksAsync(
        List<Guid> deadRunnerIds, CancellationToken ct = default)
    {
        if (deadRunnerIds.Count == 0)
            return [];

        var claimedStatuses = new[] { ForgeTaskStatus.Claimed, ForgeTaskStatus.AddressingReview };

        var tasksToRelease = await _db.Tasks
            .Include(t => t.ClaimedByRunner)
            .Where(t => t.ClaimedBy.HasValue
                        && deadRunnerIds.Contains(t.ClaimedBy.Value)
                        && claimedStatuses.Contains(t.Status))
            .ToListAsync(ct);

        var releasedInfo = tasksToRelease
            .Select(t => (t.GitHubIssueNumber, t.GitHubRepo, t.ClaimedByRunner?.Name ?? ""))
            .ToList();

        foreach (var task in tasksToRelease)
        {
            task.Status = ForgeTaskStatus.Pending;
            task.ClaimedBy = null;
            task.ClaimedAt = null;
            task.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        if (releasedInfo.Count > 0)
            _logger.LogInformation("Released {Count} tasks from dead runners", releasedInfo.Count);

        return releasedInfo;
    }

    public async Task<ForgeTask> UpsertTaskAsync(int issueNumber, string repo, CancellationToken ct = default)
    {
        // Try insert with ON CONFLICT DO NOTHING
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO tasks (github_issue_number, github_repo, status, created_at, updated_at) " +
            "VALUES ({0}, {1}, 'pending', NOW(), NOW()) ON CONFLICT (github_issue_number, github_repo) DO NOTHING",
            issueNumber, repo);

        var task = await _db.Tasks
            .FirstAsync(t => t.GitHubIssueNumber == issueNumber && t.GitHubRepo == repo, ct);

        return task;
    }

    public async Task<ForgeTask?> ClaimTaskAsync(Guid runnerId, CancellationToken ct = default)
    {
        // Atomic claim using SELECT ... FOR UPDATE SKIP LOCKED
        var task = await _db.Tasks
            .FromSqlRaw(
                "SELECT * FROM tasks WHERE status = 'pending' ORDER BY created_at ASC FOR UPDATE SKIP LOCKED LIMIT 1")
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return null;

        task.Status = ForgeTaskStatus.Claimed;
        task.ClaimedBy = runnerId;
        task.ClaimedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Runner {RunnerId} claimed task {TaskId} (issue #{IssueNumber})",
            runnerId, task.Id, task.GitHubIssueNumber);

        return task;
    }

    public async Task<ForgeTask> UpdateTaskStatusAsync(int taskId, ForgeTaskStatus status, int? prNumber = null, CancellationToken ct = default)
    {
        var task = await _db.Tasks.FindAsync([taskId], ct)
                   ?? throw new InvalidOperationException($"Task {taskId} not found");

        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;
        if (prNumber.HasValue)
            task.PrNumber = prNumber.Value;

        await _db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<ForgeTask?> GetTaskAsync(int taskId, CancellationToken ct = default)
    {
        return await _db.Tasks.FindAsync([taskId], ct);
    }

    public async Task<List<ForgeTask>> GetTasksByStatusAsync(CancellationToken ct = default, params ForgeTaskStatus[] statuses)
    {
        return await _db.Tasks
            .Where(t => statuses.Contains(t.Status))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }
}
