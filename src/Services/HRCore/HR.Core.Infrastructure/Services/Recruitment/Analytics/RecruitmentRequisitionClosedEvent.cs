using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.RecruitmentKpi;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Analytics;

/// <summary>HCM-032: the aggregate payload of <see cref="IRecruitmentRequisitionClosed"/>.</summary>
public sealed class RecruitmentRequisitionClosedMessage : IRecruitmentRequisitionClosed
{
    public Guid TenantId { get; init; }
    public Guid RequisitionId { get; init; }
    public Guid OrgUnitId { get; init; }
    public string Outcome { get; init; } = "";
    public DateTime ClosedAt { get; init; }
    public int Applicants { get; init; }
    public int HiredCount { get; init; }
    public int? TimeToFillDays { get; init; }
    public int ApplicantsPublic { get; init; }
    public int ApplicantsInternal { get; init; }
    public int ApplicantsHrEntered { get; init; }
    public int ApplicantsLegacy { get; init; }
    public int OffersAccepted { get; init; }
    public int OffersDeclined { get; init; }
    public int OffersExpired { get; init; }
    public int OffersRevoked { get; init; }
    public string? CorrelationId { get; init; }
    public string SourceSystem { get; init; } = "HR.Core";
    public int SchemaVersion => 1;
}

/// <summary>
/// HCM-032 (PLAN-32, P6): stages <see cref="IRecruitmentRequisitionClosed"/> on the HR.Core outbox in the same save as the
/// close — every close path calls it just before its <c>SaveChangesAsync</c> (hire closure, manual close, cancellation,
/// position freeze). The aggregates are the saved rows overlaid with the tracked, unsaved changes of that save (the hire,
/// the rejections, the revoked offers), so the message describes the requisition as committed. A hire made in this save
/// counts from now (its Hired event is recorded by the same save). No person-level data leaves HR.Core.
/// </summary>
public static class RecruitmentRequisitionClosedEvent
{
    public static async Task StageAsync(HRCoreDbContext db, IPublishEndpoint? publish, RecruitmentRequisition requisition, CancellationToken ct)
    {
        if (publish is null)
            return;
        var message = await BuildAsync(db, requisition, DateTime.UtcNow, ct);
        await publish.Publish<IRecruitmentRequisitionClosed>(message, ct);
    }

    public static async Task<RecruitmentRequisitionClosedMessage> BuildAsync(
        HRCoreDbContext db, RecruitmentRequisition requisition, DateTime nowUtc, CancellationToken ct)
    {
        var requisitionId = requisition.Id;

        // Applications: saved rows overlaid with tracked entries (status, hire) of this save.
        var apps = (await db.RecruitmentApplications.AsNoTracking()
                .Where(a => a.RequisitionId == requisitionId)
                .Select(a => new AppRow(a.Id, a.IntakeChannel, a.Status, a.HiredEmployeeId, false))
                .ToListAsync(ct))
            .ToDictionary(a => a.Id);
        foreach (var entry in db.ChangeTracker.Entries<RecruitmentApplication>())
        {
            var app = entry.Entity;
            if (entry.State == EntityState.Deleted)
            {
                apps.Remove(app.Id);
                continue;
            }
            if (app.RequisitionId != requisitionId)
            {
                apps.Remove(app.Id);
                continue;
            }
            var hiredNow = app.Status == RecruitmentApplicationStatus.Hired
                && (entry.State == EntityState.Added || entry.Property(a => a.Status).IsModified);
            apps[app.Id] = new AppRow(app.Id, app.IntakeChannel, app.Status, app.HiredEmployeeId, hiredNow);
        }

        var hired = apps.Values.Where(a => a.Status == RecruitmentApplicationStatus.Hired || a.HiredEmployeeId.HasValue).ToList();
        var hiredIds = hired.Select(a => a.Id).ToArray();
        const int hiredStatus = (int)RecruitmentApplicationStatus.Hired;
        var hiredEvents = await db.RecruitmentApplicationEvents.AsNoTracking()
            .Where(e => hiredIds.Contains(e.RecruitmentApplicationId)
                && e.EventType == RecruitmentApplicationEventTypes.StatusChanged && e.ToStatus == hiredStatus)
            .GroupBy(e => e.RecruitmentApplicationId)
            .Select(g => new { Id = g.Key, At = g.Min(e => e.OccurredAt) })
            .ToDictionaryAsync(e => e.Id, e => e.At, ct);
        var employeeIds = hired.Where(a => a.HiredEmployeeId.HasValue).Select(a => a.HiredEmployeeId!.Value).ToArray();
        var employeeCreated = await db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.CreatedAt, ct);
        foreach (var entry in db.ChangeTracker.Entries<Employee>().Where(e => e.State == EntityState.Added))
            employeeCreated[entry.Entity.Id] = entry.Entity.CreatedAt;

        var hireTimes = new List<HiredTime>();
        foreach (var app in hired)
        {
            if (hiredEvents.TryGetValue(app.Id, out var at))
                hireTimes.Add(new HiredTime(at, false));
            else if (app.HiredNow)
                hireTimes.Add(new HiredTime(nowUtc, false));
            else if (app.HiredEmployeeId is { } employee && employeeCreated.TryGetValue(employee, out var created))
                hireTimes.Add(new HiredTime(created, true));
        }
        int? timeToFill = null;
        if (hireTimes.Count > 0)
        {
            var (start, _) = TimeToFillRules.ResolveFillStart(requisition.ApprovedDate, requisition.RequestedDate);
            timeToFill = KpiStatistics.CalendarDays(start, hireTimes.Min(h => h.At));
        }

        // Offers of these applications: saved rows overlaid with tracked entries.
        var appIds = apps.Keys.ToArray();
        var offers = (await db.OfferLetters.AsNoTracking()
                .Where(o => appIds.Contains(o.RecruitmentApplicationId))
                .Select(o => new { o.Id, o.Status })
                .ToListAsync(ct))
            .ToDictionary(o => o.Id, o => o.Status);
        foreach (var entry in db.ChangeTracker.Entries<OfferLetter>())
        {
            if (entry.State == EntityState.Deleted || !apps.ContainsKey(entry.Entity.RecruitmentApplicationId))
                offers.Remove(entry.Entity.Id);
            else
                offers[entry.Entity.Id] = entry.Entity.Status;
        }

        var orgUnitId = requisition.FundedPosition?.OrgUnitId
            ?? await db.FundedPositions.AsNoTracking()
                .Where(p => p.Id == requisition.FundedPositionId)
                .Select(p => p.OrgUnitId)
                .FirstOrDefaultAsync(ct);

        int Channel(RecruitmentIntakeChannel channel) => apps.Values.Count(a => a.Channel == channel);
        int Offers(OfferStatus status) => offers.Values.Count(s => s == status);
        return new RecruitmentRequisitionClosedMessage
        {
            TenantId = requisition.TenantId,
            RequisitionId = requisitionId,
            OrgUnitId = orgUnitId,
            Outcome = requisition.Status == RequisitionStatus.Cancelled ? "Cancelled" : "Closed",
            ClosedAt = requisition.Status == RequisitionStatus.Cancelled
                ? requisition.CancelledAt ?? requisition.ClosedDate ?? nowUtc
                : requisition.ClosedDate ?? nowUtc,
            Applicants = apps.Count,
            HiredCount = hired.Count,
            TimeToFillDays = timeToFill,
            ApplicantsPublic = Channel(RecruitmentIntakeChannel.Public),
            ApplicantsInternal = Channel(RecruitmentIntakeChannel.Internal),
            ApplicantsHrEntered = Channel(RecruitmentIntakeChannel.HrEntered),
            ApplicantsLegacy = Channel(RecruitmentIntakeChannel.Legacy),
            OffersAccepted = Offers(OfferStatus.Accepted),
            OffersDeclined = Offers(OfferStatus.Declined),
            OffersExpired = Offers(OfferStatus.Expired),
            OffersRevoked = Offers(OfferStatus.Revoked),
            CorrelationId = CorrelationIdFlow.Get() ?? Guid.NewGuid().ToString(),
            SourceSystem = "HR.Core"
        };
    }

    private sealed record AppRow(Guid Id, RecruitmentIntakeChannel Channel, RecruitmentApplicationStatus Status, Guid? HiredEmployeeId, bool HiredNow);
}
