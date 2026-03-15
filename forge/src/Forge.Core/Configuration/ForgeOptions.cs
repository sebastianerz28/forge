namespace Forge.Core.Configuration;

public class ForgeOptions
{
    public const string SectionName = "Forge";

    public RunnerOptions Runner { get; set; } = new();
    public GitHubOptions GitHub { get; set; } = new();
    public PostgresOptions Postgres { get; set; } = new();
    public ExecutorOptions Executor { get; set; } = new();
    public List<TargetRepoOptions> TargetRepos { get; set; } = [];
}
