using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Domain.Services;

public static class AssetAssignmentRules
{
    public static bool HasOpenAssignment(IEnumerable<AssetAssignment> assignments, Guid assetId) =>
        assignments.Any(a => a.AssetId == assetId && a.ReturnedAt == null && a.Status != AssetAssignmentStatus.Rejected);

    public static bool CanAssign(Asset asset, IEnumerable<AssetAssignment> assignments) =>
        !asset.IsCancelled
        && asset.Status is not AssetStatus.Disposed and not AssetStatus.Lost
        && !HasOpenAssignment(assignments, asset.Id);

    public static bool RequiresEvidence(Asset asset) => asset.IsPrivileged;

    public static bool RequiresHandoverProof(Asset asset, string? evidenceRef, Guid? handoverFileId) =>
        !RequiresEvidence(asset) || AssetHandoverRules.HasProof(evidenceRef, handoverFileId);

    public static void SoftCancel(Asset asset, string? reason)
    {
        asset.IsCancelled = true;
        asset.CancelledAt = DateTime.UtcNow;
        asset.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء أصل" : reason.Trim();
    }
}
