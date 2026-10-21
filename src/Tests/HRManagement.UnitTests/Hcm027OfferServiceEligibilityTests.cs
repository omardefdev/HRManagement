using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (A2): RecruitmentOfferService applies merit order and one active offer per requisition.</summary>
public class Hcm027OfferServiceEligibilityTests
{
    [Fact]
    public async Task Best_available_rank_ignores_rejected_declined_and_pending_appeal()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Rejected, 1);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.OfferDeclined, 2);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 3, appeal: RecruitmentAppealStatus.Pending);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Assessed, null);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 5);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 4);

        var best = await CreateOfferService(db).GetBestAvailableRankAsync(req.Id);

        Assert.Equal(4, best);
    }

    [Fact]
    public async Task Best_available_rank_is_null_without_candidates()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db);
        Assert.Null(await CreateOfferService(db).GetBestAvailableRankAsync(req.Id));
    }

    [Fact]
    public async Task Assessed_application_cannot_receive_an_offer()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Assessed, null);

        var (offer, error) = await CreateOfferService(db).CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);

        Assert.Null(offer);
        Assert.Equal(OfferEligibilityRules.NotRankedMessage, error);
    }

    [Fact]
    public async Task Second_parallel_offer_on_same_requisition_is_rejected()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);
        var service = CreateOfferService(db);

        var (offer1, e1) = await service.CreateDraftAsync(first.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);
        Assert.True(offer1 is not null, e1);

        var (offer2, e2) = await service.CreateDraftAsync(
            second.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null,
            isMeritOverride: true, meritOverrideReason: ValidOverrideReason);

        Assert.Null(offer2);
        Assert.Equal(OfferEligibilityRules.OtherActiveOfferMessage, e2);
    }

    [Fact]
    public async Task Rank_two_without_override_is_rejected_by_service()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);

        var (offer, error) = await CreateOfferService(db).CreateDraftAsync(second.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);

        Assert.Null(offer);
        Assert.Equal(OfferEligibilityRules.NotBestRankMessage(1), error);
    }

    [Fact]
    public async Task Override_flag_and_reason_are_persisted_and_application_moves_to_offer_extended()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);

        var (offer, error) = await CreateOfferService(db).CreateDraftAsync(
            second.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId,
            isMeritOverride: true, meritOverrideReason: "  " + ValidOverrideReason + "  ");
        Assert.True(offer is not null, error);

        db.ChangeTracker.Clear();
        var saved = await db.OfferLetters.SingleAsync();
        var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == second.Id);
        Assert.True(saved.IsMeritOverride);
        Assert.Equal(ValidOverrideReason, saved.MeritOverrideReason);
        Assert.Equal(14, saved.ValidityDays);
        Assert.Null(saved.ExpiryDate);
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, app.Status);
        Assert.Equal(saved.Id, app.ActiveOfferLetterId);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(61)]
    public async Task Validity_days_outside_range_are_rejected(int days)
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);

        var (offer, error) = await CreateOfferService(db).CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), days);

        Assert.Null(offer);
        Assert.Equal(OfferWorkflowRules.ValidityDaysRangeMessage, error);
    }
}
