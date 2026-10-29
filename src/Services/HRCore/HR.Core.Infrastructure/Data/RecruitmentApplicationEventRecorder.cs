using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// HCM-030 (B1): turns tracked recruitment changes into <see cref="RecruitmentApplicationEvent"/> rows added to the same
/// save. It reads the change tracker only (no queries), so every path — controllers, jobs, cascades, hire closure,
/// reserve list, appeals — is covered by <see cref="HRCoreDbContext.SaveChanges()"/> and
/// <see cref="HRCoreDbContext.SaveChangesAsync(CancellationToken)"/>.
/// <list type="table">
/// <item><term>Received</term><description><see cref="RecruitmentApplication"/> added (to = its status).</description></item>
/// <item><term>StatusChanged</term><description><see cref="RecruitmentApplication.Status"/> modified (from/to).</description></item>
/// <item><term>InterviewScheduled / InterviewRescheduled</term><description>slot added; rescheduled when <c>RescheduledFromSlotId</c> is set.</description></item>
/// <item><term>InterviewCancelled</term><description>slot status modified to Cancelled.</description></item>
/// <item><term>OfferIssued … OfferRevoked</term><description><see cref="OfferLetter.Status"/> modified to Issued/Accepted/Declined/Expired/Revoked.</description></item>
/// <item><term>AppealFiled / AppealDecided</term><description>appeal added / its status leaves Pending.</description></item>
/// </list>
/// </summary>
internal static class RecruitmentApplicationEventRecorder
{
    public static void Record(HRCoreDbContext db)
    {
        if (db.ApplicationEventsSuppressed)
            return;

        var now = DateTime.UtcNow;
        var entries = db.ChangeTracker.Entries().ToList();

        // A save retried after a failure still tracks the events of the failed attempt: never add them twice.
        var pending = entries
            .Where(e => e.State == EntityState.Added && e.Entity is RecruitmentApplicationEvent)
            .Select(e => Key((RecruitmentApplicationEvent)e.Entity))
            .ToHashSet();

        foreach (var entry in entries)
        {
            var recorded = entry.Entity switch
            {
                RecruitmentApplication app => FromApplication(entry, app),
                RecruitmentInterviewSlot slot => FromSlot(entry, slot),
                OfferLetter offer => FromOffer(entry, offer),
                RecruitmentAppeal appeal => FromAppeal(entry, appeal),
                _ => null
            };
            if (recorded is null)
                continue;

            // COM-030: CandidateNotificationDispatcher.PlanAsync plans messages for rows with NotificationsPlannedAt IS NULL.
            recorded.OccurredAt = now;
            if (pending.Add(Key(recorded)))
                db.RecruitmentApplicationEvents.Add(recorded);
        }
    }

    private static RecruitmentApplicationEvent? FromApplication(EntityEntry entry, RecruitmentApplication app)
    {
        if (entry.State == EntityState.Added)
            return New(app.TenantId, app.Id, RecruitmentApplicationEventTypes.Received, null, (int)app.Status, null);

        if (entry.State != EntityState.Modified)
            return null;
        var status = entry.Property(nameof(RecruitmentApplication.Status));
        if (!status.IsModified || Equals(status.OriginalValue, status.CurrentValue))
            return null;
        return New(app.TenantId, app.Id, RecruitmentApplicationEventTypes.StatusChanged,
            (int)(RecruitmentApplicationStatus)status.OriginalValue!, (int)app.Status, null);
    }

    private static RecruitmentApplicationEvent? FromSlot(EntityEntry entry, RecruitmentInterviewSlot slot)
    {
        if (entry.State == EntityState.Added)
        {
            var type = slot.RescheduledFromSlotId is null
                ? RecruitmentApplicationEventTypes.InterviewScheduled
                : RecruitmentApplicationEventTypes.InterviewRescheduled;
            return New(slot.TenantId, slot.RecruitmentApplicationId, type, null, (int)slot.Status, slot.Id);
        }

        if (ModifiedTo(entry, nameof(RecruitmentInterviewSlot.Status), out var from)
            && slot.Status == InterviewSlotStatus.Cancelled)
            return New(slot.TenantId, slot.RecruitmentApplicationId, RecruitmentApplicationEventTypes.InterviewCancelled,
                (int)(InterviewSlotStatus)from!, (int)slot.Status, slot.Id);
        return null;
    }

    private static RecruitmentApplicationEvent? FromOffer(EntityEntry entry, OfferLetter offer)
    {
        if (!ModifiedTo(entry, nameof(OfferLetter.Status), out var from))
            return null;

        var type = offer.Status switch
        {
            OfferStatus.Issued => RecruitmentApplicationEventTypes.OfferIssued,
            OfferStatus.Accepted => RecruitmentApplicationEventTypes.OfferAccepted,
            OfferStatus.Declined => RecruitmentApplicationEventTypes.OfferDeclined,
            OfferStatus.Expired => RecruitmentApplicationEventTypes.OfferExpired,
            OfferStatus.Revoked => RecruitmentApplicationEventTypes.OfferRevoked,
            _ => null
        };
        return type is null
            ? null
            : New(offer.TenantId, offer.RecruitmentApplicationId, type, (int)(OfferStatus)from!, (int)offer.Status, offer.Id);
    }

    private static RecruitmentApplicationEvent? FromAppeal(EntityEntry entry, RecruitmentAppeal appeal)
    {
        if (entry.State == EntityState.Added)
            return New(appeal.TenantId, appeal.RecruitmentApplicationId, RecruitmentApplicationEventTypes.AppealFiled,
                null, (int)appeal.Status, appeal.Id);

        if (ModifiedTo(entry, nameof(RecruitmentAppeal.Status), out var from)
            && (RecruitmentAppealStatus)from! == RecruitmentAppealStatus.Pending)
            return New(appeal.TenantId, appeal.RecruitmentApplicationId, RecruitmentApplicationEventTypes.AppealDecided,
                (int)RecruitmentAppealStatus.Pending, (int)appeal.Status, appeal.Id);
        return null;
    }

    /// <summary>The property of a modified entity changed value; <paramref name="from"/> is its original value.</summary>
    private static bool ModifiedTo(EntityEntry entry, string property, out object? from)
    {
        from = null;
        if (entry.State != EntityState.Modified)
            return false;
        var prop = entry.Property(property);
        if (!prop.IsModified || Equals(prop.OriginalValue, prop.CurrentValue))
            return false;
        from = prop.OriginalValue;
        return true;
    }

    private static RecruitmentApplicationEvent New(
        Guid tenantId, Guid applicationId, string type, int? from, int? to, Guid? related) => new()
    {
        // Taken from the changed entity: on the anonymous public portal it is the only tenant available.
        TenantId = tenantId,
        RecruitmentApplicationId = applicationId,
        EventType = type,
        FromStatus = from,
        ToStatus = to,
        RelatedEntityId = related
    };

    private static (Guid, string, int?, int?, Guid?) Key(RecruitmentApplicationEvent e) =>
        (e.RecruitmentApplicationId, e.EventType, e.FromStatus, e.ToStatus, e.RelatedEntityId);
}
