using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>HCM-030 (B1): every application, interview, offer and appeal change leaves exactly one event row.</summary>
public class Hcm030ApplicationEventLogTests
{
    private static async Task<(HRCoreDbContext Db, RecruitmentApplication App)> SeedAsync(
        RecruitmentApplicationStatus status = RecruitmentApplicationStatus.Submitted)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var app = new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "مرشح",
            NationalId = "0909090909",
            RequisitionId = fx.Requisition.Id,
            AppliedPosition = "محلل نظم",
            AppliedAt = DateTime.UtcNow,
            Status = status
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        return (db, app);
    }

    private static Task<List<RecruitmentApplicationEvent>> EventsAsync(HRCoreDbContext db, string? type = null) =>
        db.RecruitmentApplicationEvents.AsNoTracking()
            .Where(e => type == null || e.EventType == type)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

    [Fact]
    public async Task Added_application_records_Received()
    {
        var (db, app) = await SeedAsync();
        await using var _ = db;

        var e = Assert.Single(await EventsAsync(db));
        Assert.Equal(RecruitmentApplicationEventTypes.Received, e.EventType);
        Assert.Equal(app.Id, e.RecruitmentApplicationId);
        Assert.Null(e.FromStatus);
        Assert.Equal((int)RecruitmentApplicationStatus.Submitted, e.ToStatus);
        Assert.Equal(Hcm030TestSupport.TenantA, e.TenantId);
        Assert.Null(e.NotificationsPlannedAt);
    }

    [Fact]
    public async Task Status_change_through_the_selection_service_records_StatusChanged()
    {
        var (db, app) = await SeedAsync();
        await using var _ = db;

        var (success, _) = await new RecruitmentSelectionService(db)
            .TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.UnderReview, false);

        Assert.True(success);
        var e = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.StatusChanged));
        Assert.Equal((int)RecruitmentApplicationStatus.Submitted, e.FromStatus);
        Assert.Equal((int)RecruitmentApplicationStatus.UnderReview, e.ToStatus);
    }

    [Fact]
    public async Task Save_without_a_status_change_records_nothing()
    {
        var (db, app) = await SeedAsync();
        await using var _ = db;

        app.Notes = "ملاحظة";
        app.Status = RecruitmentApplicationStatus.Submitted;
        await db.SaveChangesAsync();

        Assert.Single(await EventsAsync(db));
    }

    [Fact]
    public async Task Synchronous_save_records_too()
    {
        var (db, app) = await SeedAsync();
        await using var _ = db;

        app.Status = RecruitmentApplicationStatus.Withdrawn;
        db.SaveChanges();

        Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.StatusChanged));
    }

    [Fact]
    public async Task Interview_scheduled_rescheduled_and_cancelled_each_record_one_row()
    {
        var (db, app) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _ = db;
        var interviews = new RecruitmentInterviewService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));
        var slot = new RecruitmentInterviewSlot
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = app.Id,
            ScheduledAt = DateTime.UtcNow.AddDays(2),
            Status = InterviewSlotStatus.Scheduled
        };
        db.RecruitmentInterviewSlots.Add(slot);
        await db.SaveChangesAsync();

        var (replacement, rescheduleError) = await interviews.RescheduleAsync(slot.Id, DateTime.UtcNow.AddDays(4), "تعارض في جدول اللجنة");
        Assert.Null(rescheduleError);
        var (cancelled, cancelError) = await interviews.CancelAsync(replacement!.Id, "اعتذار المرشح");
        Assert.True(cancelled, cancelError);

        var scheduled = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.InterviewScheduled));
        Assert.Equal(slot.Id, scheduled.RelatedEntityId);
        var rescheduled = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.InterviewRescheduled));
        Assert.Equal(replacement.Id, rescheduled.RelatedEntityId);
        var cancel = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.InterviewCancelled));
        Assert.Equal(replacement.Id, cancel.RelatedEntityId);
        Assert.Equal((int)InterviewSlotStatus.Scheduled, cancel.FromStatus);
        Assert.Equal((int)InterviewSlotStatus.Cancelled, cancel.ToStatus);
        // The original slot moving to Rescheduled is not a cancellation.
        Assert.Equal(4, (await EventsAsync(db)).Count);
    }

    [Theory]
    [InlineData(OfferStatus.Issued, RecruitmentApplicationEventTypes.OfferIssued)]
    [InlineData(OfferStatus.Accepted, RecruitmentApplicationEventTypes.OfferAccepted)]
    [InlineData(OfferStatus.Declined, RecruitmentApplicationEventTypes.OfferDeclined)]
    [InlineData(OfferStatus.Expired, RecruitmentApplicationEventTypes.OfferExpired)]
    [InlineData(OfferStatus.Revoked, RecruitmentApplicationEventTypes.OfferRevoked)]
    public async Task Offer_status_change_records_its_event(OfferStatus to, string expectedType)
    {
        var (db, app) = await SeedAsync(RecruitmentApplicationStatus.Ranked);
        await using var _ = db;
        var from = to == OfferStatus.Issued ? OfferStatus.PendingApproval : OfferStatus.Issued;
        var offer = new OfferLetter
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = app.Id,
            FundedPositionId = (await db.FundedPositions.FirstAsync()).Id,
            OfferNumber = "OFF-H30-1",
            Status = from,
            ProposedStartDate = DateTime.UtcNow.AddDays(30)
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();

        offer.Status = to;
        await db.SaveChangesAsync();

        Assert.DoesNotContain(await EventsAsync(db), e => e.EventType.StartsWith("Offer") && e.EventType != expectedType);
        var e = Assert.Single(await EventsAsync(db, expectedType));
        Assert.Equal((int)from, e.FromStatus);
        Assert.Equal((int)to, e.ToStatus);
        Assert.Equal(offer.Id, e.RelatedEntityId);
        Assert.Equal(app.Id, e.RecruitmentApplicationId);
    }

    [Fact]
    public async Task Draft_offer_creation_and_submission_record_no_offer_event()
    {
        var (db, app) = await SeedAsync(RecruitmentApplicationStatus.Ranked);
        await using var _ = db;
        var offer = new OfferLetter
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = app.Id,
            FundedPositionId = (await db.FundedPositions.FirstAsync()).Id,
            OfferNumber = "OFF-H30-2",
            Status = OfferStatus.Draft,
            ProposedStartDate = DateTime.UtcNow.AddDays(30)
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        offer.Status = OfferStatus.PendingApproval;
        await db.SaveChangesAsync();

        Assert.DoesNotContain(await EventsAsync(db), e => e.EventType.StartsWith("Offer"));
    }

    [Fact]
    public async Task Appeal_filed_and_decided_record_one_row_each()
    {
        var (db, app) = await SeedAsync(RecruitmentApplicationStatus.Rejected);
        await using var _ = db;
        var appeal = new RecruitmentAppeal
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = app.Id,
            Reason = "لم تُحتسب خبرتي السابقة في التقييم كما يجب"
        };
        db.RecruitmentAppeals.Add(appeal);
        await db.SaveChangesAsync();
        appeal.Decision = "مراجعة";
        await db.SaveChangesAsync();
        appeal.Status = RecruitmentAppealStatus.Decided;
        await db.SaveChangesAsync();

        var filed = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.AppealFiled));
        Assert.Equal(appeal.Id, filed.RelatedEntityId);
        var decided = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.AppealDecided));
        Assert.Equal((int)RecruitmentAppealStatus.Pending, decided.FromStatus);
        Assert.Equal((int)RecruitmentAppealStatus.Decided, decided.ToStatus);
    }

    [Fact]
    public async Task Suppression_records_nothing_and_is_restored_after_the_scope()
    {
        var (db, app) = await SeedAsync();
        await using var _ = db;

        using (db.SuppressApplicationEvents())
        {
            app.Status = RecruitmentApplicationStatus.UnderReview;
            await db.SaveChangesAsync();
        }
        Assert.False(db.ApplicationEventsSuppressed);
        app.Status = RecruitmentApplicationStatus.Shortlisted;
        await db.SaveChangesAsync();

        var e = Assert.Single(await EventsAsync(db, RecruitmentApplicationEventTypes.StatusChanged));
        Assert.Equal((int)RecruitmentApplicationStatus.UnderReview, e.FromStatus);
    }

    [Fact]
    public async Task Public_apply_records_Received_with_the_posting_tenant()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);

        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id));

        var e = Assert.Single(await EventsAsync(db));
        Assert.Equal(RecruitmentApplicationEventTypes.Received, e.EventType);
        Assert.Equal(Hcm030TestSupport.TenantA, e.TenantId);
    }

    [Fact]
    public void Event_row_has_no_personal_data_columns()
    {
        var columns = typeof(RecruitmentApplicationEvent).GetProperties().Select(p => p.Name).OrderBy(n => n);

        Assert.Equal(
            new[]
            {
                "CreatedAt", "EventType", "FromStatus", "Id", "NotificationsPlannedAt", "OccurredAt",
                "RecruitmentApplicationId", "RelatedEntityId", "TenantId", "ToStatus", "UpdatedAt"
            },
            columns);
    }
}
