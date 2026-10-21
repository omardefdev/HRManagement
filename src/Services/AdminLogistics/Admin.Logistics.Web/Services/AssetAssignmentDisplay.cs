using System.Text.RegularExpressions;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Services;
using HRManagement.SharedKernel.Localization;
using Microsoft.AspNetCore.Html;

namespace Admin.Logistics.Web.Services;

public static class AssetAssignmentDisplay
{
    private static readonly Regex LossHolderGuidRegex = new(
        @"حائز:\s*([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Renders return notes; legacy loss entries may embed a holder GUID — hydrate via data-hr-employee-id.
    /// </summary>
    public static IHtmlContent FormatReturnNotesHtml(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return new HtmlString("—");

        var html = LossHolderGuidRegex.Replace(
            System.Net.WebUtility.HtmlEncode(notes),
            m => $"حائز: <span data-hr-employee-id=\"{m.Groups[1].Value}\">{m.Groups[1].Value}</span>");
        return new HtmlString(html);
    }
    public static string FormatHandoverCondition(AssetAssignment assignment)
    {
        if (assignment.HandoverCondition is null)
            return string.IsNullOrWhiteSpace(assignment.HandoverConditionNotes) ? "—" : assignment.HandoverConditionNotes.Trim();

        var label = EnumDisplayExtensions.GetDisplayName(assignment.HandoverCondition.Value);
        if (string.IsNullOrWhiteSpace(assignment.HandoverConditionNotes))
            return label;

        return $"{label} — {assignment.HandoverConditionNotes.Trim()}";
    }

    public static string FormatEvidenceSummary(AssetAssignment assignment, Asset? asset = null)
    {
        asset ??= assignment.Asset;
        if (asset is null || !AssetAssignmentRules.RequiresEvidence(asset))
            return "غير مطلوب";

        if (!AssetHandoverRules.HasProof(assignment.EvidenceRef, assignment.HandoverFileId))
            return "—";

        var hasValidText = AssetHandoverRules.HasProof(assignment.EvidenceRef, null);
        var hasFile = assignment.HandoverFileId.HasValue;

        if (hasValidText && hasFile)
            return $"{assignment.EvidenceRef!.Trim()} · ملف مرفق";
        if (hasValidText)
            return assignment.EvidenceRef!.Trim();
        return "ملف مرفق";
    }

    public static bool RequiresEvidenceDisplay(AssetAssignment assignment, Asset? asset = null)
    {
        asset ??= assignment.Asset;
        return asset is not null && AssetAssignmentRules.RequiresEvidence(asset);
    }

    public static bool HasMissingRequiredEvidence(AssetAssignment assignment, Asset? asset = null)
    {
        asset ??= assignment.Asset;
        return asset is not null
               && AssetAssignmentRules.RequiresEvidence(asset)
               && !AssetHandoverRules.HasProof(assignment.EvidenceRef, assignment.HandoverFileId);
    }
}
