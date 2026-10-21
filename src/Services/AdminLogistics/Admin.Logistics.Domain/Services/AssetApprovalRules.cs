using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Domain.Services;

public static class AssetApprovalRules
{
    /// <summary>Degraded fallback: privileged classification is persisted from policy Classify.</summary>
    public static bool RequiresApproval(Asset asset) => asset.IsPrivileged;
}
