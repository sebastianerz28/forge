namespace Forge.Core.Models;

public class ForgeTask
{
    public int Id { get; set; }
    public int GitHubIssueNumber { get; set; }
    public string GitHubRepo { get; set; } = "";
    public ForgeTaskStatus Status { get; set; } = ForgeTaskStatus.Pending;
    public Guid? ClaimedBy { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public int? PrNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ForgeRunner? ClaimedByRunner { get; set; }
}
