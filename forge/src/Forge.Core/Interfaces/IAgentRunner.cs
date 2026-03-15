namespace Forge.Core.Interfaces;

public interface IAgentRunner : IAsyncDisposable
{
    Task<AgentResult> RunAsync(string prompt, string workDir, CancellationToken ct = default);
}

public record AgentResult(
    bool Success,
    string Output = "",
    string? ErrorMessage = null,
    Dictionary<string, object>? TokenUsage = null,
    List<string>? ChangedFiles = null);
