using System.Text;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P6): the competition part of «طلبي» on <c>/careers/my</c> — preferences, the admission card, the
/// result only after publication, the objection within its window, allocation/reserve status and document upload.
/// </summary>
public sealed class Hcm031PortalTests
{
    private const string ObjectionReason = "أطلب إعادة تدقيق ورقة الامتحان الكتابي لوجود خطأ في الجمع";

    [Fact]
    public async Task The_page_shows_the_preferences_and_the_admission_card_once_issued()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 3, 10);
        var seating = CreateSeating(db);
        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        var app = await db.RecruitmentApplications.AsNoTracking().OrderBy(a => a.AppliedAt).FirstAsync();
        var session = await LinkAsync(db, app);
        var portal = CreatePortal(db);

        var before = (await portal.GetPageAsync(session)).Competition!;
        Assert.Equal(competition.Number, before.Number);
        Assert.Equal(2, before.Preferences.Count);
        Assert.StartsWith("1. ", before.Preferences[0], StringComparison.Ordinal);
        var exam = Assert.Single(before.Exams);
        Assert.False(exam.CardIssued);
        Assert.Equal(8, exam.ExamNumber.Length);
        Assert.Null(before.Result);
        Assert.False(before.CanObject);
        Assert.Null(await portal.GetAdmissionCardAsync(session, nameof(ExamSessionType.Written)));

        Assert.True((await seating.IssueAdmissionCardsAsync(competition.Id, Creator)).Success);
        Assert.True(Assert.Single((await portal.GetPageAsync(session)).Competition!.Exams).CardIssued);

        var controller = CreateController(db, session);
        var file = Assert.IsType<FileContentResult>(await controller.AdmissionCard(nameof(ExamSessionType.Written), CancellationToken.None));
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(file.FileContents, 0, 4));
        Assert.Contains(exam.ExamNumber, file.FileDownloadName, StringComparison.Ordinal);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        // Another type (not seated) or no type: back to the page, nothing served.
        Assert.IsType<RedirectToActionResult>(await controller.AdmissionCard(nameof(ExamSessionType.Oral), CancellationToken.None));
        Assert.IsType<RedirectToActionResult>(await controller.AdmissionCard(null, CancellationToken.None));
        Assert.Single(await db.AuditLogs.AsNoTracking().Where(l => l.Action == "CandidatePortalAdmissionCardDownloaded").ToListAsync());
    }

    [Fact]
    public async Task The_result_is_shown_only_after_publication_and_the_objection_is_filed_within_the_window()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 40m]);
        var top = await LinkAsync(db, fx.Applications[0]);
        var session = await LinkAsync(db, fx.Applications[1]);
        var portal = CreatePortal(db);

        // A computed draft is not published: nothing about it reaches the candidate.
        var (draft, error) = await CreateResults(db).ComputeDraftAsync(fx.Competition.Id, null, Creator);
        Assert.True(draft is not null, error);
        var unpublished = (await portal.GetPageAsync(session)).Competition!;
        Assert.Null(unpublished.Result);
        Assert.False(unpublished.CanObject);
        Assert.Null(unpublished.ObjectionDeadline);

        await ApproveAndPublishAsync(db, draft!.Id);
        var first = (await portal.GetPageAsync(top)).Competition!.Result!;
        Assert.Equal("90", first.FinalScore);
        Assert.Equal(1, first.Rank);
        var published = (await portal.GetPageAsync(session)).Competition!;
        Assert.NotNull(published.Result);
        Assert.False(string.IsNullOrEmpty(published.Result!.OutcomeLabel));
        Assert.True(published.CanObject);
        Assert.NotNull(published.ObjectionDeadline);
        Assert.Null(published.ObjectionClosedText);
        Assert.Null(published.ObjectionStatusText);

        var posted = await CreateController(db, session).Objection(new CandidatePortalTextForm { Text = ObjectionReason }, nameof(ExamSessionType.Written), CancellationToken.None);
        var redirect = Assert.IsType<RedirectToActionResult>(posted);
        Assert.Equal("objected", redirect.RouteValues!["done"]);
        var appeal = await db.RecruitmentAppeals.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentAppealKind.ResultObjection, appeal.Kind);
        Assert.Equal(RecruitmentAppealFiledBy.Candidate, appeal.FiledBy);
        Assert.Equal(fx.Papers[fx.Applications[1].Id].Id, appeal.ObjectionExamCandidateId);

        var pending = (await portal.GetPageAsync(session)).Competition!;
        Assert.False(pending.CanObject);
        Assert.Equal("اعتراضك قيد المراجعة", pending.ObjectionStatusText);
        var again = await portal.FileObjectionAsync(session, ObjectionReason, nameof(ExamSessionType.Written));
        Assert.False(again.Success);
        Assert.Contains(again.Errors, e => e.Error == CompetitionObjectionService.PendingExistsMessage);
    }

    [Fact]
    public async Task After_the_window_the_page_says_when_it_closed_and_the_objection_is_refused()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 40m]);
        var session = await LinkAsync(db, fx.Applications[1]);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        var portal = CreatePortal(db);

        var card = (await portal.GetPageAsync(session)).Competition!;
        Assert.False(card.CanObject);
        Assert.StartsWith("انتهت مدة الاعتراض في ", card.ObjectionClosedText, StringComparison.Ordinal);
        var result = await portal.FileObjectionAsync(session, ObjectionReason, nameof(ExamSessionType.Written));
        Assert.False(result.Success);
        Assert.Empty(await db.RecruitmentAppeals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task After_finalization_the_page_shows_the_allocation_or_the_reserve()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m, 75m, 70m, 65m, 40m]);
        var allocated = await LinkAsync(db, fx.Applications[0]);
        var reserve = await LinkAsync(db, fx.Applications[5]);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
        var portal = CreatePortal(db);

        var allocatedCard = (await portal.GetPageAsync(allocated)).Competition!;
        var lineTitles = fx.Competition.Lines.Select(l => l.Title).ToList();
        Assert.StartsWith("مرشح للتعيين على ", allocatedCard.AllocationText, StringComparison.Ordinal);
        Assert.Contains(lineTitles, t => allocatedCard.AllocationText!.EndsWith(t, StringComparison.Ordinal));
        Assert.Empty(allocatedCard.ReserveTexts);

        var reserveCard = (await portal.GetPageAsync(reserve)).Competition!;
        Assert.Null(reserveCard.AllocationText);
        Assert.NotEmpty(reserveCard.ReserveTexts);
        Assert.All(reserveCard.ReserveTexts, t => Assert.StartsWith("في قائمة الاحتياط على ", t, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_documents_are_the_union_of_the_lines_and_upload_closes_with_the_intake()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1, 1);
        var requisitions = await db.RecruitmentRequisitions.Where(r => r.CompetitionLineId != null).OrderBy(r => r.RequisitionNumber).ToListAsync();
        Assert.Equal(2, requisitions.Count);
        requisitions[0].RequiredDocumentTypes = "Certificate";
        requisitions[1].RequiredDocumentTypes = "IdCopy";
        await db.SaveChangesAsync();
        var app = Assert.Single(await SeedAdmittedApplicationsAsync(db, competition, 1, RecruitmentApplicationStatus.Submitted));
        var session = await LinkAsync(db, app);
        var portal = CreatePortal(db);

        var open = await portal.GetPageAsync(session);
        var missing = open.MissingDocumentTypes.Select(m => m.Code).ToList();
        Assert.Contains("Certificate", missing);
        Assert.Contains("IdCopy", missing);
        Assert.True(open.CanUploadDocuments);
        Assert.Null(open.DocumentsClosedReason);

        Assert.True((await CreateLifecycle(db).CloseIntakeAsync(competition.Id, Creator)).Success);
        db.ChangeTracker.Clear();
        var closed = await portal.GetPageAsync(session);
        Assert.False(closed.CanUploadDocuments);
        Assert.NotNull(closed.DocumentsClosedReason);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The portal as DI builds it (with the competition services).</summary>
    internal static CandidatePortalService CreatePortal(HRCoreDbContext db)
    {
        var settings = new RecruitmentOptions();
        var opts = Microsoft.Extensions.Options.Options.Create(settings);
        return new CandidatePortalService(
            db,
            new CandidatePublicStatusService(db, opts),
            CreateOfferServiceWithRecall(db, options: settings),
            new RecruitmentSelectionService(db),
            Hcm028AppealTestSupport.CreateService(db, options: settings),
            new OfferLetterDocumentService(db, new LegislationRegisterProvider(db), opts),
            Hcm030TestSupport.CreateCandidateDocumentService(db, null),
            new UnifiedIntakeService(db, opts),
            opts,
            NullLogger<CandidatePortalService>.Instance,
            new CompetitionReserveService(db),
            new CompetitionObjectionService(db, Hcm028AppealTestSupport.CreateService(db, options: settings), CreateResults(db)),
            new ExamAdmissionCardDocumentService(db, opts));
    }

    private static CandidatePortalController CreateController(HRCoreDbContext db, CandidatePortalContext session)
    {
        var controller = new CandidatePortalController(null!, null!, null!, CreatePortal(db));
        var http = new DefaultHttpContext();
        http.Items[CandidatePortalSessionFilter.ItemKey] = session;
        controller.ControllerContext = new ControllerContext { HttpContext = http, RouteData = new RouteData() };
        return controller;
    }

    /// <summary>
    /// Links a public citizen application (the competition's posting + a reference) to the recruitment application, with
    /// an e-mail and (optionally) notification consent; returns the portal session.
    /// </summary>
    internal static async Task<CandidatePortalContext> LinkAsync(HRCoreDbContext db, RecruitmentApplication application, bool consent = false)
    {
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == application.Id);
        var posting = new PublicJobPosting { TenantId = app.TenantId, Title = app.AppliedPosition, IsPublished = true };
        db.PublicJobPostings.Add(posting);
        var citizen = new CitizenApplication
        {
            TenantId = app.TenantId,
            PublicJobPostingId = posting.Id,
            PromotedRecruitmentApplicationId = app.Id,
            ReferenceNumber = $"CA-H31-{app.Id.ToString("N")[..10]}",
            FullName = app.ApplicantFullName,
            NationalId = app.NationalId ?? "0000000000",
            NationalIdHash = app.NationalIdHash,
            Email = "candidate@test.sy",
            NotificationChannel = consent ? NotificationChannel.Email : null,
            NotificationConsentAt = consent ? DateTime.UtcNow.AddDays(-30) : null
        };
        db.CitizenApplications.Add(citizen);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new CandidatePortalContext(citizen.Id, citizen.TenantId, app.Id, citizen.ReferenceNumber, DateTime.UtcNow.AddMinutes(30));
    }
}
