using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Forge.Executor;

public static class ExecutorExtensions
{
    public static IServiceCollection AddForgeExecutor(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRunner>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ForgeOptions>>();
            var backend = options.Value.Executor.Backend.ToLowerInvariant();

            return backend switch
            {
                "cli" => ActivatorUtilities.CreateInstance<CliAgentRunner>(sp),
                "api" => ActivatorUtilities.CreateInstance<ApiAgentRunner>(sp),
                _ => throw new InvalidOperationException($"Unknown executor backend: {backend}")
            };
        });

        return services;
    }
}
