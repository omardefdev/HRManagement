using Admin.Logistics.Domain.Entities;
using HRManagement.SharedKernel.Policy;

namespace Admin.Logistics.Infrastructure.Integration;

public static class AssetPolicyFactsBuilder
{
    public static Dictionary<string, string> ForClassifyFacts(Asset asset) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["value"] = (asset.Value ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["category"] = asset.Category ?? "",
            ["hasOriginReceipt"] = asset.OriginReceiptRecordId.HasValue.ToString().ToLowerInvariant(),
            ["isLegacyEntry"] = asset.IsLegacyEntry.ToString().ToLowerInvariant(),
            ["action"] = "Classify"
        };

    public static PolicyEnforceRequest ForClassify(Asset asset, Guid? tenantId) =>
        new()
        {
            Domain = PolicyDomains.AssetsCustody,
            TenantId = tenantId,
            SubjectType = "Asset",
            Action = "Classify",
            Facts = ForClassifyFacts(asset)
        };

    public static Dictionary<string, string> ForAsset(Asset asset, string action) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["isPrivileged"] = asset.IsPrivileged.ToString().ToLowerInvariant(),
            ["value"] = (asset.Value ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["category"] = asset.Category ?? "",
            ["hasOriginReceipt"] = asset.OriginReceiptRecordId.HasValue.ToString().ToLowerInvariant(),
            ["isLegacyEntry"] = asset.IsLegacyEntry.ToString().ToLowerInvariant(),
            ["action"] = action
        };

    public static PolicyEnforceRequest ForCreate(Asset asset, Guid? tenantId) =>
        new()
        {
            Domain = PolicyDomains.AssetsCustody,
            TenantId = tenantId,
            SubjectType = "Asset",
            Action = "Create",
            Facts = ForAsset(asset, "Create")
        };

    public static PolicyEnforceRequest ForAssign(Asset asset, Guid? tenantId, Guid entityId, string? contextJson) =>
        new()
        {
            Domain = PolicyDomains.AssetsCustody,
            TenantId = tenantId,
            SubjectType = "AssetAssignment",
            Action = "Assign",
            EntityId = entityId,
            ContextJson = contextJson,
            Facts = ForAsset(asset, "Assign")
        };
}
