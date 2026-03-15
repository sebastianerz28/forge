using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICoordinationService
{
    Task<ForgeRunner> RegisterRunnerAsync(Guid runnerId, string hostname, string name, CancellationToken ct = default);
    Task HeartbeatAsync(Guid runnerId, CancellationToken ct = default);
    Task<List<(Guid RunnerId, string RunnerName)>> MarkDeadRunnersAsync(TimeSpan timeout, CancellationToken ct = default);
    Task<List<(int IssueNumber, string Repo, string RunnerName)>> ReleaseDeadRunnerTasksAsync(List<Guid> deadRunnerIds, CancellationToken ct = default);

    Task<ForgeTask> UpsertTaskAsync(int issueNumber, string repo, CancellationToken ct = default);
    Task<ForgeTask?> ClaimTaskAsync(Guid runnerId, CancellationToken ct = default);
    Task<ForgeTask> UpdateTaskStatusAsync(int taskId, ForgeTaskStatus status, int? prNumber = null, CancellationToken ct = default);
    Task<ForgeTask?> GetTaskAsync(int taskId, CancellationToken ct = default);
    Task<List<ForgeTask>> GetTasksByStatusAsync(CancellationToken ct = default, params ForgeTaskStatus[] statuses);
}
