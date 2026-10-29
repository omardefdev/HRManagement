using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services.RecruitmentKpi;

/// <summary>The instant an application reached Hired, and whether it came from the evidence fallback.</summary>
public sealed record HiredTime(DateTime At, bool IsFallback);

/// <summary>One requisition's time-to-fill (null days when it was not filled in the period).</summary>
public sealed record TimeToFillResult(Guid RequisitionId, int Days, bool StartIsFallback, bool HiredIsFallback);

/// <summary>Days spent in one application status, between two consecutive status events.</summary>
public sealed record StageDuration(RecruitmentApplicationStatus Status, int Days);

/// <summary>
/// HCM-032 (PLAN-32, decisions 1–2): the time KPIs <c>recruitment_time_to_fill</c>, <c>recruitment_time_to_hire</c> and
/// <c>recruitment_stage_time</c>. Days are calendar days between UTC dates. Time-to-fill starts at the requisition's
/// <c>ApprovedDate</c> (fallback <c>RequestedDate</c>), aligned with the frozen STR-007 <c>hire_cycle_time</c> ("approved
/// vacancy"). The Hired instant is the <c>StatusChanged → Hired</c> event (fallback: the hired employee's <c>CreatedAt</c>);
/// every fallback is counted separately (anti-gaming <c>count-fallback-separately</c>).
/// </summary>
public static class TimeToFillRules
{
    /// <summary>The earliest <c>StatusChanged → Hired</c> event, else the hired employee's creation, else none.</summary>
    public static HiredTime? ResolveHiredTime(IEnumerable<FunnelEvent> events, DateTime? hiredEmployeeCreatedAt)
    {
        var hiredEvent = events
            .Where(e => e.EventType == RecruitmentApplicationEventTypes.StatusChanged
                && e.ToStatus == (int)RecruitmentApplicationStatus.Hired)
            .OrderBy(e => e.OccurredAt)
            .FirstOrDefault();
        if (hiredEvent is not null)
            return new HiredTime(hiredEvent.OccurredAt, false);
        return hiredEmployeeCreatedAt is { } created ? new HiredTime(created, true) : null;
    }

    public static (DateTime Start, bool IsFallback) ResolveFillStart(DateTime? approvedDate, DateTime requestedDate) =>
        approvedDate is { } approved ? (approved, false) : (requestedDate, true);

    /// <summary>
    /// Time-to-fill of one requisition: the first application to reach Hired fills it; null unless that Hired instant is in
    /// <c>[fromUtc, toUtc)</c>.
    /// </summary>
    public static TimeToFillResult? TimeToFill(
        Guid requisitionId,
        DateTime? approvedDate,
        DateTime requestedDate,
        IEnumerable<HiredTime> hiredTimesOfItsApplications,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var first = hiredTimesOfItsApplications.OrderBy(h => h.At).FirstOrDefault();
        if (first is null || first.At < fromUtc || first.At >= toUtc)
            return null;
        var (start, startFallback) = ResolveFillStart(approvedDate, requestedDate);
        return new TimeToFillResult(requisitionId, KpiStatistics.CalendarDays(start, first.At), startFallback, first.IsFallback);
    }

    public static int TimeToHireDays(DateTime appliedAt, HiredTime hired) => KpiStatistics.CalendarDays(appliedAt, hired.At);

    /// <summary>
    /// Days in each status between consecutive status events (<c>Received</c> and <c>StatusChanged</c>, by time). The last
    /// status has no end and is not counted. Rows without events have no stage times (event-covered rows only).
    /// </summary>
    public static IReadOnlyList<StageDuration> StageDurations(IEnumerable<FunnelEvent> events)
    {
        var statusEvents = events
            .Where(e => e.ToStatus.HasValue
                && (e.EventType == RecruitmentApplicationEventTypes.Received
                    || e.EventType == RecruitmentApplicationEventTypes.StatusChanged)
                && Enum.IsDefined(typeof(RecruitmentApplicationStatus), e.ToStatus.Value))
            .OrderBy(e => e.OccurredAt)
            .ToList();
        var result = new List<StageDuration>();
        for (var i = 0; i + 1 < statusEvents.Count; i++)
        {
            result.Add(new StageDuration(
                (RecruitmentApplicationStatus)statusEvents[i].ToStatus!.Value,
                KpiStatistics.CalendarDays(statusEvents[i].OccurredAt, statusEvents[i + 1].OccurredAt)));
        }
        return result;
    }
}
