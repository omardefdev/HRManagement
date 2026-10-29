using System.Globalization;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.RecruitmentKpi;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;

namespace HR.Core.Web.ViewModels;

/// <summary>
/// HCM-032 (PLAN-32): the filters of «تقارير التوظيف» as posted by the GET form. Dates are <c>yyyy-MM-dd</c> strings parsed
/// with the invariant culture (never the request culture — ar-SA would read a Hijri date).
/// </summary>
public sealed class RecruitmentReportFilterForm
{
    public string? From { get; set; }
    public string? To { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid? RequisitionId { get; set; }
    public RecruitmentIntakeChannel? Channel { get; set; }
    public Guid? CompetitionId { get; set; }
    public int Page { get; set; } = 1;

    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The filter, or the default period when a date is missing or invalid (and To ≤ From swaps nothing: default).</summary>
    public RecruitmentMetricsFilter ToFilter(RecruitmentMetricsFilter defaults)
    {
        var from = ParseDate(From) ?? defaults.From;
        var to = ParseDate(To) ?? defaults.To;
        if (to <= from)
            (from, to) = (defaults.From, defaults.To);
        return new RecruitmentMetricsFilter
        {
            From = from,
            To = to,
            OrgUnitId = OrgUnitId,
            RequisitionId = RequisitionId,
            Channel = Channel is { } channel && Enum.IsDefined(channel) ? channel : null,
            CompetitionId = CompetitionId
        };
    }

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

public sealed record ReportOption(string Value, string Label);

/// <summary>HCM-032: «تقارير التوظيف» (<c>RecruitmentReports/Index</c>).</summary>
public sealed class RecruitmentReportsViewModel
{
    public required RecruitmentMetricsFilter Filter { get; init; }
    public required FunnelResult Funnel { get; init; }
    public required HR.Core.Infrastructure.Services.Recruitment.Analytics.TimeToFillResult TimeToFill { get; init; }
    public required DurationMeasure TimeToHire { get; init; }
    public required StageTimeResult StageTime { get; init; }
    public required OfferOutcomeResult Offers { get; init; }
    public required DeclineReasonsResult Declines { get; init; }
    public required ChannelMixResult Channels { get; init; }
    public required AppealResult Appeals { get; init; }
    public required MeritOverrideResult Merit { get; init; }
    public required OpenVacanciesResult Vacancies { get; init; }
    public required ApprovalSlaResult Approvals { get; init; }
    public required CompetitionsResult Competitions { get; init; }
    public required RequisitionTableResult Requisitions { get; init; }

    public IReadOnlyList<ReportOption> OrgUnits { get; init; } = [];
    public IReadOnlyList<ReportOption> CompetitionOptions { get; init; } = [];
    public bool IsScoped { get; init; }
    public DateTime GeneratedAt { get; init; }
    public string DefinitionsVersion { get; init; } = RecruitmentMetricsService.DefinitionsVersion;

    /// <summary>Funnel and channel data for the chart script (read from a JSON block, CSP-safe).</summary>
    public string ChartJson { get; init; } = "{}";
}

/// <summary>HCM-032: one requisition (<c>RecruitmentReports/Requisition/{id}</c>).</summary>
public sealed class RecruitmentRequisitionReportViewModel
{
    public required RequisitionDetailResult Detail { get; init; }
    public bool ShowNames { get; init; }
    public DateTime GeneratedAt { get; init; }
    public string ChartJson { get; init; } = "{}";
}

/// <summary>Invariant, Gregorian formatting for the recruitment reports and their exports (culture-safe).</summary>
public static class RecruitmentReportFormat
{
    public static string Date(DateTime? value) =>
        value is { } v ? v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : KpiStatistics.NoValue;

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DateTimeUtc(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(int? value) => value is { } v ? Number(v) : KpiStatistics.NoValue;

    public static string Value(decimal? value) => KpiStatistics.Format(value);

    public static string Percent(decimal? value) => KpiStatistics.FormatPercent(value);

    /// <summary>Last day of the half-open period, for display («حتى» is inclusive).</summary>
    public static string PeriodLabel(RecruitmentMetricsFilter filter) =>
        $"{Date(filter.From)} — {Date(filter.To.AddDays(-1))}";

    public static string StageLabel(RecruitmentFunnelStage stage) =>
        HRManagement.SharedKernel.Localization.EnumDisplayExtensions.GetDisplayName(stage);

    public static string Label<TEnum>(TEnum value) where TEnum : struct, Enum =>
        HRManagement.SharedKernel.Localization.EnumDisplayExtensions.GetDisplayName(value);

    public static string SlaSourceLabel(ApprovalSlaSource source) => source switch
    {
        ApprovalSlaSource.StepTimeout => "مهلة الخطوة",
        ApprovalSlaSource.WorkflowOption => "سياسة المسار",
        _ => "السياسة الافتراضية"
    };

    /// <summary>The existing HR.Core screen of a workflow entity (drill-through).</summary>
    public static string EntityLink(OverdueApprovalRow row) => row.EntityType switch
    {
        "RecruitmentRequisition" => $"/RecruitmentRequisitions/Details/{row.EntityId}",
        "OfferLetter" => $"/OfferLetters/Details/{row.EntityId}",
        "RecruitmentApplication" => $"/RecruitmentApplications/Details/{row.EntityId}",
        "RecruitmentAppeal" => $"/RecruitmentAppeals/Details/{row.EntityId}",
        "SelectionCommitteeSession" => $"/SelectionCommittees/Details/{row.EntityId}",
        "RecruitmentCompetition" => $"/Competitions/Details/{row.EntityId}",
        "CompetitionResultVersion" when row.CompetitionId is { } competition => $"/Competitions/Details/{competition}",
        _ => $"/WorkflowInstances/Details/{row.WorkflowInstanceId}"
    };
}
