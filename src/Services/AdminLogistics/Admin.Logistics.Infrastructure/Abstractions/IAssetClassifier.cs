using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Infrastructure.Abstractions;

public interface IAssetClassifier
{
    /// <summary>PLAN-42: null when applied; otherwise why the policy refused it, and the asset is left as it was.</summary>
    Task<string?> ApplyClassificationAsync(Asset asset, CancellationToken ct = default);

    Task<int> ReclassifyAllAsync(CancellationToken ct = default);
}
