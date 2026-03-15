using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPoller
{
    Task<List<ForgeTask>> PollAsync(CancellationToken ct = default);
}
