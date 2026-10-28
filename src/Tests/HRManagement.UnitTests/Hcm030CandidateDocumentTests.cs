using System.Net;
using System.Text;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static HRManagement.UnitTests.Hcm030TestSupport;
using R = HR.Core.Domain.Services.RecruitmentEligibilityRules;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (E2–E6): candidate documents — local limits, required types → eligibility flag, the single apply save
/// (documents + File.Storage reference outbox rows, explicit tenant), clean failure when File.Storage is unavailable,
/// and the HR card/download (Clean only, caller's tenant only).
/// </summary>
public class Hcm030CandidateDocumentTests
{
    private static readonly Guid TenantB = Guid.Parse("30300000-0000-4000-8000-00000000000b");

    private static byte[] Pdf(int size = 64)
    {
        var bytes = new byte[size];
        Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(bytes, 0);
        return bytes;
    }

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static IFormFile Form(byte[] bytes, string fileName, string contentType = "application/pdf", string field = "CvFile") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, field, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static CandidateDocumentFileValidator.Input Input(IFormFile file, string type = CandidateDocumentTypes.Cv) =>
        new(type, file, CitizenApplicationApplyViewModel.FieldFor(type));

    private static Task<CandidateDocumentFileValidator.Outcome> Prepare(params CandidateDocumentFileValidator.Input[] inputs) =>
        CandidateDocumentFileValidator.PrepareAsync(inputs, new RecruitmentCandidateDocumentsOptions());

    /// <summary>
    /// SQLite context with the production <see cref="TenantIdStampInterceptor"/> and NO ambient tenant (the anonymous
    /// portal): an untenanted insert throws, as it would at the real apply.
    /// </summary>
    private static (HRCoreDbContext Db, SaveCounter Saves, SqliteConnection Connection) CreatePortalContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var saves = new SaveCounter();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(saves, new TenantIdStampInterceptor(Mock.Of<ITenantContext>()))
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return (db, saves, connection);
    }

    /// <summary>A context scoped to <paramref name="tenantId"/> (tenant query filters on) over the same database.</summary>
    private static HRCoreDbContext TenantContext(SqliteConnection connection, Guid tenantId)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        return new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection).Options, tenant.Object);
    }

    private static async Task<Fixture> SeedWithRequiredAsync(HRCoreDbContext db, string? required)
    {
        var fixture = await SeedRequisitionAsync(db);
        fixture.Requisition.RequiredDocumentTypes = required;
        await db.SaveChangesAsync();
        return fixture;
    }

    // ---- E2: local limits ----

    [Fact]
    public async Task Valid_pdf_and_png_are_prepared_with_the_detected_content_type()
    {
        var outcome = await Prepare(
            Input(Form(Pdf(), "cv.pdf")),
            Input(Form(PngHeader, "id.png", "image/png", "IdCopyFile"), CandidateDocumentTypes.IdCopy));

        Assert.True(outcome.IsValid);
        Assert.Equal(["application/pdf", "image/png"], outcome.Files.Select(f => f.ContentType));
        Assert.Equal([CandidateDocumentTypes.Cv, CandidateDocumentTypes.IdCopy], outcome.Files.Select(f => f.DocumentType));
    }

    [Fact]
    public async Task More_than_MaxFiles_is_refused()
    {
        var inputs = Enumerable.Range(1, 6)
            .Select(i => Input(Form(Pdf(), $"c{i}.pdf"), CandidateDocumentTypes.Certificate))
            .ToArray();

        var outcome = await Prepare(inputs);

        Assert.False(outcome.IsValid);
        Assert.Empty(outcome.Files);
        Assert.Contains((string.Empty, CandidateDocumentFileValidator.TooManyFilesMessage(5)), outcome.Errors);
    }

    [Fact]
    public async Task A_file_over_MaxBytes_is_refused_and_exactly_MaxBytes_is_accepted()
    {
        var tooLarge = await Prepare(Input(Form(Pdf(5 * 1024 * 1024 + 1), "cv.pdf")));
        var atLimit = await Prepare(Input(Form(Pdf(5 * 1024 * 1024), "cv.pdf")));

        Assert.Equal([(nameof(CitizenApplicationApplyViewModel.CvFile), "حجم الملف يجب ألا يتجاوز 5 ميغابايت.")], tooLarge.Errors);
        Assert.True(atLimit.IsValid);
    }

    [Fact]
    public async Task A_total_over_MaxTotalBytes_is_refused_before_any_file_is_read()
    {
        // Two files of 4.8 MB each: each is within 5 MB, together over 9 MiB.
        var size = 4_800_000;
        var outcome = await Prepare(
            Input(Form(Pdf(size), "a.pdf")),
            Input(Form(Pdf(size), "b.pdf", field: "OtherFiles"), CandidateDocumentTypes.Other));

        Assert.Equal([(string.Empty, "الحجم الإجمالي للمستندات يجب ألا يتجاوز 9 ميغابايت.")], outcome.Errors);
        Assert.Equal(9_437_184, new RecruitmentCandidateDocumentsOptions().MaxTotalBytes);
    }

    [Theory]
    [InlineData("cv.docx", "application/pdf", "نوع الملف غير مدعوم.")]
    [InlineData("cv.exe", "application/pdf", "نوع الملف غير مدعوم.")]
    [InlineData("cv.pdf", "text/html", "نوع المحتوى غير مدعوم.")]
    public async Task Disallowed_extension_or_declared_type_is_refused(string fileName, string contentType, string expected)
    {
        var outcome = await Prepare(Input(Form(Pdf(), fileName, contentType)));

        Assert.Equal([(nameof(CitizenApplicationApplyViewModel.CvFile), expected)], outcome.Errors);
    }

    [Fact]
    public async Task Content_that_does_not_match_the_extension_is_refused()
    {
        var outcome = await Prepare(Input(Form(PngHeader, "cv.pdf")));

        Assert.Equal([(nameof(CitizenApplicationApplyViewModel.CvFile), "تعارض بين امتداد الملف ومحتواه (magic bytes).")], outcome.Errors);
    }

    [Fact]
    public void Long_file_names_are_sanitized_and_shortened_keeping_the_extension()
    {
        var name = CandidateDocumentFileValidator.SafeFileName(new string('a', 300) + ".pdf");

        Assert.Equal(CandidateDocumentFileValidator.MaxFileNameLength, name.Length);
        Assert.EndsWith(".pdf", name);
        Assert.Equal("cv.pdf", CandidateDocumentFileValidator.SafeFileName(@"C:\fakepath\cv.pdf"));
    }

    // ---- E4: required types and the eligibility flag ----

    [Fact]
    public void Required_types_are_stored_as_normalized_codes()
    {
        Assert.Equal("Cv,IdCopy", CandidateDocumentTypes.Format(["idcopy", "Cv", "unknown", "CV"]));
        Assert.Equal([CandidateDocumentTypes.Cv, CandidateDocumentTypes.IdCopy], CandidateDocumentTypes.Parse("IdCopy, Cv"));
        Assert.Null(CandidateDocumentTypes.Format([]));
        Assert.Equal("صورة الهوية", CandidateDocumentTypes.LabelAr(CandidateDocumentTypes.IdCopy));
    }

    [Fact]
    public void Missing_required_documents_flag_is_toggled_alone_in_display_order()
    {
        var withFlag = R.WithRequiredDocuments(
            $"{R.BelowMinExperience},{R.MissingProfileData}", [CandidateDocumentTypes.Cv], []);
        var cleared = R.WithRequiredDocuments(withFlag, [CandidateDocumentTypes.Cv], [CandidateDocumentTypes.Cv]);

        Assert.Equal($"{R.BelowMinExperience},{R.MissingProfileData},{R.MissingRequiredDocuments}", withFlag);
        Assert.Equal($"{R.BelowMinExperience},{R.MissingProfileData}", cleared);
        Assert.Null(R.WithRequiredDocuments(null, [], []));
        Assert.Equal("مستندات مطلوبة ناقصة", R.LabelAr(R.MissingRequiredDocuments));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Apply_flags_missing_required_documents_but_never_refuses(bool attachCv, bool attachId, bool flagged)
    {
        var (db, _) = CreateContext();
        var fixture = await SeedWithRequiredAsync(db, "Cv,IdCopy");
        var vm = ApplyModel(fixture.Posting.Id);
        if (attachCv)
            vm.CvFile = Form(Pdf(), "cv.pdf");
        if (attachId)
            vm.IdCopyFile = Form(PngHeader, "id.png", "image/png", "IdCopyFile");

        var result = await CreatePublicCareersController(db, new FakeCandidateDocumentClient()).Apply(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var app = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(flagged, R.Parse(app.EligibilityFlags).Contains(R.MissingRequiredDocuments));
        Assert.NotNull(app.EligibilityEvaluatedAt);
    }

    [Fact]
    public async Task Requisition_without_required_types_is_never_flagged()
    {
        var (db, _) = CreateContext();
        var fixture = await SeedWithRequiredAsync(db, null);

        await CreatePublicCareersController(db, new FakeCandidateDocumentClient()).Apply(ApplyModel(fixture.Posting.Id));

        Assert.DoesNotContain(R.MissingRequiredDocuments, R.Parse((await db.RecruitmentApplications.SingleAsync()).EligibilityFlags));
    }

    [Fact]
    public async Task Reevaluation_counts_saved_and_staged_documents()
    {
        var (db, _) = CreateContext();
        var fixture = await SeedWithRequiredAsync(db, "Cv");
        await CreatePublicCareersController(db, new FakeCandidateDocumentClient()).Apply(ApplyModel(fixture.Posting.Id));
        var app = await db.RecruitmentApplications.Include(a => a.Requisition).SingleAsync();
        Assert.Contains(R.MissingRequiredDocuments, R.Parse(app.EligibilityFlags));

        // PLAN-30 S3 path: a later upload is staged, then the flag is re-evaluated before the save.
        db.CandidateDocuments.Add(new CandidateDocument
        {
            TenantId = TenantA,
            RecruitmentApplicationId = app.Id,
            DocumentType = CandidateDocumentTypes.Cv,
            FileId = Guid.NewGuid(),
            OriginalFileName = "cv.pdf",
            ContentType = "application/pdf",
            SizeBytes = 64,
            UploadedAt = DateTime.UtcNow
        });
        await new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()))
            .ReevaluateDocumentFlagAsync(app);

        Assert.DoesNotContain(R.MissingRequiredDocuments, R.Parse(app.EligibilityFlags));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reevaluation_on_the_anonymous_portal_reads_the_applications_own_tenant(bool requisitionLoaded)
    {
        var (seedDb, _, connection) = CreatePortalContext();
        var fixture = await SeedWithRequiredAsync(seedDb, "Cv,IdCopy");
        var vm = ApplyModel(fixture.Posting.Id);
        vm.CvFile = Form(Pdf(), "cv.pdf");
        await CreatePublicCareersController(seedDb, new FakeCandidateDocumentClient()).Apply(vm);

        // PLAN-30 S3 (/careers/my): tenant query filters on, no ambient tenant (EffectiveTenantId = Guid.Empty).
        using var db = new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection).Options, Mock.Of<ITenantContext>());
        var apps = db.RecruitmentApplications.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal);
        var app = requisitionLoaded
            ? await apps.Include(a => a.Requisition).SingleAsync()
            : await apps.SingleAsync();
        Assert.Contains(R.MissingRequiredDocuments, R.Parse(app.EligibilityFlags));

        db.CandidateDocuments.Add(new CandidateDocument
        {
            TenantId = TenantA,
            RecruitmentApplicationId = app.Id,
            DocumentType = CandidateDocumentTypes.IdCopy,
            FileId = Guid.NewGuid(),
            OriginalFileName = "id.png",
            ContentType = "image/png",
            SizeBytes = 64,
            UploadedAt = DateTime.UtcNow
        });
        await new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()))
            .ReevaluateDocumentFlagAsync(app);

        // The saved Cv (read under the explicit tenant) plus the staged IdCopy complete the requirement.
        Assert.DoesNotContain(R.MissingRequiredDocuments, R.Parse(app.EligibilityFlags));
    }

    // ---- E3/E5: one save with documents and outbox rows, explicit tenant ----

    [Fact]
    public async Task Apply_saves_documents_and_register_outbox_rows_in_one_save_with_the_posting_tenant()
    {
        var (db, saves, _) = CreatePortalContext();
        var fixture = await SeedWithRequiredAsync(db, "Cv");
        var files = new FakeCandidateDocumentClient();
        var vm = ApplyModel(fixture.Posting.Id);
        vm.CvFile = Form(Pdf(), "my cv.pdf");
        vm.CertificateFiles = [Form(Pdf(), "c1.pdf", field: "CertificateFiles"), Form(PngHeader, "c2.png", "image/png", "CertificateFiles")];
        var savesBefore = saves.Count;

        var result = await CreatePublicCareersController(db, files).Apply(vm);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, saves.Count - savesBefore);
        Assert.All(files.Uploads, u => Assert.Equal(TenantA, u.TenantId));

        var app = await db.RecruitmentApplications.SingleAsync();
        var citizen = await db.CitizenApplications.SingleAsync();
        var documents = await db.CandidateDocuments.OrderBy(d => d.OriginalFileName).ToListAsync();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d =>
        {
            Assert.Equal(TenantA, d.TenantId);
            Assert.Equal(app.Id, d.RecruitmentApplicationId);
            Assert.Equal(citizen.Id, d.CitizenApplicationId);
            Assert.Equal(CandidateDocument.UploadedByCandidate, d.UploadedBy);
        });
        Assert.Equal(["c1.pdf", "c2.png", "my cv.pdf"], documents.Select(d => d.OriginalFileName));
        Assert.Equal(files.Uploads.Select(u => u.FileId).Order(), documents.Select(d => d.FileId).Order());

        var outbox = await db.FileReferenceOutbox.ToListAsync();
        Assert.Equal(3, outbox.Count);
        Assert.All(outbox, o =>
        {
            Assert.NotEqual(Guid.Empty, o.TenantId);
            Assert.Equal(TenantA, o.TenantId);
            Assert.Equal(FileReferenceOutboxOps.Register, o.Operation);
        });
        Assert.Equal(
            documents.Select(d => FileReferenceClientExtensions.BuildHolderKey(
                HrFileServices.HrCore, nameof(CandidateDocument), d.Id, nameof(CandidateDocument.FileId))).Order(),
            outbox.Select(o => o.HolderKey).Order());
        Assert.Equal(documents.Select(d => d.FileId).Order(), outbox.Select(o => o.FileId).Order());
        Assert.DoesNotContain(R.MissingRequiredDocuments, R.Parse(app.EligibilityFlags));
    }

    [Fact]
    public void Staging_without_an_explicit_tenant_would_leave_the_outbox_row_untenanted()
    {
        // Guards the fix: EfFileReferenceOutbox alone stamps Guid.Empty on the portal (no ambient tenant).
        var (db, _) = CreateContext();
        new EfFileReferenceOutbox(db).EnqueueRegister(Guid.NewGuid(), "hr-core:CandidateDocument:x:FileId");

        Assert.Equal(Guid.Empty, db.ChangeTracker.Entries<FileReferenceOutboxMessage>().Single().Entity.TenantId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Apply_fails_cleanly_when_File_Storage_cannot_store_every_file(int failAfter)
    {
        var (db, saves, _) = CreatePortalContext();
        var fixture = await SeedWithRequiredAsync(db, "Cv");
        var files = new FakeCandidateDocumentClient { FailAfter = failAfter };
        var vm = ApplyModel(fixture.Posting.Id);
        vm.CvFile = Form(Pdf(), "cv.pdf");
        vm.OtherFiles = [Form(Pdf(), "other.pdf", field: "OtherFiles")];
        var savesBefore = saves.Count;
        var controller = CreatePublicCareersController(db, files);

        var result = await controller.Apply(vm);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Details", view.ViewName);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors,
            e => e.ErrorMessage == "تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.");
        Assert.Equal(savesBefore, saves.Count);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State != EntityState.Unchanged);
        Assert.Equal(0, await db.CitizenApplications.CountAsync());
        Assert.Equal(0, await db.RecruitmentApplications.CountAsync());
        Assert.Equal(0, await db.CandidateDocuments.CountAsync());
        Assert.Equal(0, await db.FileReferenceOutbox.CountAsync());
    }

    [Fact]
    public async Task Unconfigured_File_Storage_fails_the_apply_with_files_but_not_without()
    {
        var (db, _) = CreateContext();
        var fixture = await SeedWithRequiredAsync(db, "Cv");
        var withFile = ApplyModel(fixture.Posting.Id, "0101010101");
        withFile.CvFile = Form(Pdf(), "cv.pdf");

        var refused = await CreatePublicCareersController(db).Apply(withFile);
        var accepted = await CreatePublicCareersController(db).Apply(ApplyModel(fixture.Posting.Id, "0202020202"));

        Assert.IsType<ViewResult>(refused);
        Assert.IsType<RedirectToActionResult>(accepted);
        Assert.Equal(1, await db.RecruitmentApplications.CountAsync());
    }

    [Fact]
    public async Task Invalid_files_are_reported_on_their_field_and_nothing_is_uploaded()
    {
        var (db, saves) = CreateContext();
        var fixture = await SeedWithRequiredAsync(db, "Cv");
        var files = new FakeCandidateDocumentClient();
        var vm = ApplyModel(fixture.Posting.Id);
        vm.CvFile = Form(Pdf(), "cv.exe");
        var savesBefore = saves.Count;
        var controller = CreatePublicCareersController(db, files);

        var result = await controller.Apply(vm);

        Assert.IsType<ViewResult>(result);
        Assert.Equal("نوع الملف غير مدعوم.", Assert.Single(controller.ModelState[nameof(vm.CvFile)]!.Errors).ErrorMessage);
        Assert.Empty(files.Uploads);
        Assert.Equal(savesBefore, saves.Count);
    }

    [Fact]
    public async Task Legacy_posting_ignores_files_and_keeps_the_citizen_row_only()
    {
        var (db, _) = CreateContext();
        var posting = new PublicJobPosting { TenantId = TenantA, Title = "إعلان قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();
        var files = new FakeCandidateDocumentClient();
        var vm = ApplyModel(posting.Id);
        vm.CvFile = Form(Pdf(), "cv.pdf");

        var result = await CreatePublicCareersController(db, files).Apply(vm);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(files.Uploads);
        Assert.Equal(1, await db.CitizenApplications.CountAsync());
        Assert.Equal(0, await db.CandidateDocuments.CountAsync());
    }

    // ---- E6: HR card and download ----

    private static async Task<(RecruitmentApplication App, CandidateDocument[] Docs)> SeedDocumentsAsync(
        HRCoreDbContext db, FakeCandidateDocumentClient files, params string?[] scanStatuses)
    {
        var fixture = await SeedWithRequiredAsync(db, "Cv,IdCopy");
        await CreatePublicCareersController(db, files).Apply(ApplyModel(fixture.Posting.Id));
        var app = await db.RecruitmentApplications.Include(a => a.Requisition).SingleAsync();
        var docs = scanStatuses.Select((status, i) =>
        {
            var doc = new CandidateDocument
            {
                TenantId = TenantA,
                RecruitmentApplicationId = app.Id,
                DocumentType = CandidateDocumentTypes.Cv,
                FileId = Guid.NewGuid(),
                OriginalFileName = $"doc{i}.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
                UploadedAt = new DateTime(2026, 10, 1, 8, i, 0, DateTimeKind.Utc)
            };
            if (status is not null)
                files.ScanStatuses[doc.FileId] = status;
            return doc;
        }).ToArray();
        db.CandidateDocuments.AddRange(docs);
        await db.SaveChangesAsync();
        return (app, docs);
    }

    [Fact]
    public async Task HR_card_offers_a_download_only_for_Clean_files()
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var (app, _) = await SeedDocumentsAsync(db, files,
            CandidateDocumentScanStatus.Clean,
            CandidateDocumentScanStatus.PendingScan,
            CandidateDocumentScanStatus.Infected,
            CandidateDocumentScanStatus.Quarantined,
            CandidateDocumentScanStatus.ScanError,
            null);

        var rows = await CreateCandidateDocumentService(db, files).GetRowsAsync(app);

        Assert.Equal([true, false, false, false, false, false], rows.Select(r => r.CanDownload));
        Assert.Equal(
            ["سليم", "قيد الفحص", "محجور — غير متاح", "محجور — غير متاح", "تعذّر الفحص — غير متاح", "غير متاح حالياً"],
            rows.Select(r => r.ScanLabel));
        Assert.All(rows, r => Assert.Equal("سيرة ذاتية", r.TypeLabel));
    }

    [Fact]
    public async Task HR_card_shows_no_link_when_File_Storage_cannot_be_asked()
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var (app, _) = await SeedDocumentsAsync(db, files, CandidateDocumentScanStatus.Clean);
        files.MetadataUnavailable = true;

        var row = Assert.Single(await CreateCandidateDocumentService(db, files).GetRowsAsync(app));

        Assert.False(row.CanDownload);
        Assert.Equal(CandidateDocumentScanStatus.UnknownLabelAr, row.ScanLabel);
    }

    [Fact]
    public async Task Index_completeness_compares_documents_with_required_types()
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var (app, _) = await SeedDocumentsAsync(db, files, CandidateDocumentScanStatus.Clean);

        var completeness = await CreateCandidateDocumentService(db, files).GetCompletenessAsync([app]);

        Assert.Equal(new CandidateDocumentCompleteness(1, 2, 1), completeness[app.Id]);
    }

    [Theory]
    [InlineData(RecruitmentIntakeChannel.Public, true)]
    [InlineData(RecruitmentIntakeChannel.Internal, false)]
    [InlineData(RecruitmentIntakeChannel.HrEntered, false)]
    [InlineData(RecruitmentIntakeChannel.Legacy, false)]
    public async Task Only_public_applications_have_required_documents_to_miss(RecruitmentIntakeChannel channel, bool missing)
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var fixture = await SeedWithRequiredAsync(db, "Cv,IdCopy");
        await CreatePublicCareersController(db, files).Apply(ApplyModel(fixture.Posting.Id));
        var app = await db.RecruitmentApplications.Include(a => a.Requisition).SingleAsync();
        app.IntakeChannel = channel;
        app.EligibilityFlags = null;
        await db.SaveChangesAsync();

        // HR list badge, Details alert (same helper) and the eligibility flag give one answer.
        var completeness = await CreateCandidateDocumentService(db, files).GetCompletenessAsync([app]);
        UnifiedIntakeService.ApplyDocumentFlag(app, app.Requisition!, []);

        Assert.Equal(missing, completeness.ContainsKey(app.Id));
        if (missing)
            Assert.Equal(new CandidateDocumentCompleteness(0, 2, 2), completeness[app.Id]);
        Assert.Equal(
            missing ? new[] { CandidateDocumentTypes.Cv, CandidateDocumentTypes.IdCopy } : Array.Empty<string>(),
            CandidateDocumentTypes.RequiredFor(app.IntakeChannel, app.Requisition!.RequiredDocumentTypes));
        Assert.Equal(missing, R.Parse(app.EligibilityFlags).Contains(R.MissingRequiredDocuments));
    }

    [Fact]
    public async Task Download_streams_a_Clean_document()
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var (_, docs) = await SeedDocumentsAsync(db, files, CandidateDocumentScanStatus.Clean);
        var controller = Hcm029TestSupport.CreateHireController(db, candidateDocuments: CreateCandidateDocumentService(db, files));

        var result = await controller.CandidateDocument(docs[0].Id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("doc0.pdf", file.FileDownloadName);
        Assert.Equal([docs[0].FileId], files.ContentRequests);
    }

    [Theory]
    [InlineData(CandidateDocumentScanStatus.PendingScan)]
    [InlineData(CandidateDocumentScanStatus.Infected)]
    [InlineData(CandidateDocumentScanStatus.Quarantined)]
    [InlineData(CandidateDocumentScanStatus.ScanError)]
    public async Task Download_refuses_non_Clean_documents_without_asking_for_content(string scanStatus)
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var (_, docs) = await SeedDocumentsAsync(db, files, scanStatus);
        var controller = Hcm029TestSupport.CreateHireController(db, candidateDocuments: CreateCandidateDocumentService(db, files));

        var result = await controller.CandidateDocument(docs[0].Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(files.ContentRequests);
    }

    [Fact]
    public async Task Download_refuses_another_tenants_document()
    {
        var (seedDb, _, connection) = CreatePortalContext();
        var files = new FakeCandidateDocumentClient();
        var (_, docs) = await SeedDocumentsAsync(seedDb, files, CandidateDocumentScanStatus.Clean);

        var otherTenant = CreateCandidateDocumentService(TenantContext(connection, TenantB), files);
        var ownTenant = CreateCandidateDocumentService(TenantContext(connection, TenantA), files);

        var (otherDoc, otherResult) = await otherTenant.OpenAsync(docs[0].Id);
        Assert.Null(otherDoc);
        Assert.IsType<CandidateFileContentResult.NotFound>(otherResult);
        Assert.Empty(files.ContentRequests);

        var (_, ownResult) = await ownTenant.OpenAsync(docs[0].Id);
        Assert.IsType<CandidateFileContentResult.Found>(ownResult);
    }

    [Fact]
    public async Task Download_reports_an_unavailable_File_Storage_on_the_details_page()
    {
        var (db, _) = CreateContext();
        var files = new FakeCandidateDocumentClient();
        var (app, docs) = await SeedDocumentsAsync(db, files, CandidateDocumentScanStatus.Clean);
        files.MetadataUnavailable = true;
        var controller = Hcm029TestSupport.CreateHireController(db, candidateDocuments: CreateCandidateDocumentService(db, files));

        var result = await controller.CandidateDocument(docs[0].Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(app.Id, redirect.RouteValues!["id"]);
        Assert.Equal(CandidateDocumentService.DownloadUnavailableMessage, controller.TempData["Error"]);
    }

    // ---- E2: HTTP client ----

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static CandidateDocumentUploadClient CreateClient(HttpMessageHandler handler, IConfiguration? configuration = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(CandidateDocumentUploadClient.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://file-storage.test/") });
        return new CandidateDocumentUploadClient(
            factory.Object,
            configuration ?? new ConfigurationBuilder().Build(),
            NullLogger<CandidateDocumentUploadClient>.Instance);
    }

    private static readonly PreparedCandidateFile CvFile = new(CandidateDocumentTypes.Cv, "cv.pdf", "application/pdf", Pdf());

    [Fact]
    public async Task Client_posts_the_file_with_the_tenant_header_profile_and_caller_proof()
    {
        var fileId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                $$"""{"fileId":"{{fileId}}","originalName":"cv.pdf","size":64,"mimeType":"application/pdf"}""",
                Encoding.UTF8, "application/json")
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GatewayCallerProof:Secret"] = "unit-test-secret" })
            .Build();

        var result = await CreateClient(handler, config).UploadAsync(TenantA, CvFile);

        Assert.Equal(new CandidateDocumentUploadResult.Uploaded(fileId, "application/pdf", 64), result);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://file-storage.test/api/files/service-upload", request.RequestUri!.ToString());
        Assert.Equal(TenantA.ToString("D"), request.Headers.GetValues(TenantIdMiddleware.HeaderName).Single());
        Assert.Equal("/api/files/service-upload", request.Headers.GetValues(GatewayCallerProof.PathHeader).Single());
        Assert.True(request.Headers.Contains(GatewayCallerProof.ProofHeader));
        Assert.Contains("name=constraintProfile", body);
        Assert.Contains("CandidateDocument", body);
        Assert.Contains("name=file; filename=cv.pdf", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Client_maps_File_Storage_errors_to_a_failure(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("English detail") });

        var result = await CreateClient(handler).UploadAsync(TenantA, CvFile);

        Assert.Equal(new CandidateDocumentUploadResult.Failed($"HTTP {(int)status}"), result);
    }

    [Fact]
    public async Task Client_maps_transport_errors_to_a_failure()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));

        Assert.IsType<CandidateDocumentUploadResult.Failed>(await CreateClient(handler).UploadAsync(TenantA, CvFile));
        Assert.Null(await CreateClient(handler).GetMetadataAsync(TenantA, [Guid.NewGuid()]));
        Assert.IsType<CandidateFileContentResult.Unavailable>(await CreateClient(handler).GetContentAsync(TenantA, Guid.NewGuid()));
    }

    [Fact]
    public async Task Client_reads_metadata_and_content()
    {
        var fileId = Guid.NewGuid();
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""[{"fileId":"{{fileId}}","originalName":"cv.pdf","mimeType":"application/pdf","size":70,"scanStatus":"Clean","status":"Pending","uploadedAt":"2026-10-01T08:00:00Z"}]""",
                    Encoding.UTF8, "application/json")
            }
            : request.RequestUri!.AbsolutePath.Contains(fileId.ToString("D"))
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Pdf(16)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var metadata = await client.GetMetadataAsync(TenantA, [fileId]);
        var found = await client.GetContentAsync(TenantA, fileId);
        var missing = await client.GetContentAsync(TenantA, Guid.NewGuid());

        Assert.Equal(CandidateDocumentScanStatus.Clean, metadata![fileId].ScanStatus);
        Assert.Equal(70, metadata[fileId].Size);
        Assert.Equal(16, Assert.IsType<CandidateFileContentResult.Found>(found).Content.Length);
        Assert.IsType<CandidateFileContentResult.NotFound>(missing);
        Assert.Equal("/api/files/service-upload/metadata", handler.Requests[0].Request.RequestUri!.AbsolutePath);
        Assert.Contains(fileId.ToString(), handler.Requests[0].Body);
        Assert.All(handler.Requests, r =>
            Assert.Equal(TenantA.ToString("D"), r.Request.Headers.GetValues(TenantIdMiddleware.HeaderName).Single()));
    }

    [Fact]
    public void Unconfigured_base_url_registers_the_unavailable_client()
    {
        static ICandidateDocumentUploadClient Resolve(string? baseUrl)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Integration:FileStorageBaseUrl"] = baseUrl })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddCandidateDocumentUploadClient(configuration);
            return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ICandidateDocumentUploadClient>();
        }

        Assert.IsType<UnavailableCandidateDocumentUploadClient>(Resolve(null));
        Assert.IsType<CandidateDocumentUploadClient>(Resolve("http://file-storage-web:8080"));
    }
}
