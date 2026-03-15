namespace Forge.Core.Models;

public class ForgeRun
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public Guid RunnerId { get; set; }
    public RunType RunType { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public string? TokenUsage { get; set; } // JSONB stored as string
    public bool? Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PromptHash { get; set; }
    public string? TargetRepo { get; set; } // "owner/repo" — denormalized for cross-repo queries

    // Navigation
    public ForgeTask? Task { get; set; }
    public ForgeRunner? Runner { get; set; }
}
