using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FileVisibility = HRManagement.SharedKernel.Files.FileVisibility;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-30 (FS-030): File.Storage service upload — the S2S policy (client + scope), the CandidateDocument
/// constraint enforced inside File.Storage, and the read-back limited to the caller's own Clean uploads.
/// </summary>
public class Fs030ServiceUploadPolicyTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000f030");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-4000-8000-00000000f030");
    private static readonly Guid HrCoreSubject = Guid.Parse("cccccccc-0000-4000-8000-00000000f030");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FileStorageDbContext> _options;
    private readonly InMemoryStorage _storage = new();

    public Fs030ServiceUploadPolicyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<FileStorageDbContext>().UseSqlite(_connection).Options;
        using var db = new FileStorageDbContext(_options, new FixedTenant(TenantA));
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    // --- Policy: Service.FileStorageServiceUpload ---

    [Fact]
    public async Task FileStorageServiceUpload_HrCoreWithScope_Succeeds()
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload, ServiceApiAudiences.FileStorage);
        Assert.True((await auth.AuthorizeAsync(svc, ServicePolicies.FileStorageServiceUpload)).Succeeded);
    }

    [Theory]
    [InlineData(ServiceClientIds.Payroll)]
    [InlineData(ServiceClientIds.HrContracts)]
    [InlineData(ServiceClientIds.Compliance)]
    [InlineData(ServiceClientIds.FileStorage)]
    public async Task FileStorageServiceUpload_OtherClientWithScope_Fails(string clientId)
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(clientId, ServiceScopes.FileStorageServiceUpload, ServiceApiAudiences.FileStorage);
        Assert.False((await auth.AuthorizeAsync(svc, ServicePolicies.FileStorageServiceUpload)).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ServiceScopes.FileStorageReferences)]
    [InlineData("file-storage.references payroll.grades.read")]
    public async Task FileStorageServiceUpload_HrCoreWithoutScope_Fails(string? scope)
    {
        var auth = BuildAuthService();
        var svc = ServiceAccount(ServiceClientIds.HrCore, scope, ServiceApiAudiences.FileStorage);
        Assert.False((await auth.AuthorizeAsync(svc, ServicePolicies.FileStorageServiceUpload)).Succeeded);
    }

    [Fact]
    public async Task FileStorageServiceUpload_InteractiveUserOrAnonymous_Fails()
    {
        var auth = BuildAuthService();
        // Even a user token that carried the scope claim is not a service account.
        var user = InteractiveUser(
            scope: "openid " + ServiceScopes.FileStorageServiceUpload,
            HrManagementRoles.Admin, HrManagementRoles.FileStorage, HrManagementRoles.HrCore);
        var anon = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False((await auth.AuthorizeAsync(user, ServicePolicies.FileStorageServiceUpload)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(anon, ServicePolicies.FileStorageServiceUpload)).Succeeded);
    }

    [Fact]
    public async Task FileStorageServiceUpload_ScopeGrantsNothingOnFilesApi_AndExistingPoliciesUnchanged()
    {
        var auth = BuildAuthService();
        var uploadOnly = ServiceAccount(ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload);
        var referencesOnly = ServiceAccount(ServiceClientIds.HrCore, ServiceScopes.FileStorageReferences);

        // FilesApiController's class policy still needs file-storage.references.
        Assert.False((await auth.AuthorizeAsync(uploadOnly, HrManagementPolicies.ModuleFileStorage)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(uploadOnly, ServicePolicies.FileStorageReferences)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(referencesOnly, HrManagementPolicies.ModuleFileStorage)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(referencesOnly, ServicePolicies.FileStorageReferences)).Succeeded);
    }

    // --- Constraint profile (enforced in File.Storage) ---

    [Fact]
    public void CandidateDocument_profile_is_5_MiB_pdf_jpeg_png_with_small_request_headroom()
    {
        var c = FileUploadConstraints.CandidateDocument;

        Assert.Equal(5_242_880L, c.MaxBytes);
        Assert.Equal(new[] { ".jpeg", ".jpg", ".pdf", ".png" }, c.AllowedExtensions.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "application/pdf", "image/jpeg", "image/png" },
            c.AllowedContentTypes!.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(5_308_416L, FileServiceUploadService.MaxRequestBodyBytes);
    }

    [Theory]
    [InlineData("CandidateDocument")]
    [InlineData("candidatedocument")]
    [InlineData(" CandidateDocument ")]
    public void ResolveConstraintProfile_accepts_CandidateDocument(string profile) =>
        Assert.Same(FileUploadConstraints.CandidateDocument, FileServiceUploadService.ResolveConstraintProfile(profile));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Document")]
    [InlineData("Certificate")]
    [InlineData("Photo")]
    [InlineData("candidate-document")]
    public async Task Upload_with_another_profile_is_rejected_before_storage(string? profile)
    {
        using var h = Create(TenantA);

        var (record, error) = await h.Service.UploadAsync(HrCore(), PdfForm(), profile);

        Assert.Null(record);
        Assert.Equal(FileServiceUploadService.UnsupportedProfileMessage, error);
        await AssertNothingStoredAsync(h.Db);
    }

    [Theory]
    [InlineData("cv.pdf", "application/pdf")]
    [InlineData("id.jpg", "image/jpeg")]
    [InlineData("id.jpeg", "image/jpeg")]
    [InlineData("certificate.png", "image/png")]
    public async Task Upload_accepts_pdf_jpeg_and_png(string fileName, string contentType)
    {
        using var h = Create(TenantA);

        var (record, error) = await h.Service.UploadAsync(HrCore(), Form(SampleBytes(contentType), fileName, contentType), "CandidateDocument");

        Assert.Null(error);
        Assert.NotNull(record);
        Assert.Equal(contentType, record!.MimeType);
    }

    [Fact]
    public async Task Upload_goes_to_quarantine_and_scan_queue_with_the_service_grant()
    {
        using var h = Create(TenantA);

        var (record, error) = await h.Service.UploadAsync(HrCore(), PdfForm("cv.pdf"), "CandidateDocument");

        Assert.Null(error);
        Assert.NotNull(record);
        Assert.Equal(FileStorageBuckets.Quarantine, record!.Bucket);
        Assert.Equal(FileRecordStatus.Pending, record.Status);
        Assert.Equal(FileScanStatus.PendingScan, record.ScanStatus);
        Assert.Equal(TenantA, record.TenantId);
        Assert.Equal(HrCoreSubject, record.UploadedBy);
        Assert.True(await h.Db.FileScanWorkItems.AnyAsync(w => w.FileId == record.Id && w.Status == FileScanWorkItemStatus.Pending));

        var grant = await h.Db.FileAccessGrants.SingleAsync(g => g.FileId == record.Id);
        Assert.Equal(FileServiceUploadService.ServiceHolderType, grant.HolderType);
        Assert.Equal(ServiceClientIds.HrCore, grant.HolderId);
        Assert.Equal(HrFileServices.HrCore, grant.Module);
        Assert.Equal(FileVisibility.Module, grant.Visibility);
        Assert.Equal(HrCoreSubject.ToString(), grant.GranteeId);
        Assert.Equal(TenantA, grant.TenantId);
        Assert.False(grant.IsRevoked);
    }

    [Fact]
    public async Task Upload_of_an_executable_is_rejected()
    {
        using var h = Create(TenantA);

        var (record, error) = await h.Service.UploadAsync(
            HrCore(), Form(ExecutableBytes(), "setup.exe", "application/x-msdownload"), "CandidateDocument");

        Assert.Null(record);
        Assert.Equal("نوع الملف غير مدعوم.", error);
        await AssertNothingStoredAsync(h.Db);
    }

    [Fact]
    public async Task Upload_of_a_docx_is_rejected_although_the_user_Document_profile_allows_it()
    {
        using var h = Create(TenantA);
        var docx = Form(
            [(byte)'P', (byte)'K', 0x03, 0x04, 0x14, 0x00, 0x06, 0x00],
            "cv.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var (record, error) = await h.Service.UploadAsync(HrCore(), docx, "CandidateDocument");

        Assert.Null(record);
        Assert.Equal("نوع الملف غير مدعوم.", error);
        await AssertNothingStoredAsync(h.Db);
    }

    [Fact]
    public async Task Upload_over_5_MiB_is_rejected_and_exactly_5_MiB_is_accepted()
    {
        using var h = Create(TenantA);

        var (tooLarge, error) = await h.Service.UploadAsync(
            HrCore(), Form(PdfOfSize(5 * 1024 * 1024 + 1), "big.pdf", "application/pdf"), "CandidateDocument");
        Assert.Null(tooLarge);
        Assert.StartsWith("حجم الملف يجب ألا يتجاوز", error);
        Assert.Contains("ميغابايت", error);
        await AssertNothingStoredAsync(h.Db);

        var (atLimit, limitError) = await h.Service.UploadAsync(
            HrCore(), Form(PdfOfSize(5 * 1024 * 1024), "max.pdf", "application/pdf"), "CandidateDocument");
        Assert.Null(limitError);
        Assert.Equal(5L * 1024 * 1024, atLimit!.Size);
    }

    [Theory]
    [InlineData("png-as-pdf")]
    [InlineData("exe-as-pdf")]
    public async Task Upload_with_spoofed_magic_bytes_is_rejected(string kind)
    {
        using var h = Create(TenantA);
        var bytes = kind == "png-as-pdf" ? SampleBytes("image/png") : ExecutableBytes();

        var (record, error) = await h.Service.UploadAsync(HrCore(), Form(bytes, "cv.pdf", "application/pdf"), "CandidateDocument");

        Assert.Null(record);
        Assert.Equal("تعارض بين امتداد الملف ومحتواه (magic bytes).", error);
        await AssertNothingStoredAsync(h.Db);
    }

    [Fact]
    public async Task Upload_without_tenant_is_refused_before_storage()
    {
        using var h = Create(tenantId: null);

        var (record, error) = await h.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");

        Assert.Null(record);
        Assert.Equal(FileServiceUploadService.MissingTenantMessage, error);
        Assert.Empty(_storage.Keys);
    }

    [Fact]
    public async Task Upload_by_an_interactive_user_or_without_file_or_with_an_overlong_name_is_refused()
    {
        using var h = Create(TenantA);

        var (byUser, userError) = await h.Service.UploadAsync(InteractiveUser(scope: null, HrManagementRoles.Admin), PdfForm(), "CandidateDocument");
        var (noFile, noFileError) = await h.Service.UploadAsync(HrCore(), null, "CandidateDocument");
        var (longName, longNameError) = await h.Service.UploadAsync(HrCore(), PdfForm(new string('a', 300) + ".pdf"), "CandidateDocument");

        Assert.Null(byUser);
        Assert.Equal("رفع الخدمات متاح لحسابات الخدمة فقط.", userError);
        Assert.Null(noFile);
        Assert.Equal("الملف فارغ.", noFileError);
        Assert.Null(longName);
        Assert.Equal("اسم الملف طويل جداً.", longNameError);
        await AssertNothingStoredAsync(h.Db);
    }

    // --- Read-back: metadata and Clean content ---

    [Fact]
    public async Task Content_is_served_only_once_the_scan_marks_the_file_Clean()
    {
        using var h = Create(TenantA);
        var pdf = MinimalPdf();
        var (record, _) = await h.Service.UploadAsync(HrCore(), Form(pdf, "cv.pdf", "application/pdf"), "CandidateDocument");

        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), record!.Id));

        await RunScanQueueAsync(h);
        var content = await h.Service.OpenCleanContentAsync(HrCore(), record.Id);

        Assert.NotNull(content);
        await using var stream = content.Value.Stream;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(pdf, copy.ToArray());
        Assert.Equal("application/pdf", content.Value.ContentType);
        Assert.Equal("cv.pdf", content.Value.FileName);
    }

    [Fact]
    public async Task Content_of_infected_or_quarantined_uploads_is_refused_after_a_real_scan()
    {
        using var h = Create(TenantA);
        var eicarPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n" + EicarAwareScanner.EicarSignature + "\n%%EOF\n");
        var activePdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</OpenAction<</S/JavaScript/JS(app.alert(1))>>>>endobj\n%%EOF\n");
        var (infected, _) = await h.Service.UploadAsync(HrCore(), Form(eicarPdf, "a.pdf", "application/pdf"), "CandidateDocument");
        var (quarantined, _) = await h.Service.UploadAsync(HrCore(), Form(activePdf, "b.pdf", "application/pdf"), "CandidateDocument");

        await RunScanQueueAsync(h);

        var metadata = await h.Service.GetOwnedFilesAsync(HrCore(), [infected!.Id, quarantined!.Id]);
        Assert.Equal(FileScanStatus.Infected, metadata.Single(f => f.Id == infected.Id).ScanStatus);
        Assert.Equal(FileScanStatus.Quarantined, metadata.Single(f => f.Id == quarantined.Id).ScanStatus);
        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), infected.Id));
        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), quarantined.Id));
    }

    [Theory]
    [InlineData(FileScanStatus.PendingScan)]
    [InlineData(FileScanStatus.Infected)]
    [InlineData(FileScanStatus.Quarantined)]
    [InlineData(FileScanStatus.ScanError)]
    public async Task Content_of_a_non_Clean_upload_is_refused(string scanStatus)
    {
        using var h = Create(TenantA);
        var (record, _) = await h.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
        var row = await h.Db.Files.SingleAsync(f => f.Id == record!.Id);
        row.ScanStatus = scanStatus;
        await h.Db.SaveChangesAsync();

        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), record!.Id));
    }

    [Fact]
    public async Task Content_ignores_the_grant_expiry_of_the_service_grant()
    {
        using var h = Create(TenantA);
        var (record, _) = await h.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
        await RunScanQueueAsync(h);
        var grant = await h.Db.FileAccessGrants.SingleAsync(g => g.FileId == record!.Id);
        grant.ExpiresAtUtc = DateTime.UtcNow.AddDays(-30);
        await h.Db.SaveChangesAsync();

        var content = await h.Service.OpenCleanContentAsync(HrCore(), record!.Id);

        Assert.NotNull(content);
        await content.Value.Stream.DisposeAsync();
    }

    [Fact]
    public async Task Content_and_metadata_refuse_another_clients_uploads()
    {
        using var h = Create(TenantA);
        var (hrCoreFile, _) = await h.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
        // Called past the policy on purpose: ownership is the client id on the Service grant.
        var (contractsFile, _) = await h.Service.UploadAsync(
            ServiceAccount(ServiceClientIds.HrContracts, ServiceScopes.FileStorageServiceUpload), PdfForm(), "CandidateDocument");
        await RunScanQueueAsync(h);
        var contracts = ServiceAccount(ServiceClientIds.HrContracts, ServiceScopes.FileStorageServiceUpload);

        Assert.Null(await h.Service.OpenCleanContentAsync(contracts, hrCoreFile!.Id));
        Assert.Empty(await h.Service.GetOwnedFilesAsync(contracts, [hrCoreFile.Id]));
        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), contractsFile!.Id));
        Assert.Empty(await h.Service.GetOwnedFilesAsync(HrCore(), [contractsFile.Id]));
    }

    [Fact]
    public async Task Content_and_metadata_refuse_files_uploaded_through_the_user_endpoint()
    {
        using var h = Create(TenantA);
        // What FilesApiController.Upload does: same pipeline, private Uploader grant.
        var userFile = await h.Platform.UploadAsync(PdfForm(), FileUploadConstraints.Document, HrCoreSubject);
        await h.Grants.CreateGrantAsync(
            userFile.Id, $"{userFile.Bucket}/{userFile.ObjectKey}", "File.Storage", "Uploader",
            HrCoreSubject.ToString(), FileVisibility.Private, HrCoreSubject.ToString());
        await RunScanQueueAsync(h);

        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), userFile.Id));
        Assert.Empty(await h.Service.GetOwnedFilesAsync(HrCore(), [userFile.Id]));
    }

    [Fact]
    public async Task Content_and_metadata_refuse_files_of_another_tenant()
    {
        Guid fileId;
        using (var a = Create(TenantA))
        {
            var (record, _) = await a.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
            await RunScanQueueAsync(a);
            fileId = record!.Id;
            Assert.NotNull(await a.Service.OpenCleanContentAsync(HrCore(), fileId));
        }

        using var b = Create(TenantB);
        Assert.Null(await b.Service.OpenCleanContentAsync(HrCore(), fileId));
        Assert.Empty(await b.Service.GetOwnedFilesAsync(HrCore(), [fileId]));
    }

    [Fact]
    public async Task Content_and_metadata_refuse_revoked_grants_and_deleted_files()
    {
        using var h = Create(TenantA);
        var (released, _) = await h.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
        var (deleted, _) = await h.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
        await RunScanQueueAsync(h);
        await h.Grants.RevokeGrantsForFileAsync(released!.Id);
        var deletedRow = await h.Db.Files.SingleAsync(f => f.Id == deleted!.Id);
        deletedRow.IsDeleted = true;
        await h.Db.SaveChangesAsync();

        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), released.Id));
        Assert.Null(await h.Service.OpenCleanContentAsync(HrCore(), deleted!.Id));
        Assert.Empty(await h.Service.GetOwnedFilesAsync(HrCore(), [released.Id, deleted.Id]));
    }

    [Fact]
    public async Task Metadata_returns_the_callers_uploads_with_scan_status_and_omits_the_rest()
    {
        using var h = Create(TenantA);
        var (cv, _) = await h.Service.UploadAsync(HrCore(), PdfForm("cv.pdf"), "CandidateDocument");
        var (photo, _) = await h.Service.UploadAsync(
            HrCore(), Form(SampleBytes("image/png"), "id.png", "image/png"), "CandidateDocument");
        var unknown = Guid.NewGuid();

        var files = await h.Service.GetOwnedFilesAsync(HrCore(), [cv!.Id, photo!.Id, cv.Id, unknown]);

        Assert.Equal(2, files.Count);
        var cvRow = files.Single(f => f.Id == cv.Id);
        Assert.Equal("cv.pdf", cvRow.OriginalName);
        Assert.Equal("application/pdf", cvRow.MimeType);
        Assert.Equal((long)MinimalPdf().Length, cvRow.Size);
        Assert.Equal(FileScanStatus.PendingScan, cvRow.ScanStatus);
        Assert.Equal("image/png", files.Single(f => f.Id == photo.Id).MimeType);
    }

    [Fact]
    public async Task Metadata_and_content_need_a_tenant()
    {
        Guid fileId;
        using (var a = Create(TenantA))
        {
            var (record, _) = await a.Service.UploadAsync(HrCore(), PdfForm(), "CandidateDocument");
            await RunScanQueueAsync(a);
            fileId = record!.Id;
        }

        using var none = Create(tenantId: null);
        Assert.Empty(await none.Service.GetOwnedFilesAsync(HrCore(), [fileId]));
        Assert.Null(await none.Service.OpenCleanContentAsync(HrCore(), fileId));
    }

    // --- Helpers (principals mirror ServiceToServiceNegativeMatrixTests) ---

    private static IAuthorizationService BuildAuthService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal HrCore() =>
        ServiceAccount(ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload, ServiceApiAudiences.FileStorage, HrCoreSubject);

    private static ClaimsPrincipal ServiceAccount(string clientId, string? scope, string? audience = null, Guid? subject = null)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", $"service-account-{clientId}"),
            new("azp", clientId),
            new(ClaimTypes.NameIdentifier, (subject ?? Guid.NewGuid()).ToString("D"))
        };
        if (!string.IsNullOrWhiteSpace(scope))
            claims.Add(new Claim("scope", scope));
        if (!string.IsNullOrWhiteSpace(audience))
            claims.Add(new Claim("aud", audience));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static ClaimsPrincipal InteractiveUser(string? scope, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")))
            .Append(new Claim("preferred_username", "alice"))
            .Append(new Claim("azp", ServiceClientIds.Gateway))
            .ToList();
        if (!string.IsNullOrWhiteSpace(scope))
            claims.Add(new Claim("scope", scope));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }

    private Harness Create(Guid? tenantId) => new(_options, _storage, tenantId);

    private async Task AssertNothingStoredAsync(FileStorageDbContext db)
    {
        Assert.Empty(_storage.Keys);
        Assert.False(await db.Files.AnyAsync());
        Assert.False(await db.FileAccessGrants.AnyAsync());
    }

    private static async Task RunScanQueueAsync(Harness h)
    {
        foreach (var item in h.Db.FileScanWorkItems)
            item.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        await h.Db.SaveChangesAsync();
        await h.Platform.ProcessScanQueueAsync();
    }

    private static IFormFile PdfForm(string fileName = "cv.pdf") => Form(MinimalPdf(), fileName, "application/pdf");

    private static IFormFile Form(byte[] bytes, string fileName, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static byte[] MinimalPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private static byte[] PdfOfSize(int size)
    {
        var bytes = new byte[size];
        Array.Fill(bytes, (byte)' ');
        Encoding.ASCII.GetBytes("%PDF-1.4\n").CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] SampleBytes(string contentType) => contentType switch
    {
        "image/png" => Convert.FromHexString("89504E470D0A1A0A0000000D49484452"),
        "image/jpeg" => Convert.FromHexString("FFD8FFE000104A46494600010100000100010000FFD9"),
        _ => MinimalPdf()
    };

    // "MZ" DOS/PE header of a Windows executable.
    private static byte[] ExecutableBytes() =>
        Convert.FromHexString("4D5A90000300000004000000FFFF0000B8000000000000004000000000000000");

    private sealed class Harness : IDisposable
    {
        public Harness(DbContextOptions<FileStorageDbContext> options, IFileStorageService storage, Guid? tenantId)
        {
            var tenant = new FixedTenant(tenantId);
            Db = new FileStorageDbContext(options, tenant);
            Platform = new FilePlatformService(
                Db, storage, new EicarAwareScanner(), new ContentDisarmService(NullLogger<ContentDisarmService>.Instance));
            Grants = new FileAccessGrantService(Db, tenant, Options.Create(new FileStorageOptions()));
            Service = new FileServiceUploadService(Db, Platform, Grants);
        }

        public FileStorageDbContext Db { get; }
        public FilePlatformService Platform { get; }
        public FileAccessGrantService Grants { get; }
        public FileServiceUploadService Service { get; }

        public void Dispose() => Db.Dispose();
    }

    private sealed class FixedTenant(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() =>
            tenantId ?? throw new InvalidOperationException("Tenant context required but not available.");
    }

    /// <summary>Object store kept in memory (nothing written to disk, so the EICAR sample stays off it).</summary>
    private sealed class InMemoryStorage : IFileStorageService
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _objects.Keys;

        public Task EnsureBucketsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FileReference> UploadAsync(
            string bucket, string prefix, Stream content, string fileName, string contentType, long contentLength,
            CancellationToken cancellationToken = default)
        {
            var fileId = Guid.NewGuid();
            return PutAsync(bucket, $"{prefix.Trim('/')}/{fileId:N}/{fileName}", fileId, content, contentType, contentLength, cancellationToken);
        }

        public Task<FileReference> UploadFormFileAsync(
            string bucket, string prefix, IFormFile file, FileUploadConstraints constraints, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<FileReference> PutAsync(
            string bucket, string objectKey, Guid fileId, Stream content, string contentType, long contentLength,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            _objects[$"{bucket}/{objectKey}"] = bytes;
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new FileReference(fileId, bucket, objectKey, contentType, bytes.LongLength, checksum);
        }

        public Task<string> GetPresignedDownloadUrlAsync(
            FileReference reference, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> GetPresignedUploadUrlAsync(
            string bucket, string objectKey, string contentType, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_objects[reference.StorageKey]));

        public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default)
        {
            _objects.Remove(reference.StorageKey);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_objects.ContainsKey(reference.StorageKey));

        public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_objects.Keys
                .Where(k => k.StartsWith(bucket + "/", StringComparison.Ordinal))
                .Select(k => k[(bucket.Length + 1)..])
                .ToList());
    }
}
