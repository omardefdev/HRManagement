using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;
using FakeCandidateDocumentClient = HRManagement.UnitTests.Hcm030TestSupport.FakeCandidateDocumentClient;

namespace HRManagement.UnitTests;

/// <summary>HCM-032 (PLAN-32, decision 4): every new offer decline carries a category; old declines are untouched.</summary>
public class Hcm032DeclineCategoryTests
{
    [Fact]
    public async Task Hr_decline_without_category_is_refused_and_changes_nothing()
    {
        await using var db = CreateContext();
        var (offerId, _) = await Hcm030OnBehalfAcceptanceTests.IssueOfferAsync(db);

        var result = await Hcm030OnBehalfAcceptanceTests.CreateService(db, new FakeCandidateDocumentClient())
            .DeclineAsync(offerId, "قبل عرضاً آخر", category: null, evidence: null, actorUserId: null);

        Assert.False(result.Success);
        Assert.Equal(RecruitmentOfferService.DeclineCategoryRequiredMessage, result.Error);
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Issued, offer.Status);
        Assert.Null(offer.DeclineReasonCategory);
    }

    [Fact]
    public async Task Hr_decline_stores_the_category_and_keeps_the_text()
    {
        await using var db = CreateContext();
        var (offerId, _) = await Hcm030OnBehalfAcceptanceTests.IssueOfferAsync(db);

        var result = await Hcm030OnBehalfAcceptanceTests.CreateService(db, new FakeCandidateDocumentClient())
            .DeclineAsync(offerId, "الراتب أقل من المتوقع", OfferDeclineReasonCategory.Salary, evidence: null, actorUserId: null);

        Assert.True(result.Success, result.Error);
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferDeclineReasonCategory.Salary, offer.DeclineReasonCategory);
        Assert.Equal("الراتب أقل من المتوقع", offer.DeclineReason);
        Assert.Equal(OfferDeclinedBy.HrOnBehalf, offer.DeclinedBy);
    }

    [Fact]
    public async Task Undefined_category_value_is_refused()
    {
        await using var db = CreateContext();
        var (offerId, _) = await Hcm027OfferOutcomeTests.CreateIssuedOfferAsync(db);

        var (success, error) = await CreateOfferService(db)
            .DeclineAsync(offerId, "قبل عرضاً آخر", (OfferDeclineReasonCategory)99, OfferDeclinedBy.HrOnBehalf);

        Assert.False(success);
        Assert.Equal(RecruitmentOfferService.DeclineCategoryRequiredMessage, error);
    }

    [Fact]
    public async Task Portal_decline_without_category_is_refused_on_the_category_field()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await Hcm030CandidatePortalTests.LinkCitizenAsync(db, appId);
        var controller = Hcm030CandidatePortalTests.CreateController(db, session);

        var result = await controller.DeclineOffer(
            new CandidatePortalDeclineForm { Text = "قبلت عرضاً من جهة أخرى", Category = null }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Decline.Category"));
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
    }

    [Fact]
    public async Task Portal_decline_stores_the_category_for_the_candidate()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await Hcm030CandidatePortalTests.LinkCitizenAsync(db, appId);

        var result = await Hcm030CandidatePortalTests.CreatePortal(db)
            .DeclineOfferAsync(session, "مكان العمل بعيد عن السكن", OfferDeclineReasonCategory.Location);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferDeclineReasonCategory.Location, offer.DeclineReasonCategory);
        Assert.Equal(OfferDeclinedBy.Candidate, offer.DeclinedBy);
    }

    [Fact]
    public async Task Withdrawal_with_an_issued_offer_declines_it_as_other_with_its_text()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await Hcm030CandidatePortalTests.LinkCitizenAsync(db, appId);

        var result = await Hcm030CandidatePortalTests.CreatePortal(db).WithdrawAsync(session, "ظروف عائلية", confirmed: true);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Declined, offer.Status);
        Assert.Equal(OfferDeclineReasonCategory.Other, offer.DeclineReasonCategory);
        Assert.StartsWith(RecruitmentOfferService.WithdrawalReasonPrefix, offer.DeclineReason);
    }

    [Fact]
    public async Task Declines_recorded_before_plan32_stay_uncategorized()
    {
        await using var db = CreateContext();
        var (offerId, _) = await Hcm027OfferOutcomeTests.CreateIssuedOfferAsync(db);
        var legacy = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        legacy.Status = OfferStatus.Declined;
        legacy.DeclineReason = "رفض قديم";
        legacy.DeclinedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        legacy.DeclinedBy = OfferDeclinedBy.HrOnBehalf;
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var stored = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Null(stored.DeclineReasonCategory);
        Assert.Equal("غير مصنف", OfferDeclineReasonCategories.LabelAr(stored.DeclineReasonCategory));
        Assert.Equal("رفض قديم", stored.DeclineReason);
    }

    [Theory]
    [InlineData(OfferDeclineReasonCategory.Salary, "الراتب")]
    [InlineData(OfferDeclineReasonCategory.Location, "مكان العمل")]
    [InlineData(OfferDeclineReasonCategory.OtherOffer, "عرض آخر")]
    [InlineData(OfferDeclineReasonCategory.Personal, "أسباب شخصية")]
    [InlineData(OfferDeclineReasonCategory.JobContent, "طبيعة العمل")]
    [InlineData(OfferDeclineReasonCategory.Other, "أخرى")]
    public void Category_labels_are_the_plan_labels(OfferDeclineReasonCategory category, string label) =>
        Assert.Equal(label, OfferDeclineReasonCategories.LabelAr(category));

    [Fact]
    public void Both_decline_forms_have_a_required_category_select()
    {
        var hr = System.IO.File.ReadAllText(HRManagement.TestSupport.RepoPaths.Resolve(
            "src", "Services", "HRCore", "HR.Core.Web", "Views", "OfferLetters", "Details.cshtml"));
        var portal = System.IO.File.ReadAllText(HRManagement.TestSupport.RepoPaths.Resolve(
            "src", "Services", "HRCore", "HR.Core.Web", "Views", "CandidatePortal", "Index.cshtml"));

        Assert.Contains("<select name=\"ReasonCategory\" id=\"DeclineReasonCategory\"", hr);
        Assert.Contains("<select id=\"portal-decline-category\" name=\"Category\"", portal);
    }
}
