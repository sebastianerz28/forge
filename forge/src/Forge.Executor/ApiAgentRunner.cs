using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Executor;

public class ApiAgentRunner : IAgentRunner
{
    private readonly ILogger<ApiAgentRunner> _logger;

    public ApiAgentRunner(IOptions<ForgeOptions> options, ILogger<ApiAgentRunner> logger)
    {
        _logger = logger;
        _logger.LogInformation("ApiAgentRunner initialized (stub) with model={Model}",
            options.Value.Executor.Api.Model);
    }

    public Task<AgentResult> RunAsync(string prompt, string workDir, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "API executor is not yet implemented. Set Executor:Backend to 'cli' in your config.");
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
