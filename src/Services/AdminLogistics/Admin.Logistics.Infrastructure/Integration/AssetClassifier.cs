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
    public async Task<string?> ApplyClassificationAsync(Asset asset, CancellationToken ct = default)
    {
        var result = await policyClient.EnforceAsync(
            AssetPolicyFactsBuilder.ForClassify(asset, asset.TenantId),
            ct);

        // PLAN-42: a refusal or an outage carries no classification. Reading it as "not privileged" would drop the
        // evidence and the approval this asset needs, so the caller stops instead.
        if (!result.Allowed)
            return result.Decision.DenyReason ?? "مرفوض بسياسة العهدة";

        asset.IsPrivileged = ParsePrivileged(result.Decision.GetSetFieldValue("isPrivileged"));
        return null;
    }

    public async Task<int> ReclassifyAllAsync(CancellationToken ct = default)
    {
        var assets = await db.Assets.ToListAsync(ct);
        var applied = 0;
        foreach (var asset in assets)
        {
            if (await ApplyClassificationAsync(asset, ct) is null)
                applied++;
        }

        if (applied > 0)
            await db.SaveChangesAsync(ct);

        return applied;
    }

    private static bool ParsePrivileged(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
}
