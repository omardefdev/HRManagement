using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (PLAN-30 I): «طلبي» on <c>/careers/my</c> — <see cref="CandidatePortalService"/> and the
/// <see cref="CandidatePortalController"/> actions: the application, offer and appeal come only from the session (IDOR),
/// accept (acknowledgement + typed name, expiry), decline (PLAN-27 next-candidate behaviour), offer letter PDF, withdraw
/// (Issued / Draft / PendingApproval settlement, refused after OfferAccepted), appeal window, information answer and
/// missing-document upload while intake is open. Every action runs in the session's tenant with actor Candidate.
/// </summary>
public sealed class Hcm030CandidatePortalTests
{
    private const string FirstName = "الأول";
    private const string DeclineReason = "قبلت عرضاً من جهة أخرى";

    // ------------------------------------------------------------------ page

    [Fact]
    public async Task Page_shows_status_timeline_interviews_without_panel_and_the_offer_terms_after_the_code()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);
        var at = DateTime.UtcNow.AddDays(3);
        db.RecruitmentInterviewSlots.AddRange(
            new RecruitmentInterviewSlot { RecruitmentApplicationId = appId, ScheduledAt = at, Mode = InterviewMode.Remote, PanelNotes = "سري" },
            new RecruitmentInterviewSlot { RecruitmentApplicationId = appId, ScheduledAt = at.AddDays(-10), Mode = InterviewMode.InPerson, Location = "قاعة 3", Status = InterviewSlotStatus.Completed });
        await db.SaveChangesAsync();

        var page = await CreatePortal(db).GetPageAsync(session);

        Assert.Equal(CandidatePublicStatusProjection.Codes.OfferPending, page.Status!.Code);
        Assert.NotEmpty(page.Status.Timeline);
        Assert.Equal(2, page.Interviews.Count);
        Assert.Equal("قاعة 3", page.Interviews[0].PlaceText);
        Assert.Equal("عن بُعد", page.Interviews[1].PlaceText);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2}$", page.Interviews[1].DateText); // invariant, never Hijri
        Assert.DoesNotContain(typeof(CandidatePortalInterviewRow).GetProperties(), p => p.Name.Contains("Panel") || p.Name.Contains("Note"));

        var offer = Assert.IsType<CandidatePortalOfferCard>(page.Offer);
        Assert.True(offer.CanRespond);
        Assert.True(offer.CanDownload);
        Assert.False(offer.IsAccepted);
        Assert.Equal("5,000", offer.SalaryText);
        Assert.Equal("G5", offer.GradeCode);
        Assert.NotNull(offer.ExpiresOnText);
        Assert.Matches("^[0-9A-F]{12}$", offer.VerificationCode);
        Assert.True(page.CanWithdraw);
        Assert.Null(page.Appeal);
        _ = offerId;
    }

    [Fact]
    public async Task Legacy_citizen_row_shows_its_status_and_no_action()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, "LG");
        var posting = new PublicJobPosting { Title = "إعلان قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        var citizen = new CitizenApplication
        {
            PublicJobPostingId = posting.Id, ReferenceNumber = "CA-LEGACY", FullName = "قديم", NationalId = "0303030303",
            Status = CitizenApplicationStatus.UnderReview
        };
        db.CitizenApplications.Add(citizen);
        await db.SaveChangesAsync();
        var session = new CandidatePortalContext(citizen.Id, citizen.TenantId, null, citizen.ReferenceNumber, DateTime.UtcNow.AddMinutes(30));

        var portal = CreatePortal(db);
        var page = await portal.GetPageAsync(session);

        Assert.Null(page.Status);
        Assert.Equal("قيد المراجعة", page.LegacyStatusLabel);
        Assert.Null(page.Offer);
        Assert.False(page.CanWithdraw);
        Assert.False((await portal.WithdrawAsync(session, "سبب الانسحاب", confirmed: true)).Success);
        Assert.False((await portal.FileAppealAsync(session, "سبب طويل بما يكفي لتقديم الاستئناف")).Success);
        _ = req;
    }

    // ------------------------------------------------------------------ accept

    [Fact]
    public async Task Accept_with_acknowledgement_and_matching_name_records_the_candidate_channel()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        // Spacing, tatweel and alef variants are tolerated.
        var result = await CreatePortal(db).AcceptOfferAsync(session, acknowledged: true, typedFullName: "  إلأول ");

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Accepted, offer.Status);
        Assert.Equal(OfferAcceptedBy.Candidate, offer.AcceptedBy);
        Assert.NotNull(offer.AcceptedDate);
        Assert.Null(offer.AcceptanceEvidenceFileId);
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
        var audit = await db.AuditLogs.SingleAsync(l => l.EntityId == offerId && l.Action == "AcceptOffer");
        Assert.Null(audit.UserId);
        Assert.Equal(OfferAcceptedBy.Candidate, JsonDocument.Parse(audit.ChangedFields!).RootElement.GetProperty("acceptedBy").GetString());
    }

    [Theory]
    [InlineData(false, FirstName, nameof(CandidatePortalAcceptForm.Acknowledge), CandidatePortalService.AcknowledgeRequiredMessage)]
    [InlineData(true, "", nameof(CandidatePortalAcceptForm.FullName), CandidatePortalService.FullNameRequiredMessage)]
    [InlineData(true, "الثاني", nameof(CandidatePortalAcceptForm.FullName), CandidatePortalService.NameMismatchMessage)]
    public async Task Accept_is_refused_without_acknowledgement_or_with_a_name_mismatch(bool acknowledged, string name, string field, string message)
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        var result = await CreatePortal(db).AcceptOfferAsync(session, acknowledged, name);

        Assert.False(result.Success);
        Assert.Equal((field, message), result.Errors.Single());
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
    }

    [Fact]
    public async Task Expired_offer_cannot_be_accepted_or_declined()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);
        await Hcm027OfferOutcomeTests.BackdateExpiryAsync(db, offerId);
        var portal = CreatePortal(db);

        var accept = await portal.AcceptOfferAsync(session, true, FirstName);
        var decline = await portal.DeclineOfferAsync(session, DeclineReason, OfferDeclineReasonCategory.Salary);
        var page = await portal.GetPageAsync(session);

        Assert.Equal(RecruitmentOfferService.ExpiredOfferMessage, accept.Errors.Single().Error);
        Assert.Equal(CandidatePortalService.ExpiredOfferReplyMessage, decline.Errors.Single().Error);
        Assert.True(page.Offer!.IsExpired);
        Assert.False(page.Offer.CanRespond);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
    }

    [Fact]
    public async Task Pre_hcm030_accept_overload_is_recorded_as_hr_on_behalf_and_audited()
    {
        await using var db = CreateContext();
        var (offerId, _) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);

        var (success, error) = await CreateOfferService(db).AcceptAsync(offerId);

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferAcceptedBy.HrOnBehalf, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).AcceptedBy);
        Assert.True(await db.AuditLogs.AnyAsync(l => l.EntityId == offerId && l.Action == "AcceptOfferOnBehalf"));
    }

    [Fact]
    public async Task Accept_with_an_unknown_channel_is_refused_and_the_offer_stays_issued()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);

        var (success, error) = await CreateOfferService(db).AcceptAsync(offerId, "Someone", null, null, null);

        Assert.False(success);
        Assert.Equal("جهة القبول غير صالحة.", error);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
    }

    [Theory]
    [InlineData("الأول", "الأول", true)]
    [InlineData("محمد  أحمد", "محمد احمد", true)]
    [InlineData("فاطمة", "فاطمه", true)]
    [InlineData("مُنى", "منى", true)]
    [InlineData("Ali Hassan", "ali   hassan", true)]
    [InlineData("محمد", "محمد أحمد", false)]
    [InlineData("", "محمد", false)]
    public void Typed_name_matching_ignores_only_spacing_and_letter_variants(string typed, string expected, bool matches) =>
        Assert.Equal(matches, CandidatePortalService.NameMatches(typed, expected));

    // ------------------------------------------------------------------ IDOR

    [Fact]
    public async Task Actions_use_only_the_session_application_whatever_ids_the_request_carries()
    {
        await using var db = CreateContext();
        var (ownOfferId, ownAppId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true, suffix: "OWN");
        var (otherOfferId, otherAppId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true, suffix: "OTH");
        var session = await LinkCitizenAsync(db, ownAppId);
        await LinkCitizenAsync(db, otherAppId, "0202020202");

        var controller = CreateController(db, session);
        foreach (var key in new[] { "id", "offerId", "applicationId", "appealId" })
            controller.RouteData.Values[key] = otherOfferId.ToString();
        controller.HttpContext.Request.QueryString = new QueryString($"?id={otherOfferId}&applicationId={otherAppId}");

        var result = await controller.AcceptOffer(new CandidatePortalAcceptForm { Acknowledge = true, FullName = FirstName }, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(CandidatePortalController.Index), redirect.ActionName);
        Assert.Equal("accepted", redirect.RouteValues!["done"]);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Accepted, (await db.OfferLetters.SingleAsync(o => o.Id == ownOfferId)).Status);
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == otherOfferId)).Status);

        // A refused action re-renders the page; the other application is still untouched.
        var withdraw = await CreateController(db, session).Withdraw(new CandidatePortalWithdrawForm { Reason = "ظروف عائلية", Confirm = true }, CancellationToken.None);
        Assert.IsType<ViewResult>(withdraw); // refused: the session's offer is accepted
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == otherAppId)).Status);
    }

    [Fact]
    public void Portal_actions_take_no_ids_and_need_the_session_and_anti_forgery()
    {
        var type = typeof(CandidatePortalController);
        var actions = new[]
        {
            nameof(CandidatePortalController.Index), nameof(CandidatePortalController.AcceptOffer),
            nameof(CandidatePortalController.DeclineOffer), nameof(CandidatePortalController.OfferLetter),
            nameof(CandidatePortalController.Withdraw), nameof(CandidatePortalController.Appeal),
            nameof(CandidatePortalController.AppealInfo), nameof(CandidatePortalController.Documents)
        };
        foreach (var name in actions)
        {
            var method = type.GetMethod(name)!;
            Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(CandidatePortalSessionAttribute)));
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(Guid) || p.ParameterType == typeof(Guid?));
            if (method.GetCustomAttributes(typeof(HttpPostAttribute), false).Length > 0)
                Assert.NotNull(Attribute.GetCustomAttribute(method, typeof(ValidateAntiForgeryTokenAttribute)));
        }
        foreach (var form in new[] { typeof(CandidatePortalAcceptForm), typeof(CandidatePortalTextForm), typeof(CandidatePortalDeclineForm), typeof(CandidatePortalWithdrawForm), typeof(CandidatePortalDocumentsForm) })
            Assert.DoesNotContain(form.GetProperties(), p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?));
    }

    [Fact]
    public async Task Refused_post_shows_the_page_again_with_the_error_and_the_typed_text()
    {
        await using var db = CreateContext();
        var (_, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);
        var controller = CreateController(db, session);

        var result = await controller.DeclineOffer(new CandidatePortalDeclineForm { Text = "لا", Category = OfferDeclineReasonCategory.Salary }, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(CandidatePortalController.Index), view.ViewName);
        Assert.Equal("لا", Assert.IsType<CandidatePortalPageViewModel>(view.Model).Posted.DeclineReason);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey("Decline.Text"));
    }

    // ------------------------------------------------------------------ decline

    [Fact]
    public async Task Decline_records_the_candidate_and_auto_drafts_the_next_offer_like_an_hr_decline()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        var result = await CreatePortal(db, new RecruitmentOptions { AutoDraftNextOffer = true }).DeclineOfferAsync(session, DeclineReason, OfferDeclineReasonCategory.Salary);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Declined, offer.Status);
        Assert.Equal(OfferDeclinedBy.Candidate, offer.DeclinedBy);
        Assert.Equal(RecruitmentApplicationStatus.OfferDeclined, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
        Assert.Null((await db.AuditLogs.SingleAsync(l => l.EntityId == offerId && l.Action == "DeclineOffer")).UserId);
        var draft = await db.OfferLetters.Include(o => o.RecruitmentApplication).SingleAsync(o => o.Id != offerId);
        Assert.Equal(OfferStatus.Draft, draft.Status);
        Assert.Equal(2, draft.RecruitmentApplication!.Rank);
    }

    [Fact]
    public async Task Decline_with_auto_draft_off_leaves_the_next_candidate_suggestion()
    {
        await using var db = CreateContext();
        var (_, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        Assert.True((await CreatePortal(db).DeclineOfferAsync(session, DeclineReason, OfferDeclineReasonCategory.Salary)).Success);

        var requisitionId = (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == appId)).RequisitionId!.Value;
        var suggestion = await CreateReserveList(db).GetNextCandidateSuggestionAsync(requisitionId);
        Assert.Equal(2, suggestion!.Rank);
        Assert.Single(await db.OfferLetters.ToListAsync());
    }

    // ------------------------------------------------------------------ offer letter

    [Fact]
    public async Task Offer_letter_pdf_is_served_for_the_session_offer_and_audited()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        var result = await CreateController(db, session).OfferLetter(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(file.FileContents, 0, 4));
        Assert.StartsWith("offer-letter-", file.FileDownloadName);
        var audit = await db.AuditLogs.SingleAsync(l => l.Action == "CandidatePortalOfferLetterDownloaded");
        Assert.Null(audit.UserId);
        Assert.Contains(offerId.ToString(), audit.ChangedFields);
    }

    [Fact]
    public async Task Offer_letter_is_not_served_without_an_issued_or_accepted_offer()
    {
        await using var db = CreateContext();
        var (_, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: false); // PendingApproval
        var session = await LinkCitizenAsync(db, appId);

        Assert.IsType<RedirectToActionResult>(await CreateController(db, session).OfferLetter(CancellationToken.None));
        Assert.Null((await CreatePortal(db).GetPageAsync(session)).Offer);
    }

    // ------------------------------------------------------------------ withdraw

    [Fact]
    public async Task Withdraw_with_an_issued_offer_declines_it_for_the_candidate_and_drafts_the_next_offer()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        var result = await CreatePortal(db, new RecruitmentOptions { AutoDraftNextOffer = true }).WithdrawAsync(session, "ظروف عائلية", confirmed: true);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == appId);
        Assert.Equal(OfferStatus.Declined, offer.Status);
        Assert.Equal(OfferDeclinedBy.Candidate, offer.DeclinedBy);
        Assert.Equal("انسحاب المرشح: ظروف عائلية", offer.DeclineReason);
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, app.Status);
        Assert.Null(app.ActiveOfferLetterId);
        var audit = await db.AuditLogs.SingleAsync(l => l.EntityId == appId && l.Action == "WithdrawByCandidate");
        Assert.Null(audit.UserId);
        Assert.Contains("Candidate", audit.ChangedFields);
        var draft = await db.OfferLetters.Include(o => o.RecruitmentApplication).SingleAsync(o => o.Id != offerId);
        Assert.Equal(2, draft.RecruitmentApplication!.Rank);
    }

    [Fact]
    public async Task Withdraw_with_auto_draft_off_suggests_the_next_candidate()
    {
        await using var db = CreateContext();
        var (_, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);

        Assert.True((await CreatePortal(db).WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success);

        var requisitionId = (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == appId)).RequisitionId!.Value;
        Assert.Equal(2, (await CreateReserveList(db).GetNextCandidateSuggestionAsync(requisitionId))!.Rank);
    }

    [Fact]
    public async Task Withdraw_with_a_pending_approval_offer_revokes_it_and_cancels_the_approval()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: false);
        var session = await LinkCitizenAsync(db, appId);

        Assert.True((await CreatePortal(db).WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Revoked, offer.Status);
        Assert.StartsWith(RecruitmentOfferService.WithdrawalReasonPrefix, offer.RevokeReason);
        Assert.Equal(WorkflowStatus.Cancelled, (await db.WorkflowInstances.SingleAsync(i => i.Id == offer.WorkflowInstanceId)).Status);
        var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == appId);
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, app.Status); // never Rejected or Ranked
        Assert.Null(app.RejectionKind);
    }

    [Fact]
    public async Task Withdraw_with_a_draft_offer_revokes_it()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db, "DR");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1, FirstName);
        var (draft, error) = await CreateOfferService(db).CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId);
        Assert.True(draft is not null, error);
        var session = await LinkCitizenAsync(db, app.Id);

        Assert.True((await CreatePortal(db).WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success);

        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Revoked, (await db.OfferLetters.SingleAsync(o => o.Id == draft!.Id)).Status);
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, (await db.RecruitmentApplications.SingleAsync(a => a.Id == app.Id)).Status);
    }

    [Fact]
    public async Task Withdraw_is_refused_after_the_offer_was_accepted()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);
        var portal = CreatePortal(db);
        Assert.True((await portal.AcceptOfferAsync(session, true, FirstName)).Success);

        var result = await portal.WithdrawAsync(session, "ظروف عائلية", confirmed: true);
        var page = await portal.GetPageAsync(session);

        Assert.Equal(RecruitmentSelectionService.AcceptedOfferWithdrawMessage, result.Errors.Single().Error);
        Assert.False(page.CanWithdraw);
        Assert.True(page.WithdrawBlockedByAcceptance);
        Assert.True(page.Offer!.IsAccepted);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Accepted, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
    }

    [Fact]
    public async Task Hired_application_shows_no_withdraw_section_and_is_refused_as_terminal()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        var session = await LinkCitizenAsync(db, appId);
        var portal = CreatePortal(db);
        Assert.True((await portal.AcceptOfferAsync(session, true, FirstName)).Success);
        // As RecruitmentHireClosureService leaves it: Hired, the accepted offer still active and converted.
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        offer.IsConvertedToEmployee = true;
        (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status = RecruitmentApplicationStatus.Hired;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await portal.GetPageAsync(session);
        var result = await portal.WithdrawAsync(session, "ظروف عائلية", confirmed: true);

        Assert.False(page.CanWithdraw);
        Assert.False(page.WithdrawBlockedByAcceptance);
        Assert.Equal(RecruitmentSelectionService.CannotWithdrawMessage, result.Errors.Single().Error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.Hired, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
    }

    [Fact]
    public async Task Refused_withdraw_of_an_application_no_longer_open_shows_the_error_without_the_invitation()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, "WT");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.UnderReview, null, FirstName);
        var session = await LinkCitizenAsync(db, app.Id);
        // Two tabs: the first withdraws, the second then submits its confirm panel.
        Assert.True((await CreatePortal(db).WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success);
        db.ChangeTracker.Clear();
        var controller = CreateController(db, session);

        var result = await controller.Withdraw(new CandidatePortalWithdrawForm { Reason = "ظروف عائلية", Confirm = true }, CancellationToken.None);

        var page = Assert.IsType<CandidatePortalPageViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(page.CanWithdraw);
        Assert.False(page.ShowWithdrawConfirm);
        Assert.False(page.WithdrawBlockedByAcceptance);
        Assert.Equal(RecruitmentSelectionService.CannotWithdrawMessage, controller.ModelState["Withdraw"]!.Errors.Single().ErrorMessage);

        // The view offers the invitation and button only while the application can still be withdrawn.
        var view = System.IO.File.ReadAllText(FindRepoFile("src/Services/HRCore/HR.Core.Web/Views/CandidatePortal/Index.cshtml"));
        var errorBranch = view.IndexOf("else if (!Model.CanWithdraw)", StringComparison.Ordinal);
        var invitation = view.IndexOf("يمكنك سحب طلبك ما دام قيد المعالجة", StringComparison.Ordinal);
        Assert.True(errorBranch > 0 && errorBranch < invitation);
        Assert.Contains("id=\"portal-withdraw-error\"", view);
    }

    [Fact]
    public async Task Withdraw_needs_the_confirmation_and_a_reason()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, "WC");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.UnderReview, null, FirstName);
        var session = await LinkCitizenAsync(db, app.Id);
        var portal = CreatePortal(db);

        var unconfirmed = await portal.WithdrawAsync(session, "ظروف عائلية", confirmed: false);
        var noReason = await portal.WithdrawAsync(session, " ", confirmed: true);
        Assert.Equal((nameof(CandidatePortalWithdrawForm.Confirm), CandidatePortalService.WithdrawConfirmRequiredMessage), unconfirmed.Errors.Single());
        Assert.Equal(nameof(CandidatePortalWithdrawForm.Reason), noReason.Errors.Single().Field);
        Assert.Equal(RecruitmentApplicationStatus.UnderReview, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id)).Status);

        Assert.True((await portal.WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success);
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id)).Status);
        Assert.False((await portal.WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success); // terminal now
    }

    [Fact]
    public async Task Withdrawal_without_a_settled_offer_does_not_raise_the_next_candidate_suggestion()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        // HR revokes as an administrative error (back to Ranked), then the candidate withdraws without an active offer.
        Assert.True((await CreateOfferService(db).RevokeAsync(offerId, "خطأ في الراتب المعروض", revertToRanked: true)).Success);
        var session = await LinkCitizenAsync(db, appId);

        Assert.True((await CreatePortal(db).WithdrawAsync(session, "ظروف عائلية", confirmed: true)).Success);

        var requisitionId = (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == appId)).RequisitionId!.Value;
        Assert.Null(await CreateReserveList(db).GetNextCandidateSuggestionAsync(requisitionId));
        var (draft, draftError) = await CreateOfferService(db, options: new RecruitmentOptions { AutoDraftNextOffer = true })
            .TryAutoDraftNextOfferAsync(offerId, null);
        Assert.Null(draft);
        Assert.Null(draftError);
    }

    // ------------------------------------------------------------------ appeal

    [Fact]
    public async Task Appeal_is_filed_by_the_candidate_while_the_window_is_open()
    {
        await using var db = CreateContext();
        var (_, app) = await Hcm028AppealTestSupport.SeedRejectedAsync(db);
        var session = await LinkCitizenAsync(db, app.Id);
        var portal = CreatePortal(db);

        var before = await portal.GetPageAsync(session);
        var result = await portal.FileAppealAsync(session, Hcm028AppealTestSupport.Reason);
        var after = await portal.GetPageAsync(session);

        Assert.Equal(CandidatePortalAppealState.CanFile, before.Appeal!.State);
        Assert.NotNull(before.Appeal.DateText);
        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        var appeal = await db.RecruitmentAppeals.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentAppealFiledBy.Candidate, appeal.FiledBy);
        Assert.Null(appeal.CreatedBy);
        Assert.Equal(CandidatePortalAppealState.Pending, after.Appeal!.State);
        Assert.False((await portal.FileAppealAsync(session, Hcm028AppealTestSupport.Reason)).Success); // one appeal only
    }

    [Fact]
    public async Task After_the_window_the_page_shows_the_closing_date_and_the_post_is_refused()
    {
        await using var db = CreateContext();
        var (_, app) = await Hcm028AppealTestSupport.SeedRejectedAsync(db);
        var rejectedAt = DateTime.UtcNow.AddDays(-20);
        var tracked = await db.RecruitmentApplications.SingleAsync(a => a.Id == app.Id);
        tracked.RejectedAt = rejectedAt;
        await db.SaveChangesAsync();
        var session = await LinkCitizenAsync(db, app.Id);
        var portal = CreatePortal(db);

        var page = await portal.GetPageAsync(session);
        var result = await portal.FileAppealAsync(session, Hcm028AppealTestSupport.Reason);

        Assert.Equal(CandidatePortalAppealState.WindowClosed, page.Appeal!.State);
        Assert.Equal(CandidatePortalService.FormatDate(rejectedAt.AddDays(RecruitmentAppealWorkflowRules.DefaultAppealWindowDays)), page.Appeal.DateText);
        Assert.False(page.Status!.CanAppeal);
        Assert.Equal(RecruitmentAppealWorkflowRules.WindowExpiredMessage, result.Errors.Single().Error);
        Assert.False(await db.RecruitmentAppeals.AnyAsync());
    }

    [Fact]
    public async Task Candidate_answers_the_reviewers_information_request()
    {
        await using var db = CreateContext();
        var (_, app) = await Hcm028AppealTestSupport.SeedRejectedAsync(db);
        var session = await LinkCitizenAsync(db, app.Id);
        var portal = CreatePortal(db);
        Assert.False((await portal.AnswerAppealInfoRequestAsync(session, "معلومات إضافية كافية")).Success); // no appeal yet

        Assert.True((await portal.FileAppealAsync(session, Hcm028AppealTestSupport.Reason)).Success);
        var appeals = Hcm028AppealTestSupport.CreateService(db);
        var appealId = (await db.RecruitmentAppeals.AsNoTracking().SingleAsync()).Id;
        var reviewer = await Hcm028TestSupport.SeedEmployeeAsync(db, "مراجع الامتثال");
        Assert.True((await appeals.AssignReviewerAsync(appealId, reviewer.Id, Hcm028AppealTestSupport.HrUserId)).Success);
        var (asked, askError) = await appeals.RequestInfoAsync(
            appealId, "يرجى إرفاق شهادة الخبرة", Hcm028AppealTestSupport.Reviewer(Hcm028AppealTestSupport.ReviewerUserId, reviewer.Id));
        Assert.True(asked, askError);
        db.ChangeTracker.Clear();

        var waiting = await portal.GetPageAsync(session);
        Assert.Equal(CandidatePortalAppealState.AwaitingInfo, waiting.Appeal!.State);
        Assert.Equal("يرجى إرفاق شهادة الخبرة", waiting.Appeal.InfoRequest);
        Assert.Equal(nameof(CandidatePortalTextForm.Text), (await portal.AnswerAppealInfoRequestAsync(session, "قصير")).Errors.Single().Field);

        var result = await portal.AnswerAppealInfoRequestAsync(session, "أرفقت شهادة الخبرة لدى الجهة السابقة");

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
        db.ChangeTracker.Clear();
        var appeal = await db.RecruitmentAppeals.SingleAsync();
        Assert.False(appeal.IsAwaitingInfo);
        Assert.NotNull(appeal.WorkflowInstanceId);
        Assert.Null((await db.AuditLogs.SingleAsync(l => l.Action == "CandidatePortalAppealInfoSubmitted")).UserId);
        Assert.Equal(CandidatePortalAppealState.Pending, (await portal.GetPageAsync(session)).Appeal!.State);
    }

    // ------------------------------------------------------------------ documents

    [Fact]
    public async Task Missing_documents_are_uploaded_while_intake_is_open_and_the_flag_is_reevaluated()
    {
        await using var db = CreateContext();
        var app = await SeedPublicApplicationAsync(db, "Cv,IdCopy");
        var session = await LinkCitizenAsync(db, app.Id);
        var client = new Hcm030TestSupport.FakeCandidateDocumentClient();
        var portal = CreatePortal(db, documentClient: client);

        var page = await portal.GetPageAsync(session);
        Assert.True(page.CanUploadDocuments);
        Assert.Equal(new[] { CandidateDocumentTypes.Cv, CandidateDocumentTypes.IdCopy }, page.MissingDocumentTypes.Select(t => t.Code));

        var cv = await portal.UploadDocumentsAsync(session, [(CandidateDocumentTypes.Cv, Pdf("cv.pdf"), "CvFile")]);
        Assert.True(cv.Success, string.Join(" | ", cv.Errors.Select(e => e.Error)));
        Assert.Contains(RecruitmentEligibilityRules.MissingRequiredDocuments, Flags(db, app.Id));

        // A type that is no longer missing is refused (no upload).
        var again = await portal.UploadDocumentsAsync(session, [(CandidateDocumentTypes.Cv, Pdf("cv2.pdf"), "CvFile")]);
        Assert.Equal(("CvFile", CandidatePortalService.NotMissingTypeMessage), again.Errors.Single());

        var id = await portal.UploadDocumentsAsync(session, [(CandidateDocumentTypes.IdCopy, Pdf("id.pdf"), "IdCopyFile")]);
        Assert.True(id.Success);
        Assert.DoesNotContain(RecruitmentEligibilityRules.MissingRequiredDocuments, Flags(db, app.Id));

        var documents = await db.CandidateDocuments.AsNoTracking().ToListAsync();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d => Assert.Equal(CandidateDocument.UploadedByCandidate, d.UploadedBy));
        Assert.All(documents, d => Assert.Equal(session.CitizenApplicationId, d.CitizenApplicationId));
        Assert.Equal(2, client.Uploads.Count);
        Assert.Equal(2, await db.Set<FileReferenceOutboxMessage>().CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync(l => l.Action == "CandidatePortalDocumentsUploaded" && l.UserId == null));
        Assert.False((await portal.GetPageAsync(session)).CanUploadDocuments);
    }

    [Fact]
    public async Task Documents_are_refused_once_intake_is_closed()
    {
        await using var db = CreateContext();
        var app = await SeedPublicApplicationAsync(db, "Cv");
        var req = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == app.RequisitionId);
        req.IntakeClosedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var session = await LinkCitizenAsync(db, app.Id);
        var client = new Hcm030TestSupport.FakeCandidateDocumentClient();
        var portal = CreatePortal(db, documentClient: client);

        var page = await portal.GetPageAsync(session);
        var result = await portal.UploadDocumentsAsync(session, [(CandidateDocumentTypes.Cv, Pdf("cv.pdf"), "CvFile")]);

        Assert.False(page.CanUploadDocuments);
        Assert.Equal(CandidatePortalService.DocumentsClosedMessage, page.DocumentsClosedReason);
        Assert.Equal(CandidatePortalService.DocumentsClosedMessage, result.Errors.Single().Error);
        Assert.Empty(client.Uploads);
        Assert.False(await db.CandidateDocuments.AnyAsync());
    }

    [Fact]
    public async Task Document_upload_keeps_the_session_two_checks_and_limits()
    {
        await using var db = CreateContext();
        var app = await SeedPublicApplicationAsync(db, "Cv,Certificate");
        var session = await LinkCitizenAsync(db, app.Id);
        var client = new Hcm030TestSupport.FakeCandidateDocumentClient();
        var portal = CreatePortal(db, new RecruitmentOptions { CandidateDocuments = { MaxFiles = 2 } }, client);

        var fake = await portal.UploadDocumentsAsync(session, [(CandidateDocumentTypes.Cv, Form("not a pdf"u8.ToArray(), "cv.pdf"), "CvFile")]);
        var tooMany = await portal.UploadDocumentsAsync(session,
        [
            (CandidateDocumentTypes.Certificate, Pdf("a.pdf"), "CertificateFiles"),
            (CandidateDocumentTypes.Certificate, Pdf("b.pdf"), "CertificateFiles"),
            (CandidateDocumentTypes.Cv, Pdf("cv.pdf"), "CvFile")
        ]);
        client.FailAfter = 0;
        var unavailable = await portal.UploadDocumentsAsync(session, [(CandidateDocumentTypes.Cv, Pdf("cv.pdf"), "CvFile")]);

        Assert.Equal("CvFile", fake.Errors.Single().Field);
        Assert.Equal(CandidateDocumentFileValidator.TooManyFilesMessage(2), tooMany.Errors.Single().Error);
        Assert.Equal(CandidateDocumentService.UploadFailedMessage, unavailable.Errors.Single().Error);
        Assert.Empty(client.Uploads);
        Assert.False(await db.CandidateDocuments.AnyAsync());
    }

    // ------------------------------------------------------------------ tenant

    [Fact]
    public async Task Actions_run_in_the_session_tenant_and_restore_the_ambient_tenant()
    {
        await using var seed = CreateContext();
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(seed, approve: true);
        var session = await LinkCitizenAsync(seed, appId);
        var tenant = Guid.NewGuid();
        string[] updates =
        [
            "UPDATE \"OfferLetters\" SET \"TenantId\" = {0}",
            "UPDATE \"RecruitmentApplications\" SET \"TenantId\" = {0}",
            "UPDATE \"RecruitmentRequisitions\" SET \"TenantId\" = {0}",
            "UPDATE \"CitizenApplications\" SET \"TenantId\" = {0}",
            "UPDATE \"PublicJobPostings\" SET \"TenantId\" = {0}",
            "UPDATE \"FundedPositions\" SET \"TenantId\" = {0}",
            "UPDATE \"OrgUnits\" SET \"TenantId\" = {0}"
        ];
        foreach (var sql in updates)
            await seed.Database.ExecuteSqlRawAsync(sql, tenant.ToString().ToUpperInvariant()); // EF stores SQLite Guids as upper-case text
        await using var db = new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(seed.Database.GetDbConnection()).Options,
            new FlowTenantContext());
        var portal = CreatePortal(db);
        var clientTenant = Guid.NewGuid(); // as if a client sent another X-Tenant-ID
        TenantIdFlow.Set(clientTenant);
        try
        {
            var wrongTenant = await portal.AcceptOfferAsync(session with { TenantId = clientTenant }, true, FirstName);
            Assert.Equal(CandidatePortalService.NoOfferMessage, wrongTenant.Errors.Single().Error);

            var result = await portal.AcceptOfferAsync(session with { TenantId = tenant }, true, FirstName);

            Assert.True(result.Success, string.Join(" | ", result.Errors.Select(e => e.Error)));
            Assert.Equal(clientTenant, TenantIdFlow.Get());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
        Assert.Equal(OfferStatus.Accepted, (await seed.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offerId)).Status);
        Assert.Equal(tenant, (await seed.AuditLogs.AsNoTracking().SingleAsync(l => l.Action == "AcceptOffer")).TenantId);
    }

    // ------------------------------------------------------------------ helpers

    internal static CandidatePortalService CreatePortal(
        HRCoreDbContext db,
        RecruitmentOptions? options = null,
        ICandidateDocumentUploadClient? documentClient = null)
    {
        var settings = options ?? new RecruitmentOptions();
        var opts = Microsoft.Extensions.Options.Options.Create(settings);
        return new CandidatePortalService(
            db,
            new CandidatePublicStatusService(db, opts),
            CreateOfferService(db, options: settings),
            new RecruitmentSelectionService(db),
            Hcm028AppealTestSupport.CreateService(db, options: settings),
            new OfferLetterDocumentService(db, new LegislationRegisterProvider(db), opts),
            Hcm030TestSupport.CreateCandidateDocumentService(db, documentClient),
            new UnifiedIntakeService(db, opts),
            opts,
            NullLogger<CandidatePortalService>.Instance);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, relativePath)))
            dir = dir.Parent;
        if (dir is null)
            throw new FileNotFoundException(relativePath);
        return Path.Combine(dir.FullName, relativePath);
    }

    internal static CandidatePortalController CreateController(HRCoreDbContext db, CandidatePortalContext session)
    {
        var controller = new CandidatePortalController(null!, null!, null!, CreatePortal(db));
        var http = new DefaultHttpContext();
        http.Items[CandidatePortalSessionFilter.ItemKey] = session;
        controller.ControllerContext = new ControllerContext { HttpContext = http, RouteData = new RouteData() };
        return controller;
    }

    private static ReserveListService CreateReserveList(HRCoreDbContext db) =>
        new(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), NullLogger<ReserveListService>.Instance);

    /// <summary>Links a public citizen application (posting + reference) to the recruitment application; the session.</summary>
    internal static async Task<CandidatePortalContext> LinkCitizenAsync(HRCoreDbContext db, Guid appId, string nationalId = "0101010101")
    {
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == appId);
        var posting = new PublicJobPosting { TenantId = app.TenantId, Title = "محلل", IsPublished = true, RequisitionId = app.RequisitionId };
        db.PublicJobPostings.Add(posting);
        var citizen = new CitizenApplication
        {
            TenantId = app.TenantId,
            PublicJobPostingId = posting.Id,
            RequisitionId = app.RequisitionId,
            PromotedRecruitmentApplicationId = app.Id,
            ReferenceNumber = $"CA-H30I-{nationalId[^4..]}-{app.Id.ToString()[..4]}",
            FullName = app.ApplicantFullName,
            NationalId = nationalId,
            Email = "c@test.sy"
        };
        db.CitizenApplications.Add(citizen);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new CandidatePortalContext(citizen.Id, citizen.TenantId, app.Id, citizen.ReferenceNumber, DateTime.UtcNow.AddMinutes(30));
    }

    private static async Task<RecruitmentApplication> SeedPublicApplicationAsync(HRCoreDbContext db, string requiredDocuments)
    {
        var req = await SeedRequisitionAsync(db, "DOC");
        req.RequiredDocumentTypes = requiredDocuments;
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Submitted, null, FirstName);
        app.IntakeChannel = RecruitmentIntakeChannel.Public;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return app;
    }

    private static IReadOnlyList<string> Flags(HRCoreDbContext db, Guid appId) =>
        RecruitmentEligibilityRules.Parse(db.RecruitmentApplications.AsNoTracking().Single(a => a.Id == appId).EligibilityFlags);

    private static IFormFile Pdf(string fileName)
    {
        var bytes = new byte[64];
        Array.Fill(bytes, (byte)' ');
        Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(bytes, 0);
        return Form(bytes, fileName);
    }

    private static IFormFile Form(byte[] bytes, string fileName) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

    /// <summary>Tenant filters on, tenant from <see cref="TenantIdFlow"/> (as in the host for anonymous requests).</summary>
    private sealed class FlowTenantContext : ITenantContext
    {
        public Guid? TenantId => TenantIdFlow.Get();
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => TenantId ?? throw new InvalidOperationException("No tenant.");
    }
}
