using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forge.Runner.Services;

public class HeartbeatService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RunnerIdProvider _runnerIdProvider;
    private readonly int _intervalSeconds;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        IServiceScopeFactory scopeFactory,
        RunnerIdProvider runnerIdProvider,
        IOptions<ForgeOptions> options,
        ILogger<HeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _runnerIdProvider = runnerIdProvider;
        _intervalSeconds = options.Value.Runner.HeartbeatIntervalSeconds;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordination = scope.ServiceProvider.GetRequiredService<ICoordinationService>();
                await coordination.HeartbeatAsync(_runnerIdProvider.RunnerId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Heartbeat failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
        }
    }
}
