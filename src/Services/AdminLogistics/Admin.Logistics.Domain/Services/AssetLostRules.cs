using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Localization;

namespace Admin.Logistics.Domain.Services;

public static class AssetLostRules
{
    public static bool RequiresLossApproval(Asset asset) =>
        AssetApprovalRules.RequiresApproval(asset);

    public static bool CanRequestLoss(Asset asset, AssetLossReport? pendingReport) =>
        !asset.IsCancelled
        && asset.Status is not AssetStatus.Disposed and not AssetStatus.Lost
        && pendingReport is null;

    public static bool CanRecover(Asset asset) =>
        !asset.IsCancelled && asset.Status == AssetStatus.Lost;

    public static AssetLossContext InferContext(Asset asset, IEnumerable<AssetAssignment> openAssignments) =>
        openAssignments.Any(a => a.AssetId == asset.Id && a.ReturnedAt == null)
            ? AssetLossContext.WithOpenAssignment
            : asset.Status == AssetStatus.Maintenance
                ? AssetLossContext.InMaintenance
                : AssetLossContext.InStorage;

    public static bool HasLossProof(Asset asset, string? evidenceRef, Guid? lossFileId) =>
        !RequiresLossApproval(asset) || AssetHandoverRules.HasProof(evidenceRef, lossFileId);

    public static string FormatReturnNotes(AssetLossReport report, string? holderDisplayName = null)
    {
        var reason = EnumDisplayExtensions.GetDisplayName(report.Reason);
        var context = EnumDisplayExtensions.GetDisplayName(report.Context);
        var parts = new List<string> { "[فقدان]", reason, context };
        if (!string.IsNullOrWhiteSpace(holderDisplayName))
            parts.Add($"حائز: {holderDisplayName.Trim()}");
        if (!string.IsNullOrWhiteSpace(report.EvidenceRef))
            parts.Add($"دليل: {report.EvidenceRef.Trim()}");
        if (!string.IsNullOrWhiteSpace(report.Notes))
            parts.Add(report.Notes.Trim());
        return string.Join(" — ", parts);
    }

    public static void ApplyLost(
        Asset asset,
        IList<AssetAssignment> openAssignments,
        AssetLossReport report,
        string? holderDisplayName = null)
    {
        var notes = FormatReturnNotes(report, holderDisplayName);
        var now = DateTime.UtcNow;

        foreach (var assignment in openAssignments)
        {
            assignment.ReturnedAt = now;
            assignment.Status = AssetAssignmentStatus.Returned;
            assignment.ReturnNotes = notes;
        }

        asset.Status = AssetStatus.Lost;
        report.Status = AssetLossReportStatus.Confirmed;
        report.ConfirmedLostAt = now;
        AssetLossStateCapture.CaptureAfter(asset, Array.Empty<AssetAssignment>(), report);
    }

    public static void Recover(Asset asset, AssetLossReport confirmedReport, string recoveryNotes)
    {
        asset.Status = AssetStatus.InStorage;
        confirmedReport.Status = AssetLossReportStatus.Recovered;
        confirmedReport.RecoveredAt = DateTime.UtcNow;
        confirmedReport.RecoveryNotes = recoveryNotes.Trim();
        AssetLossStateCapture.CaptureAfter(asset, Array.Empty<AssetAssignment>(), confirmedReport);
    }
}
