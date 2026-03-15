using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IMetricsService
{
    string HashPrompt(string prompt);
    Task<ForgeRun> RecordRunStartAsync(int taskId, Guid runnerId, RunType runType, string prompt, CancellationToken ct = default);
    Task<ForgeRun> RecordRunFinishAsync(int runId, bool success, string? errorMessage = null, Dictionary<string, object>? tokenUsage = null, CancellationToken ct = default);
}
