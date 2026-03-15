namespace Forge.Core.Interfaces;

public interface IReviewer
{
    Task CheckAndAddressReviewsAsync(CancellationToken ct = default);
}
