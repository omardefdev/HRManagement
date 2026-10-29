using System.Globalization;
using System.Text.Json;
using HR.Core.Domain.Enums;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Services.Recruitment.Analytics;

/// <summary>
/// HCM-032 (PLAN-32): the recruitment applications detail CSV — one writer for the synchronous export and the
/// <c>hrcore.recruitment.applications.csv</c> export job. Columns: reference, name (only when the requester held
/// <c>HrCoreWrite</c>), channel, requisition, org unit, status, stage dates, offer outcome. Never a national id, not even
/// masked. Dates <c>yyyy-MM-dd</c> Gregorian and values invariant, whatever the culture.
/// </summary>
public static class RecruitmentApplicationsCsv
{
    public const string TemplateCode = HRManagement.SharedKernel.Exports.ExportJobTemplates.HrCoreRecruitmentApplicationsCsv;

    public static string Header(bool includeNames) =>
        includeNames
            ? "Reference,CandidateName,Channel,RequisitionNumber,OrgUnit,Status,AppliedAt,ScreenedAt,RankedAt,RejectedAt,HiredAt,OfferOutcome"
            : "Reference,Channel,RequisitionNumber,OrgUnit,Status,AppliedAt,ScreenedAt,RankedAt,RejectedAt,HiredAt,OfferOutcome";

    public static string Line(ApplicationDetailRow row, bool includeNames)
    {
        var cells = new List<string> { Cell(row.Reference) };
        if (includeNames)
            cells.Add(Cell(row.CandidateName));
        cells.Add(row.Channel.ToString());
        cells.Add(Cell(row.RequisitionNumber));
        cells.Add(Cell(row.OrgUnitName));
        cells.Add(row.Status.ToString());
        cells.Add(Date(row.AppliedAt));
        cells.Add(Date(row.ScreenedAt));
        cells.Add(Date(row.RankedAt));
        cells.Add(Date(row.RejectedAt));
        cells.Add(Date(row.HiredAt));
        cells.Add(row.OfferOutcome ?? "");
        return string.Join(',', cells);
    }

    public static string FileName(DateTime nowUtc) =>
        "recruitment-applications-" + nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv";

    private static string Date(DateTime? value) =>
        value is { } v ? v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // CSV injection guard: a leading formula character is neutralised.
        if (value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

/// <summary>
/// HCM-032: the filter and the request-time authorization stored on the export job (<c>FilterJson</c>). The scope is
/// captured as the allowed org units (null = unrestricted); <see cref="IncludeNames"/> records that the requester held
/// <c>HrCoreWrite</c> — the download re-checks it.
/// </summary>
public sealed record RecruitmentApplicationsExportFilter(
    string From,
    string To,
    Guid? OrgUnitId,
    Guid? RequisitionId,
    RecruitmentIntakeChannel? Channel,
    Guid? CompetitionId,
    IReadOnlyList<Guid>? AllowedOrgUnitIds,
    bool IncludeNames)
{
    public static RecruitmentApplicationsExportFilter Create(RecruitmentMetricsFilter filter, OrgAccessScope scope, bool includeNames) =>
        new(filter.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filter.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            filter.OrgUnitId, filter.RequisitionId, filter.Channel, filter.CompetitionId,
            scope.MustFilter ? scope.AllowedOrgUnitIds.OrderBy(id => id).ToList() : null, includeNames);

    public RecruitmentMetricsFilter ToFilter() => new()
    {
        From = DateOnly.ParseExact(From, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        To = DateOnly.ParseExact(To, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        OrgUnitId = OrgUnitId,
        RequisitionId = RequisitionId,
        Channel = Channel,
        CompetitionId = CompetitionId
    };

    public OrgAccessScope ToScope() =>
        AllowedOrgUnitIds is { } ids ? OrgAccessScope.Restricted(ids.ToHashSet()) : OrgAccessScope.Unrestricted();

    public static RecruitmentApplicationsExportFilter? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RecruitmentApplicationsExportFilter>(json);
}
