using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (C): decline, revoke and expiry always settle the application.</summary>
public class Hcm027OfferOutcomeTests
{
    [Fact]
    public async Task Decline_sets_application_offer_declined_and_clears_active_offer()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await CreateIssuedOfferAsync(db);

        var (success, error) = await CreateOfferService(db).DeclineAsync(offerId, "قبل عرضاً آخر", OfferDeclineReasonCategory.OtherOffer, OfferDeclinedBy.HrOnBehalf, CreatorId);
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        var app = await db.RecruitmentApplications.Include(a => a.OfferLetters).SingleAsync(a => a.Id == appId);
        Assert.Equal(OfferStatus.Declined, offer.Status);
        Assert.Equal("قبل عرضاً آخر", offer.DeclineReason);
        Assert.Equal(OfferDeclinedBy.HrOnBehalf, offer.DeclinedBy);
        Assert.NotNull(offer.DeclinedAt);
        Assert.Equal(RecruitmentApplicationStatus.OfferDeclined, app.Status);
        Assert.Null(app.ActiveOfferLetterId);
        Assert.Contains(app.OfferLetters, o => o.Id == offerId);
        Assert.True(await db.AuditLogs.AnyAsync(l => l.EntityId == offerId && l.Action == "DeclineOffer"));
    }

    [Theory]
    [InlineData(null, OfferDeclinedBy.HrOnBehalf)]
    [InlineData("لا", OfferDeclinedBy.HrOnBehalf)]
    [InlineData("قبل عرضاً آخر", "Someone")]
    public async Task Decline_requires_reason_and_known_decliner(string? reason, string declinedBy)
    {
        await using var db = CreateContext();
        var (offerId, _) = await CreateIssuedOfferAsync(db);

        var (success, _) = await CreateOfferService(db).DeclineAsync(offerId, reason, OfferDeclineReasonCategory.Other, declinedBy);

        Assert.False(success);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
    }

    [Theory]
    [InlineData(true, RecruitmentApplicationStatus.Ranked)]
    [InlineData(false, RecruitmentApplicationStatus.Rejected)]
    public async Task Revoke_reverts_to_ranked_or_rejects(bool revertToRanked, RecruitmentApplicationStatus expected)
    {
        await using var db = CreateContext();
        var (offerId, appId) = await CreateIssuedOfferAsync(db);

        var (success, error) = await CreateOfferService(db).RevokeAsync(offerId, "خطأ في الراتب المعروض", revertToRanked, CreatorId);
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == appId);
        Assert.Equal(OfferStatus.Revoked, offer.Status);
        Assert.Equal("خطأ في الراتب المعروض", offer.RevokeReason);
        Assert.NotNull(offer.RevokedAt);
        Assert.Equal(expected, app.Status);
        Assert.Null(app.ActiveOfferLetterId);
        Assert.Contains("إلغاء العرض", app.Notes);
    }

    [Fact]
    public async Task Revoke_requires_ten_character_reason()
    {
        await using var db = CreateContext();
        var (offerId, _) = await CreateIssuedOfferAsync(db);

        var (success, _) = await CreateOfferService(db).RevokeAsync(offerId, "قصير", revertToRanked: false);

        Assert.False(success);
    }

    [Fact]
    public async Task Revoking_pending_offer_cancels_its_workflow_instance()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await CreateOfferAsync(db, approve: false);

        var (success, error) = await CreateOfferService(db).RevokeAsync(offerId, "إلغاء قبل الاعتماد بسبب خطأ", revertToRanked: true);
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        var instance = await db.WorkflowInstances.SingleAsync(i => i.Id == offer.WorkflowInstanceId);
        Assert.Equal(OfferStatus.Revoked, offer.Status);
        Assert.Equal(WorkflowStatus.Cancelled, instance.Status);
        Assert.NotNull(instance.CompletedAt);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
    }

    [Fact]
    public async Task Expiry_date_is_set_at_issue_from_validity_days()
    {
        await using var db = CreateContext();
        var (offerId, _) = await CreateIssuedOfferAsync(db, validityDays: 7);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Issued, offer.Status);
        Assert.NotNull(offer.IssuedDate);
        Assert.Equal(7, offer.ValidityDays);
        var expected = offer.IssuedDate!.Value.Date.AddDays(7).AddDays(1).AddSeconds(-1);
        Assert.Equal(expected, offer.ExpiryDate!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Compute_expiry_is_end_of_utc_day()
    {
        var issued = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 4, 23, 59, 59, DateTimeKind.Utc), OfferWorkflowRules.ComputeExpiryDate(issued, 14));
    }

    [Fact]
    public async Task Accepting_after_expiry_is_rejected()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await CreateIssuedOfferAsync(db);
        await BackdateExpiryAsync(db, offerId);

        var (success, error) = await CreateOfferService(db).AcceptAsync(offerId);

        Assert.False(success);
        Assert.Contains("صلاحية", error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
    }

    [Fact]
    public async Task Expire_on_non_expired_offer_is_rejected()
    {
        await using var db = CreateContext();
        var (offerId, _) = await CreateIssuedOfferAsync(db);

        var (success, _) = await CreateOfferService(db).ExpireAsync(offerId, DateTime.UtcNow);

        Assert.False(success);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
    }

    [Fact]
    public async Task Expire_on_due_offer_settles_application()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await CreateIssuedOfferAsync(db);
        await BackdateExpiryAsync(db, offerId);

        var (success, error) = await CreateOfferService(db).ExpireAsync(offerId, DateTime.UtcNow);
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == appId);
        Assert.Equal(OfferStatus.Expired, offer.Status);
        Assert.NotNull(offer.ExpiredAt);
        Assert.Equal(RecruitmentApplicationStatus.OfferDeclined, app.Status);
        Assert.Null(app.ActiveOfferLetterId);
        Assert.Contains(RecruitmentOfferService.ExpiryNote, app.Notes);
    }

    [Fact]
    public async Task After_decline_next_candidate_by_merit_can_receive_an_offer()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await CreateIssuedOfferAsync(db);
        var service = CreateOfferService(db);
        Assert.True((await service.DeclineAsync(offerId, "قبل عرضاً آخر", OfferDeclineReasonCategory.OtherOffer, OfferDeclinedBy.HrOnBehalf)).Success);

        var reqId = (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == appId)).RequisitionId!.Value;
        var second = await db.RecruitmentApplications.SingleAsync(a => a.RequisitionId == reqId && a.Rank == 2);
        Assert.Equal(2, await service.GetBestAvailableRankAsync(reqId));

        var (offer, error) = await service.CreateDraftAsync(second.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);
        Assert.True(offer is not null, error);
    }

    internal static async Task BackdateExpiryAsync(HRCoreDbContext db, Guid offerId)
    {
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        offer.ExpiryDate = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    internal static Task<(Guid OfferId, Guid ApplicationId)> CreateIssuedOfferAsync(HRCoreDbContext db, int? validityDays = null) =>
        CreateOfferAsync(db, approve: true, validityDays);

    /// <summary>Rank 1 and rank 2 on one requisition; offer for rank 1, submitted (lab path) and optionally approved.</summary>
    internal static async Task<(Guid OfferId, Guid ApplicationId)> CreateOfferAsync(
        HRCoreDbContext db,
        bool approve,
        int? validityDays = null,
        string suffix = "A")
    {
        if (!await db.WorkflowDefinitions.AnyAsync())
            await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db, suffix);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1, "الأول");
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2, "الثاني");
        var service = CreateOfferService(db);

        var (offer, error) = await service.CreateDraftAsync(first.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), validityDays, CreatorId);
        Assert.True(offer is not null, error);
        var (submitted, submitError) = await service.SubmitForApprovalAsync(offer!.Id, CreatorId, "creator");
        Assert.True(submitted, submitError);

        if (approve)
        {
            var instanceId = (await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).WorkflowInstanceId!.Value;
            var actions = CreateActionService(db);
            foreach (var approver in new[] { ApproverA, ApproverB })
            {
                var result = await actions.ApproveAsync(
                    instanceId, new WorkflowActionActor(approver, "approver", "hr-management-approver"), "ok", null);
                Assert.True(result.Success, result.Error);
            }
        }

        db.ChangeTracker.Clear();
        return (offer.Id, first.Id);
    }
}
