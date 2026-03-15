using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IDispatcher
{
    Task<AgentResult> DispatchInitialAsync(ForgeTask task, CancellationToken ct = default);
    Task<AgentResult> DispatchReviewAsync(ForgeTask task, List<GitHubReviewComment> comments, string prDiff, CancellationToken ct = default);
}
