namespace Forge.Core.Configuration;

public class ExecutorOptions
{
    public string Backend { get; set; } = "cli";
    public CliOptions Cli { get; set; } = new();
    public ApiOptions Api { get; set; } = new();
}

public class CliOptions
{
    public string Command { get; set; } = "claude";
    public int TimeoutSeconds { get; set; } = 600;
    public string? WorkDir { get; set; }
}

public class ApiOptions
{
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public string ApiKeyEnv { get; set; } = "ANTHROPIC_API_KEY";
}
