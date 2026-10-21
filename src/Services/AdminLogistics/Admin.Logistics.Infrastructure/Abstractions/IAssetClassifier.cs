using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Infrastructure.Abstractions;

public interface IAssetClassifier
{
    Task ApplyClassificationAsync(Asset asset, CancellationToken ct = default);

    Task<int> ReclassifyAllAsync(CancellationToken ct = default);
}
