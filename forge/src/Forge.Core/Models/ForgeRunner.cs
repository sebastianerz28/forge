namespace Forge.Core.Models;

public class ForgeRunner
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastHeartbeat { get; set; }
    public DateTime RegisteredAt { get; set; }
    public RunnerStatus Status { get; set; } = RunnerStatus.Active;
}
