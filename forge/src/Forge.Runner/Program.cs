using Forge.Coordination;
using Forge.Core.Configuration;
using Forge.Core.Interfaces;
using Forge.Executor;
using Forge.GitHub;
using Forge.Runner.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} {SourceContext,-40} {Level:u3} {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configuration binding
    builder.Services.Configure<ForgeOptions>(builder.Configuration.GetSection(ForgeOptions.SectionName));

    // Serilog
    builder.Services.AddSerilog(config => config
        .ReadFrom.Configuration(builder.Configuration)
        .WriteTo.Console(
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} {SourceContext,-40} {Level:u3} {Message:lj}{NewLine}{Exception}")
        .Enrich.FromLogContext());

    // Resolve connection string from environment variable
    var forgeOptions = builder.Configuration.GetSection(ForgeOptions.SectionName).Get<ForgeOptions>() ?? new ForgeOptions();
    var connStringEnv = forgeOptions.Postgres.ConnectionStringEnv;
    var connectionString = Environment.GetEnvironmentVariable(connStringEnv)
                           ?? throw new InvalidOperationException($"Environment variable {connStringEnv} is not set");

    // Validate config
    if (string.IsNullOrWhiteSpace(forgeOptions.GitHub.Owner))
        throw new InvalidOperationException("Forge:GitHub:Owner must be set");
    if (forgeOptions.TargetRepos.Count == 0)
        throw new InvalidOperationException("Forge:TargetRepos must contain at least one repo");

    foreach (var repo in forgeOptions.TargetRepos)
    {
        if (string.IsNullOrWhiteSpace(repo.Name))
            throw new InvalidOperationException("Each TargetRepo must have a Name");
        if (string.IsNullOrWhiteSpace(repo.ClonePath))
            throw new InvalidOperationException($"TargetRepo '{repo.Name}' must have a ClonePath");
    }

    Log.Information("Watching {Count} repos under {Owner}: {Repos}",
        forgeOptions.TargetRepos.Count, forgeOptions.GitHub.Owner,
        string.Join(", ", forgeOptions.TargetRepos.Select(r => r.Name)));

    // Register services
    builder.Services.AddForgeCoordination(connectionString);
    builder.Services.AddForgeGitHub();
    builder.Services.AddForgeExecutor();

    // Runner services
    builder.Services.AddSingleton<RunnerIdProvider>();
    builder.Services.AddScoped<IPoller, PollerService>();
    builder.Services.AddScoped<IDispatcher, DispatcherService>();
    builder.Services.AddScoped<IReviewer, ReviewerService>();

    // Hosted services
    builder.Services.AddHostedService<HeartbeatService>();
    builder.Services.AddHostedService<ForgeRunnerService>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Forge runner terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
