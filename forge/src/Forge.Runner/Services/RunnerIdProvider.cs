using Forge.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Runner.Services;

public class RunnerIdProvider
{
    private const string RunnerIdFile = ".forge-runner-id";
    private readonly Guid _runnerId;
    private readonly string _runnerName;
    private readonly ILogger<RunnerIdProvider> _logger;

    public RunnerIdProvider(IOptions<ForgeOptions> options, ILogger<RunnerIdProvider> logger)
    {
        _logger = logger;
        var runnerOptions = options.Value.Runner;
        _runnerName = string.IsNullOrEmpty(runnerOptions.Name)
            ? Environment.MachineName
            : runnerOptions.Name;
        _runnerId = GetOrCreateRunnerId(runnerOptions.Id);
    }

    public Guid RunnerId => _runnerId;
    public string RunnerName => _runnerName;
    public string RunnerLabel => $"na#{_runnerName}";

    private Guid GetOrCreateRunnerId(string? configuredId)
    {
        if (!string.IsNullOrEmpty(configuredId))
            return Guid.Parse(configuredId);

        var idFile = new FileInfo(RunnerIdFile);
        if (idFile.Exists)
            return Guid.Parse(File.ReadAllText(idFile.FullName).Trim());

        var newId = Guid.NewGuid();
        File.WriteAllText(idFile.FullName, newId.ToString());
        _logger.LogInformation("Generated new runner ID: {RunnerId} (saved to {File})", newId, RunnerIdFile);
        return newId;
    }
}
