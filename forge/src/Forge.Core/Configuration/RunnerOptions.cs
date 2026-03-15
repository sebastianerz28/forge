namespace Forge.Core.Configuration;

public class RunnerOptions
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int DeadTimeoutSeconds { get; set; } = 120;
}
