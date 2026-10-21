using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Localization;

namespace Admin.Logistics.Web.Services;

public static class AssetLossDisplay
{
    public static string FormatReport(AssetLossReport report)
    {
        var status = EnumDisplayExtensions.GetDisplayName(report.Status);
        var reason = EnumDisplayExtensions.GetDisplayName(report.Reason);
        var context = EnumDisplayExtensions.GetDisplayName(report.Context);
        var text = $"{status}: {reason} — {context}";
        if (!string.IsNullOrWhiteSpace(report.Notes))
            text += $" — {report.Notes.Trim()}";
        if (!string.IsNullOrWhiteSpace(report.EvidenceRef))
            text += $" (دليل: {report.EvidenceRef.Trim()})";
        if (report.RecoveredAt.HasValue && !string.IsNullOrWhiteSpace(report.RecoveryNotes))
            text += $" | استرداد: {report.RecoveryNotes.Trim()}";
        return text;
    }

    public static string FormatStateSnapshot(AssetLossReport report)
    {
        var parts = new List<string>();
        if (report.BeforeAssetStatus.HasValue)
            parts.Add($"قبل: {EnumDisplayExtensions.GetDisplayName(report.BeforeAssetStatus.Value)}");
        if (report.AfterAssetStatus.HasValue)
            parts.Add($"بعد: {EnumDisplayExtensions.GetDisplayName(report.AfterAssetStatus.Value)}");
        if (!string.IsNullOrWhiteSpace(report.BeforeStateJson))
            parts.Add($"لقطة قبل: {report.BeforeStateJson.Trim()}");
        if (!string.IsNullOrWhiteSpace(report.AfterStateJson))
            parts.Add($"لقطة بعد: {report.AfterStateJson.Trim()}");
        if (report.SettlementApprovedAt.HasValue)
            parts.Add($"اعتماد: {report.SettlementApprovedAt:yyyy/MM/dd}");
        return parts.Count == 0 ? "—" : string.Join(" | ", parts);
    }
}
