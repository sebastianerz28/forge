namespace Forge.Core.Configuration;

public class GitHubOptions
{
    public string Owner { get; set; } = "";
    public string TokenEnv { get; set; } = "GITHUB_TOKEN";
    public string OwnerUsername { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 60;
    public int ReviewPollIntervalSeconds { get; set; } = 30;
}
