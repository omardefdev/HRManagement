using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.RecruitmentKpi;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Infrastructure.Services.Recruitment.Analytics;

/// <summary>
/// HCM-032 (PLAN-32): filters of the recruitment reports. The period is <c>[From, To)</c> in UTC dates. The org-unit filter
/// covers the unit and its descendants and is intersected with the caller's scope; the competition filter is honoured for
/// unrestricted users only (a scoped user gets no data). The channel filter applies to application-based KPIs only.
/// </summary>
public sealed record RecruitmentMetricsFilter
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public Guid? OrgUnitId { get; init; }
    public Guid? RequisitionId { get; init; }
    public RecruitmentIntakeChannel? Channel { get; init; }
    public Guid? CompetitionId { get; init; }

    public DateTime FromUtc => From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    public DateTime ToUtc => To.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>The last <paramref name="months"/> full calendar months before the current UTC month.</summary>
    public static RecruitmentMetricsFilter DefaultPeriod(DateTime nowUtc, int months)
    {
        var to = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        return new RecruitmentMetricsFilter { From = to.AddMonths(-Math.Max(1, months)), To = to };
    }

    /// <summary>Every application of one requisition, whatever its date (requisition page).</summary>
    public static RecruitmentMetricsFilter ForRequisition(Guid requisitionId) =>
        new() { From = DateOnly.MinValue, To = DateOnly.MaxValue, RequisitionId = requisitionId };
}

/// <summary>
/// A duration KPI: median and P90 (nearest rank) in days or hours, the number of values, and how many values came from an
/// evidence fallback (<c>count-fallback-separately</c>). Coverage = values without any fallback ÷ n.
/// </summary>
public sealed record DurationMeasure(decimal? Median, decimal? P90, int N, int FallbackCount)
{
    public decimal? CoveragePercent => KpiStatistics.Coverage(N - FallbackCount, N);

    public static DurationMeasure From(IReadOnlyCollection<decimal> values, int fallbackCount) =>
        new(KpiStatistics.Median(values), KpiStatistics.P90(values), values.Count, fallbackCount);
}

public sealed record FunnelStageCount(RecruitmentFunnelStage Stage, int Count);

public sealed record StatusCount(RecruitmentApplicationStatus Status, int Count);

/// <summary><c>recruitment_funnel</c>; coverage = cohort applications with at least one event ÷ cohort.</summary>
public sealed record FunnelResult(
    int CohortCount,
    int CoveredCount,
    IReadOnlyList<FunnelStageCount> Stages,
    IReadOnlyList<StatusCount> CurrentStatuses)
{
    public decimal? CoveragePercent => KpiStatistics.Coverage(CoveredCount, CohortCount);
}

/// <summary>
/// <c>recruitment_time_to_fill</c>. <see cref="StartFallbackCount"/>: no <c>ApprovedDate</c> (from <c>RequestedDate</c>);
/// <see cref="HiredFallbackCount"/>: no Hired event (from the hired employee's creation).
/// </summary>
public sealed record TimeToFillResult(DurationMeasure Days, int StartFallbackCount, int HiredFallbackCount);

public sealed record StageTimeRow(RecruitmentApplicationStatus Status, decimal? MedianDays, int N);

/// <summary><c>recruitment_stage_time</c>: event-covered rows only.</summary>
public sealed record StageTimeResult(int CohortCount, int CoveredCount, IReadOnlyList<StageTimeRow> Rows)
{
    public decimal? CoveragePercent => KpiStatistics.Coverage(CoveredCount, CohortCount);
}

/// <summary><c>recruitment_offer_acceptance_rate</c> (revoked excluded; <see cref="Revoked"/> shown for transparency).</summary>
public sealed record OfferOutcomeResult(int Accepted, int Declined, int Expired, int Revoked)
{
    public int Decided => Accepted + Declined + Expired;
    public decimal? AcceptanceRate => KpiStatistics.Rate(Accepted, Decided);
}

public sealed record LabelCount(string Key, string Label, int Count);

/// <summary><c>recruitment_offer_decline_reasons</c>.</summary>
public sealed record DeclineReasonsResult(int Total, IReadOnlyList<LabelCount> ByCategory, IReadOnlyList<LabelCount> ByDeclinedBy);

public sealed record ChannelRow(RecruitmentIntakeChannel Channel, int Applied, int Hired)
{
    public decimal? HireRate => KpiStatistics.Rate(Hired, Applied);
}

/// <summary><c>recruitment_channel_mix</c>.</summary>
public sealed record ChannelMixResult(int CohortCount, IReadOnlyList<ChannelRow> Rows);

/// <summary><c>recruitment_appeal_rate</c>; objections (result objections, PLAN-31) are separate.</summary>
public sealed record AppealResult(
    int AppealsFiled,
    int AppealableRejections,
    IReadOnlyList<LabelCount> DecidedOutcomes,
    int ObjectionsFiled,
    int ObjectionsUpheld,
    int ObjectionsDismissed)
{
    public decimal? AppealRate => KpiStatistics.Rate(AppealsFiled, AppealableRejections);
}

/// <summary><c>recruitment_merit_overrides</c>.</summary>
public sealed record MeritOverrideResult(int OffersCreated, int MeritOverrides, int ExceptionOffers)
{
    public decimal? MeritOverrideRate => KpiStatistics.Rate(MeritOverrides, OffersCreated);
    public decimal? ExceptionRate => KpiStatistics.Rate(ExceptionOffers, OffersCreated);
}

public sealed record VacantPositionRow(
    Guid FundedPositionId,
    string PositionCode,
    string GradeCode,
    string OrgUnitName,
    DateTime VacantSince,
    int DaysVacant);

public sealed record StaleRequisitionRow(
    Guid RequisitionId,
    string RequisitionNumber,
    string JobTitle,
    string OrgUnitName,
    DateTime PublishedAt,
    int DaysPublished);

/// <summary><c>recruitment_open_vacancies</c> as of <see cref="AsOf"/>; lists hold the oldest <see cref="ListLimit"/>.</summary>
public sealed record OpenVacanciesResult(
    DateOnly AsOf,
    int StalePublishedDays,
    int VacantWithoutRequisitionCount,
    IReadOnlyList<VacantPositionRow> VacantWithoutRequisition,
    int StalePublishedCount,
    IReadOnlyList<StaleRequisitionRow> StalePublished,
    int ListLimit);

public sealed record OverdueApprovalRow(
    Guid WorkflowInstanceId,
    string WorkflowCode,
    string WorkflowName,
    string EntityType,
    Guid EntityId,
    string EntityLabel,
    int StepLevel,
    string StepName,
    string RoleRequired,
    DateTime StepStartedAt,
    decimal AgeHours,
    int SlaHours,
    ApprovalSlaSource SlaSource);

public sealed record StepDurationRow(
    string WorkflowCode,
    string WorkflowName,
    int StepLevel,
    string StepName,
    decimal? MedianHours,
    decimal? P90Hours,
    int N);

/// <summary>
/// <c>recruitment_approval_sla</c>. Bound: recruitment instances in scope that started before the period end and were in
/// progress or completed on/after the period start (<see cref="InstancesExamined"/>); only those histories are parsed.
/// </summary>
public sealed record ApprovalSlaResult(
    int InProgressCount,
    int OverdueCount,
    IReadOnlyList<OverdueApprovalRow> Overdue,
    IReadOnlyList<StepDurationRow> CompletedSteps,
    int InstancesExamined,
    int ListLimit);

public sealed record CompetitionRow(
    Guid CompetitionId,
    string Number,
    string Title,
    RecruitmentCompetitionStatus Status,
    int Applicants,
    int Admitted,
    int Present,
    int Passed,
    int Allocated,
    int Reserve,
    int Recalled,
    int ObjectionsFiled,
    int ObjectionsUpheld);

/// <summary><c>recruitment_competitions</c>; <see cref="Visible"/> is false for scoped users (decision 6).</summary>
public sealed record CompetitionsResult(bool Visible, IReadOnlyList<CompetitionRow> Rows);

public sealed record RequisitionTableRow(
    Guid RequisitionId,
    string RequisitionNumber,
    string JobTitle,
    string OrgUnitName,
    RequisitionStatus Status,
    DateTime RequestedDate,
    DateTime? ApprovedDate,
    DateTime? PublishedAt,
    DateTime? EndedAt,
    int AgeDays,
    int Applicants,
    IReadOnlyList<FunnelStageCount> Stages,
    int? TimeToFillDays);

/// <summary>Requisitions active in the period (open now, or requested / closed / cancelled in the period), oldest first.</summary>
public sealed record RequisitionTableResult(int Total, int Page, int PageSize, IReadOnlyList<RequisitionTableRow> Rows);

public sealed record RequisitionOfferRow(
    Guid OfferId,
    string OfferNumber,
    string ApplicationReference,
    string? CandidateName,
    OfferStatus Status,
    DateTime CreatedAt,
    DateTime? IssuedDate,
    DateTime? OutcomeAt,
    string? DeclineCategory,
    bool IsMeritOverride,
    bool IsExceptionOffer);

/// <summary>One requisition: funnel and stage time over all its applications, its offers, its time-to-fill.</summary>
public sealed record RequisitionDetailResult(
    RequisitionTableRow Requisition,
    FunnelResult Funnel,
    StageTimeResult StageTime,
    IReadOnlyList<RequisitionOfferRow> Offers);

public sealed record ApplicationDetailRow(
    Guid ApplicationId,
    string Reference,
    string? CandidateName,
    RecruitmentIntakeChannel Channel,
    string? RequisitionNumber,
    string? OrgUnitName,
    RecruitmentApplicationStatus Status,
    DateTime AppliedAt,
    DateTime? ScreenedAt,
    DateTime? RankedAt,
    DateTime? RejectedAt,
    DateTime? HiredAt,
    string? OfferOutcome);
