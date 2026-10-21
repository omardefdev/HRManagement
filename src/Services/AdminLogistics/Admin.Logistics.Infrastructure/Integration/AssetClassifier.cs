using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.ServiceDefaults.Policy;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Integration;

public sealed class AssetClassifier(
    AdminLogisticsDbContext db,
    IPolicyEnforceClient policyClient) : IAssetClassifier
{
    public async Task ApplyClassificationAsync(Asset asset, CancellationToken ct = default)
    {
        var result = await policyClient.EnforceAsync(
            AssetPolicyFactsBuilder.ForClassify(asset, asset.TenantId),
            ct);

        asset.IsPrivileged = ParsePrivileged(result.Decision.GetSetFieldValue("isPrivileged"));
    }

    public async Task<int> ReclassifyAllAsync(CancellationToken ct = default)
    {
        var assets = await db.Assets.ToListAsync(ct);
        foreach (var asset in assets)
            await ApplyClassificationAsync(asset, ct);

        if (assets.Count > 0)
            await db.SaveChangesAsync(ct);

        return assets.Count;
    }

    private static bool ParsePrivileged(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
}
