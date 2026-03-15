using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Microsoft.Extensions.Logging;

namespace Forge.Coordination.Services;

public class MetricsService : IMetricsService
{
    private readonly ForgeDbContext _db;
    private readonly ILogger<MetricsService> _logger;

    public MetricsService(ForgeDbContext db, ILogger<MetricsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string HashPrompt(string prompt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public async Task<ForgeRun> RecordRunStartAsync(int taskId, Guid runnerId, RunType runType, string prompt, CancellationToken ct = default)
    {
        var run = new ForgeRun
        {
            TaskId = taskId,
            RunnerId = runnerId,
            RunType = runType,
            StartedAt = DateTime.UtcNow,
            PromptHash = HashPrompt(prompt)
        };

        _db.Runs.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<ForgeRun> RecordRunFinishAsync(int runId, bool success, string? errorMessage = null,
        Dictionary<string, object>? tokenUsage = null, CancellationToken ct = default)
    {
        var run = await _db.Runs.FindAsync([runId], ct)
                  ?? throw new InvalidOperationException($"Run {runId} not found");

        run.FinishedAt = DateTime.UtcNow;
        run.DurationSeconds = (run.FinishedAt.Value - run.StartedAt).TotalSeconds;
        run.Success = success;
        run.ErrorMessage = errorMessage;
        run.TokenUsage = tokenUsage is not null ? JsonSerializer.Serialize(tokenUsage) : null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Run {RunId} finished: success={Success} duration={Duration:F1}s tokens={Tokens}",
            run.Id, run.Success, run.DurationSeconds, run.TokenUsage);

        return run;
    }
}
