using System.Reflection;
using System.Security.Claims;
using System.Text;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;
using FakeCandidateDocumentClient = HRManagement.UnitTests.Hcm030TestSupport.FakeCandidateDocumentClient;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (PLAN-30 J): «قبول نيابةً عن المرشح» on OfferLetters/Details — reason (≥ 20) and signed evidence are mandatory,
/// the evidence is relayed to File.Storage server-side and its reference is staged in the offer's tenant in the same save;
/// a failed relay or a refused acceptance saves nothing. Audit <c>AcceptOfferOnBehalf</c>; policy stays HrCoreWrite. The
/// HR decline keeps the optional evidence; evidence downloads go through HR.Core (Clean only).
/// </summary>
public sealed class Hcm030OnBehalfAcceptanceTests
{
    private const string ValidReason = "سلّم المرشح قبولاً ورقياً موقّعاً في مكتب الموارد البشرية";
    private static readonly Guid OfferTenant = Guid.Parse("30300000-0000-4000-8000-0000000000aa");
    private static readonly Guid HrUserId = Guid.Parse("30300000-0000-4000-8000-0000000000bb");

    private static byte[] Pdf(int size = 64)
    {
        var bytes = new byte[size];
        Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(bytes, 0);
        return bytes;
    }

    private static IFormFile Form(byte[] bytes, string fileName = "signed-acceptance.pdf", string contentType = "application/pdf") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "EvidenceFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    // ------------------------------------------------------------------ reason and evidence

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("قبول ورقي")]
    [InlineData("1234567890123456789")] // 19 characters
    public async Task Reason_shorter_than_twenty_characters_is_refused_and_nothing_is_relayed(string? reason)
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var (success, error) = await CreateService(db, files).AcceptAsync(offerId, reason, Form(Pdf()), HrUserId);

        Assert.False(success);
        Assert.Equal(OfferOnBehalfService.ReasonRequiredMessage, error);
        Assert.Contains("20", error);
        Assert.Empty(files.Uploads);
        await AssertNothingSavedAsync(db, offerId);
    }

    [Fact]
    public async Task Twenty_characters_after_trimming_are_enough()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);

        var (success, error) = await CreateService(db, new FakeCandidateDocumentClient())
            .AcceptAsync(offerId, "  12345678901234567890  ", Form(Pdf()), HrUserId);

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        Assert.Equal("12345678901234567890", (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).AcceptanceNote);
    }

    [Fact]
    public async Task Reason_longer_than_1000_characters_is_refused()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);

        var (success, error) = await CreateService(db, new FakeCandidateDocumentClient())
            .AcceptAsync(offerId, new string('س', 1001), Form(Pdf()), HrUserId);

        Assert.False(success);
        Assert.Equal(OfferOnBehalfService.ReasonTooLongMessage, error);
    }

    [Fact]
    public async Task Evidence_is_required()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();
        var service = CreateService(db, files);

        var missing = await service.AcceptAsync(offerId, ValidReason, evidence: null, HrUserId);
        var empty = await service.AcceptAsync(offerId, ValidReason, Form([]), HrUserId);

        Assert.Equal((false, OfferOnBehalfService.EvidenceRequiredMessage), missing);
        Assert.Equal((false, OfferOnBehalfService.EvidenceRequiredMessage), empty);
        Assert.Empty(files.Uploads);
        await AssertNothingSavedAsync(db, offerId);
    }

    [Theory]
    [InlineData("acceptance.exe", "application/octet-stream")]
    [InlineData("acceptance.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task Evidence_outside_pdf_jpg_png_is_refused_before_the_relay(string fileName, string contentType)
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var (success, error) = await CreateService(db, files).AcceptAsync(offerId, ValidReason, Form(Pdf(), fileName, contentType), HrUserId);

        Assert.False(success);
        Assert.StartsWith("مستند الإثبات: ", error);
        Assert.Empty(files.Uploads);
        await AssertNothingSavedAsync(db, offerId);
    }

    [Fact]
    public async Task Evidence_whose_content_is_not_a_pdf_is_refused()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var (success, error) = await CreateService(db, files)
            .AcceptAsync(offerId, ValidReason, Form(Encoding.ASCII.GetBytes("not a pdf at all, just text")), HrUserId);

        Assert.False(success);
        Assert.StartsWith("مستند الإثبات: ", error);
        Assert.Empty(files.Uploads);
    }

    [Fact]
    public async Task Evidence_above_five_megabytes_is_refused()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var (success, error) = await CreateService(db, files).AcceptAsync(offerId, ValidReason, Form(Pdf(5 * 1024 * 1024 + 1)), HrUserId);

        Assert.False(success);
        Assert.Contains("5 ميغابايت", error);
        Assert.Empty(files.Uploads);
    }

    // ------------------------------------------------------------------ relay, save, audit, outbox

    [Fact]
    public async Task Upload_failure_returns_the_arabic_error_and_saves_nothing()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient { FailAfter = 0 };

        var (success, error) = await CreateService(db, files).AcceptAsync(offerId, ValidReason, Form(Pdf()), HrUserId);

        Assert.False(success);
        Assert.Equal(OfferOnBehalfService.EvidenceUploadFailedMessage, error);
        Assert.Empty(db.ChangeTracker.Entries<FileReferenceOutboxMessage>());
        await AssertNothingSavedAsync(db, offerId);
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);
    }

    [Fact]
    public async Task Accept_on_behalf_records_the_reason_evidence_audit_and_the_outbox_row_in_the_offers_tenant()
    {
        await using var db = CreateContext();
        var (offerId, appId) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var (success, error) = await CreateService(db, files).AcceptAsync(offerId, ValidReason, Form(Pdf()), HrUserId);

        Assert.True(success, error);
        var upload = Assert.Single(files.Uploads);
        Assert.Equal(OfferTenant, upload.TenantId);
        Assert.Equal("application/pdf", upload.File.ContentType);

        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Accepted, offer.Status);
        Assert.Equal(OfferAcceptedBy.HrOnBehalf, offer.AcceptedBy);
        Assert.Equal(ValidReason, offer.AcceptanceNote);
        Assert.Equal(upload.FileId, offer.AcceptanceEvidenceFileId);
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, (await db.RecruitmentApplications.SingleAsync(a => a.Id == appId)).Status);

        var audit = await db.AuditLogs.SingleAsync(l => l.EntityId == offerId && l.Action == "AcceptOfferOnBehalf");
        Assert.Equal(HrUserId, audit.UserId);
        Assert.Equal(OfferTenant, audit.TenantId);
        Assert.Contains(upload.FileId.ToString(), audit.ChangedFields);
        Assert.Contains(OfferAcceptedBy.HrOnBehalf, audit.ChangedFields);
        Assert.False(await db.AuditLogs.AnyAsync(l => l.EntityId == offerId && l.Action == "AcceptOffer"));

        var outbox = await db.FileReferenceOutbox.SingleAsync();
        Assert.Equal(upload.FileId, outbox.FileId);
        Assert.Equal($"hr-core:OfferLetter:{offerId:N}:AcceptanceEvidence", outbox.HolderKey);
        Assert.Equal(FileReferenceOutboxOps.Register, outbox.Operation);
        Assert.Equal(OfferTenant, outbox.TenantId); // explicit: the outbox writer alone would stamp Guid.Empty here
    }

    [Fact]
    public async Task Expired_offer_is_refused_before_the_relay()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        offer.ExpiryDate = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var files = new FakeCandidateDocumentClient();

        var (success, error) = await CreateService(db, files).AcceptAsync(offerId, ValidReason, Form(Pdf()), HrUserId);

        Assert.False(success);
        Assert.Equal(RecruitmentOfferService.ExpiredOfferMessage, error);
        Assert.Empty(files.Uploads);
        await AssertNothingSavedAsync(db, offerId);
    }

    [Fact]
    public async Task Unknown_offer_is_refused()
    {
        await using var db = CreateContext();
        var (success, error) = await CreateService(db, new FakeCandidateDocumentClient())
            .AcceptAsync(Guid.NewGuid(), ValidReason, Form(Pdf()), HrUserId);

        Assert.False(success);
        Assert.Equal(OfferOnBehalfService.OfferNotFoundMessage, error);
    }

    // ------------------------------------------------------------------ controller

    [Fact]
    public async Task Controller_accepts_on_behalf_and_reports_the_outcome()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var service = CreateService(db, new FakeCandidateDocumentClient());

        var refused = await CreateController(db).AcceptOffer(
            offerId, new AcceptOfferOnBehalfForm { Reason = "قصير" }, service, CancellationToken.None);
        var controller = CreateController(db);
        var accepted = await controller.AcceptOffer(
            offerId, new AcceptOfferOnBehalfForm { Reason = ValidReason, EvidenceFile = Form(Pdf()) }, service, CancellationToken.None);

        Assert.Equal(nameof(OfferLettersController.Details), Assert.IsType<RedirectToActionResult>(refused).ActionName);
        Assert.Equal(nameof(OfferLettersController.Details), Assert.IsType<RedirectToActionResult>(accepted).ActionName);
        Assert.Equal("تم تسجيل قبول خطاب العرض نيابةً عن المرشح", controller.TempData["Success"]);
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferAcceptedBy.HrOnBehalf, offer.AcceptedBy);
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == offerId && l.Action == "AcceptOfferOnBehalf"));
        Assert.Equal(HrUserId, (await db.AuditLogs.SingleAsync(l => l.Action == "AcceptOfferOnBehalf")).UserId);
    }

    [Fact]
    public void Accept_keeps_HrCoreWrite_and_anti_forgery()
    {
        var method = typeof(OfferLettersController).GetMethod(nameof(OfferLettersController.AcceptOffer))!;
        var policies = method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToList();

        Assert.Equal([HrManagementPolicies.HrCoreWrite], policies);
        Assert.DoesNotContain(HrManagementPolicies.HrCoreApprove, policies);
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }

    // ------------------------------------------------------------------ decline (optional evidence)

    [Fact]
    public async Task Decline_without_evidence_relays_nothing()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var result = await CreateService(db, files).DeclineAsync(offerId, "قبل عرضاً آخر", evidence: null, HrUserId);

        Assert.True(result.Success, result.Error);
        Assert.Empty(files.Uploads);
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Declined, offer.Status);
        Assert.Equal(OfferDeclinedBy.HrOnBehalf, offer.DeclinedBy);
        Assert.Null(offer.DeclineEvidenceFileId);
        Assert.Empty(await db.FileReferenceOutbox.ToListAsync());
    }

    [Fact]
    public async Task Decline_with_evidence_stores_the_file_and_its_reference()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var result = await CreateService(db, files).DeclineAsync(offerId, "قبل عرضاً آخر", Form(Pdf(), "signed-decline.pdf"), HrUserId);

        Assert.True(result.Success, result.Error);
        var upload = Assert.Single(files.Uploads);
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(upload.FileId, offer.DeclineEvidenceFileId);
        var outbox = await db.FileReferenceOutbox.SingleAsync();
        Assert.Equal($"hr-core:OfferLetter:{offerId:N}:DeclineEvidence", outbox.HolderKey);
        Assert.Equal(OfferTenant, outbox.TenantId);
        Assert.Contains(upload.FileId.ToString(), (await db.AuditLogs.SingleAsync(l => l.Action == "DeclineOffer")).ChangedFields);
    }

    [Theory]
    [InlineData(2, "سبب الرفض مطلوب")]
    [InlineData(1001, "سبب الرفض يجب ألا يتجاوز 1000 حرف.")]
    public async Task Decline_with_a_short_or_too_long_reason_relays_no_evidence(int reasonLength, string expectedError)
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();

        var result = await CreateService(db, files)
            .DeclineAsync(offerId, new string('ر', reasonLength), Form(Pdf()), HrUserId);

        Assert.False(result.Success);
        Assert.StartsWith(expectedError, result.Error, StringComparison.Ordinal);
        Assert.Empty(files.Uploads);
        await AssertNothingSavedAsync(db, offerId);
    }

    [Fact]
    public async Task Decline_upload_failure_saves_nothing()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);

        var result = await CreateService(db, new FakeCandidateDocumentClient { FailAfter = 0 })
            .DeclineAsync(offerId, "قبل عرضاً آخر", Form(Pdf()), HrUserId);

        Assert.False(result.Success);
        Assert.Equal(OfferOnBehalfService.EvidenceUploadFailedMessage, result.Error);
        await AssertNothingSavedAsync(db, offerId);
    }

    // ------------------------------------------------------------------ evidence download and display

    [Fact]
    public async Task Evidence_downloads_only_once_clean()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();
        var service = CreateService(db, files);
        Assert.True((await service.AcceptAsync(offerId, ValidReason, Form(Pdf()), HrUserId)).Success);
        var fileId = files.Uploads.Single().FileId;

        var pending = await CreateController(db).Evidence(offerId, OfferEvidenceKind.Acceptance, service, CancellationToken.None);
        Assert.IsType<NotFoundResult>(pending);
        Assert.Empty(files.ContentRequests);

        files.ScanStatuses[fileId] = CandidateDocumentScanStatus.Clean;
        var clean = await CreateController(db).Evidence(offerId, OfferEvidenceKind.Acceptance, service, CancellationToken.None);
        Assert.Equal("application/pdf", Assert.IsType<FileContentResult>(clean).ContentType);

        // The offer has no decline evidence.
        Assert.IsType<NotFoundResult>(await CreateController(db).Evidence(offerId, OfferEvidenceKind.Decline, service, CancellationToken.None));
    }

    [Fact]
    public async Task Evidence_download_reports_an_unavailable_file_storage()
    {
        await using var db = CreateContext();
        var (offerId, _) = await IssueOfferAsync(db);
        var files = new FakeCandidateDocumentClient();
        var service = CreateService(db, files);
        Assert.True((await service.AcceptAsync(offerId, ValidReason, Form(Pdf()), HrUserId)).Success);
        files.MetadataUnavailable = true;
        var controller = CreateController(db);

        var result = await controller.Evidence(offerId, OfferEvidenceKind.Acceptance, service, CancellationToken.None);

        Assert.Equal(nameof(OfferLettersController.Details), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(CandidateDocumentService.DownloadUnavailableMessage, controller.TempData["Error"]);
    }

    [Theory]
    [InlineData(OfferAcceptedBy.Candidate, "المرشح عبر بوابة التوظيف")]
    [InlineData(OfferAcceptedBy.HrOnBehalf, "HR نيابة عن المرشح")]
    [InlineData(null, "غير مسجَّل (قبول سابق لتسجيل جهة القبول)")]
    public void Accepted_by_labels_shown_on_details(string? acceptedBy, string label) =>
        Assert.Equal(label, OfferAcceptedBy.LabelAr(acceptedBy));

    [Fact]
    public void Details_shows_who_accepted_with_reason_and_evidence_link_and_has_no_native_confirm()
    {
        var view = System.IO.File.ReadAllText(FindRepoFile("src/Services/HRCore/HR.Core.Web/Views/OfferLetters/Details.cshtml"));

        Assert.Contains("OfferAcceptedBy.LabelAr(offer.AcceptedBy)", view);
        Assert.Contains("offer.AcceptanceNote", view);
        Assert.Contains("asp-action=\"Evidence\"", view);
        Assert.Contains("قبول نيابةً عن المرشح", view);
        Assert.Contains("enctype=\"multipart/form-data\"", view);
        Assert.Contains("data-hr-confirm=", view);
        Assert.DoesNotContain("confirm(", view);
        Assert.DoesNotContain("onsubmit=", view);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<(Guid OfferId, Guid ApplicationId)> IssueOfferAsync(HRCoreDbContext db)
    {
        var (offerId, appId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true);
        // The offer's tenant differs from the (empty) tenant the outbox writer stamps in these tests.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"OfferLetters\" SET \"TenantId\" = {0}", OfferTenant.ToString().ToUpperInvariant());
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == offerId)).Status);
        db.ChangeTracker.Clear();
        return (offerId, appId);
    }

    private static async Task AssertNothingSavedAsync(HRCoreDbContext db, Guid offerId)
    {
        db.ChangeTracker.Clear();
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == offerId);
        Assert.Equal(OfferStatus.Issued, offer.Status);
        Assert.Null(offer.AcceptedBy);
        Assert.Null(offer.AcceptanceEvidenceFileId);
        Assert.Null(offer.DeclineEvidenceFileId);
        Assert.Empty(await db.FileReferenceOutbox.ToListAsync());
        Assert.False(await db.AuditLogs.AnyAsync(l => l.EntityId == offerId && (l.Action == "AcceptOfferOnBehalf" || l.Action == "DeclineOffer")));
    }

    private static OfferOnBehalfService CreateService(HRCoreDbContext db, ICandidateDocumentUploadClient files) =>
        new(db, CreateOfferService(db), files, new EfFileReferenceOutbox(db),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

    private static OfferLettersController CreateController(HRCoreDbContext db)
    {
        var controller = new OfferLettersController(db, null!, null!, null!, null!, null!, null!, null!);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, HrUserId.ToString())], "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
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
}
