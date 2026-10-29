using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-029 (D): official offer letter PDF with a verification hash fixed at issue.</summary>
public class Hcm029OfferDocumentTests
{
    [Fact]
    public async Task Approval_issues_the_offer_and_stores_the_document_hash()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await SeedOfferAsync(db, OfferStatus.PendingApproval);

        await new OfferLetterWorkflowHandler(db, CreateDocuments(db))
            .HandleAsync(new WorkflowInstance { Id = offer.WorkflowInstanceId ?? Guid.NewGuid(), EntityType = "OfferLetter", EntityId = offer.Id }, approved: true, null);

        var issued = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id);
        Assert.Equal(OfferStatus.Issued, issued.Status);
        Assert.Matches("^[0-9a-f]{64}$", issued.DocumentSha256);
        Assert.NotNull(issued.DocumentGeneratedAt);

        var view = (await CreateDocuments(db).GetAsync(offer.Id))!;
        Assert.True(view.IsIntact);
        Assert.Equal(issued.DocumentSha256, view.CurrentSha256);
        Assert.Equal(issued.DocumentSha256![..12].ToUpperInvariant(), view.VerificationCode);
    }

    [Fact]
    public async Task Handler_built_without_the_service_still_stamps_the_hash()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await SeedOfferAsync(db, OfferStatus.PendingApproval);

        await new OfferLetterWorkflowHandler(db)
            .HandleAsync(new WorkflowInstance { Id = offer.WorkflowInstanceId ?? Guid.NewGuid(), EntityType = "OfferLetter", EntityId = offer.Id }, approved: true, null);

        Assert.NotNull((await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).DocumentSha256);
    }

    [Fact]
    public async Task Rejection_returns_to_draft_without_a_hash()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await SeedOfferAsync(db, OfferStatus.PendingApproval);

        await new OfferLetterWorkflowHandler(db, CreateDocuments(db))
            .HandleAsync(new WorkflowInstance { Id = offer.WorkflowInstanceId ?? Guid.NewGuid(), EntityType = "OfferLetter", EntityId = offer.Id }, approved: false, "مراجعة");

        var draft = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id);
        Assert.Equal(OfferStatus.Draft, draft.Status);
        Assert.Null(draft.DocumentSha256);
    }

    [Fact]
    public async Task Hash_is_stable_across_renders_and_cultures()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await IssueAsync(db);
        var first = (await CreateDocuments(db).GetAsync(offer.Id))!;

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SY");
            var second = (await CreateDocuments(db).GetAsync(offer.Id))!;
            Assert.Equal(first.CurrentSha256, second.CurrentSha256);
            Assert.Equal(OfferLetterDocumentService.CanonicalJson(first.Data), OfferLetterDocumentService.CanonicalJson(second.Data));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        Assert.True(first.IsIntact);
        Assert.Contains("\"offeredSalary\":\"5000.00\"", OfferLetterDocumentService.CanonicalJson(first.Data));
    }

    [Fact]
    public async Task A_field_changed_after_issue_is_detected_instead_of_rehashed()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await IssueAsync(db);
        var stored = offer.DocumentSha256;

        offer.OfferedSalary = 9999;
        await db.SaveChangesAsync();
        var view = (await CreateDocuments(db).GetAsync(offer.Id))!;

        Assert.False(view.IsIntact);
        Assert.Equal(stored, view.StoredSha256);
        Assert.Equal(stored![..12].ToUpperInvariant(), view.VerificationCode);
        Assert.Equal(stored, (await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).DocumentSha256);
    }

    [Fact]
    public async Task Pdf_is_generated_for_an_issued_offer()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await IssueAsync(db);
        var view = (await CreateDocuments(db).GetAsync(offer.Id))!;

        var pdf = OfferLetterDocument.Generate(view);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.DoesNotContain("0127", view.Data.CandidateName);
    }

    [Theory]
    [InlineData(OfferStatus.Draft)]
    [InlineData(OfferStatus.PendingApproval)]
    [InlineData(OfferStatus.Declined)]
    public async Task Download_is_refused_unless_issued_or_accepted(OfferStatus status)
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await SeedOfferAsync(db, status);
        var controller = CreateController(db);

        var result = await controller.Document(offer.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(OfferLettersController.Details), redirect.ActionName);
        Assert.Equal(OfferLetterDocumentService.NotDownloadableMessage, controller.TempData["Error"]);
    }

    [Theory]
    [InlineData(OfferStatus.Issued)]
    [InlineData(OfferStatus.Accepted)]
    public async Task Download_streams_the_pdf_when_issued_or_accepted(OfferStatus status)
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var offer = await IssueAsync(db);
        offer.Status = status;
        await db.SaveChangesAsync();

        var result = await CreateController(db).Document(offer.Id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.NotEmpty(file.FileContents);
    }

    [Fact]
    public async Task Unknown_offer_is_not_found()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        Assert.IsType<NotFoundResult>(await CreateController(db).Document(Guid.NewGuid(), CancellationToken.None));
    }

    private static OfferLetterDocumentService CreateDocuments(HRCoreDbContext db) =>
        new(db, new LegislationRegisterProvider(db), Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

    private static OfferLettersController CreateController(HRCoreDbContext db)
    {
        var controller = new OfferLettersController(db, null!, null!, null!, null!, null!, null!, CreateDocuments(db));
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static async Task<OfferLetter> IssueAsync(HRCoreDbContext db)
    {
        var offer = await SeedOfferAsync(db, OfferStatus.PendingApproval);
        await new OfferLetterWorkflowHandler(db, CreateDocuments(db))
            .HandleAsync(new WorkflowInstance { Id = offer.WorkflowInstanceId ?? Guid.NewGuid(), EntityType = "OfferLetter", EntityId = offer.Id }, approved: true, null);
        return offer;
    }

    private static async Task<OfferLetter> SeedOfferAsync(HRCoreDbContext db, OfferStatus status)
    {
        var req = await Hcm027TestSupport.SeedRequisitionAsync(db);
        var app = await Hcm027TestSupport.SeedApplicationAsync(db, req, RecruitmentApplicationStatus.OfferExtended, 1, "سارة المرشحة");
        app.NationalId = "0127290001";
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            FundedPositionId = req.FundedPositionId,
            OfferNumber = $"OFF-H29-{Guid.NewGuid():N}"[..20],
            Status = status,
            // PLAN-25 (review finding 2): the handler acts only on the offer's linked instance.
            WorkflowInstanceId = status == OfferStatus.PendingApproval ? Guid.NewGuid() : null,
            OfferedSalary = 5000,
            GradeCode = "G5",
            ProposedStartDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidityDays = 14,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        return offer;
    }
}
