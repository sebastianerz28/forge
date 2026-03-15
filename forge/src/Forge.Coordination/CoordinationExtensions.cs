using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Coordination.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Coordination;

public static class CoordinationExtensions
{
    public static IServiceCollection AddForgeCoordination(this IServiceCollection services, string connectionString)
    {
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<RunnerStatus>("runner_status");
        dataSourceBuilder.MapEnum<ForgeTaskStatus>("task_status");
        dataSourceBuilder.MapEnum<RunType>("run_type");
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ForgeDbContext>(options =>
            options.UseNpgsql(dataSource));

        services.AddScoped<ICoordinationService, CoordinationService>();
        services.AddScoped<IMetricsService, MetricsService>();

        return services;
    }
}
