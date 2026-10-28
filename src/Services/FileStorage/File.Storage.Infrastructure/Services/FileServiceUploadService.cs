using System.Security.Claims;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using FileVisibility = HRManagement.SharedKernel.Files.FileVisibility;

namespace File.Storage.Infrastructure.Services;

/// <summary>
/// PLAN-30 (FS-030): files a trusted service uploads for someone who cannot call File.Storage itself
/// (anonymous candidates, relayed by HR.Core). The upload takes the normal quarantine + scan path; the
/// caller's <c>Service</c> grant is what lets it read the file back, and content only once it is Clean.
/// </summary>
public sealed class FileServiceUploadService(
    FileStorageDbContext db,
    FilePlatformService filePlatform,
    FileAccessGrantService grantService)
{
    /// <summary>Grant holder type of a service upload; the holder id is the caller's client id.</summary>
    public const string ServiceHolderType = "Service";

    /// <summary>The only constraint profile a service may request.</summary>
    public const string CandidateDocumentProfile = "CandidateDocument";

    /// <summary>
    /// Body cap of the upload endpoint (5_308_416 bytes): the CandidateDocument maximum (5 MiB) plus 64 KiB of
    /// multipart framing. The exact file size is still checked by <see cref="FileUploadHelper.Validate"/>.
    /// </summary>
    public const long MaxRequestBodyBytes = 5 * 1024 * 1024 + 64 * 1024;

    public const string MissingTenantMessage = "تعذر تحديد المستأجر — رأس X-Tenant-ID مطلوب.";
    public const string UnsupportedProfileMessage = "ملف قيود الرفع غير مدعوم (المسموح: CandidateDocument).";

    // FileRecord.OriginalName is varchar(256): a longer name would fail the insert after the object is stored.
    private const int MaxOriginalNameLength = 256;

    public static FileUploadConstraints? ResolveConstraintProfile(string? constraintProfile) =>
        string.Equals(constraintProfile?.Trim(), CandidateDocumentProfile, StringComparison.OrdinalIgnoreCase)
            ? FileUploadConstraints.CandidateDocument
            : null;

    /// <summary>
    /// Validates and stores the file (quarantine bucket, scan queue) and creates the caller's Service grant.
    /// Every refusal returns an Arabic message before anything reaches storage.
    /// </summary>
    public async Task<(FileRecord? Record, string? Error)> UploadAsync(
        ClaimsPrincipal caller,
        IFormFile? file,
        string? constraintProfile,
        CancellationToken cancellationToken = default)
    {
        if (db.CurrentTenantId is null)
            return (null, MissingTenantMessage);

        var clientId = GetServiceClientId(caller);
        if (clientId is null)
            return (null, "رفع الخدمات متاح لحسابات الخدمة فقط.");

        var constraints = ResolveConstraintProfile(constraintProfile);
        if (constraints is null)
            return (null, UnsupportedProfileMessage);

        if (file is null || file.Length == 0)
            return (null, "الملف فارغ.");

        if (file.FileName.Length > MaxOriginalNameLength)
            return (null, "اسم الملف طويل جداً.");

        var uploadedBy = ParseSubject(caller);
        FileRecord record;
        try
        {
            record = await filePlatform.UploadAsync(file, constraints, uploadedBy, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return (null, SafeClientError.ForUser(ex));
        }

        // Module visibility for hr-core (the only client the policy admits). RegisterReference never adds a
        // Module grant later because this one already exists (FileAccessGrantService returns it).
        await grantService.CreateGrantAsync(
            record.Id,
            fileReference: $"{record.Bucket}/{record.ObjectKey}",
            module: HrFileServices.HrCore,
            holderType: ServiceHolderType,
            holderId: clientId,
            visibility: FileVisibility.Module,
            granteeId: uploadedBy?.ToString(),
            cancellationToken: cancellationToken);

        return (record, null);
    }

    /// <summary>
    /// The caller's own service uploads in the request tenant: a non-revoked Service grant held by its client id.
    /// The grant's expiry is ignored (it limits user downloads; this grant records the owning service).
    /// Unknown, foreign, other-tenant and deleted ids are left out. At most 50 ids are read.
    /// </summary>
    public async Task<IReadOnlyList<FileRecord>> GetOwnedFilesAsync(
        ClaimsPrincipal caller,
        IEnumerable<Guid> fileIds,
        CancellationToken cancellationToken = default)
    {
        var clientId = GetServiceClientId(caller);
        var ids = fileIds.Distinct().Take(SearchFilterBase.LookupMaxPageSize).ToList();
        if (db.CurrentTenantId is not { } tenantId || clientId is null || ids.Count == 0)
            return [];

        var ownedIds = await db.FileAccessGrants.AsNoTracking()
            .Where(g => ids.Contains(g.FileId)
                && g.TenantId == tenantId
                && !g.IsRevoked
                && g.HolderType == ServiceHolderType
                && g.HolderId == clientId)
            .Select(g => g.FileId)
            .ToListAsync(cancellationToken);
        if (ownedIds.Count == 0)
            return [];

        return await db.Files.AsNoTracking()
            .Where(f => ownedIds.Contains(f.Id) && f.TenantId == tenantId && !f.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Content of one of the caller's service uploads once its scan verdict is Clean; otherwise null.</summary>
    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenCleanContentAsync(
        ClaimsPrincipal caller,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var owned = await GetOwnedFilesAsync(caller, [fileId], cancellationToken);
        if (owned.Count == 0 || owned[0].ScanStatus != FileScanStatus.Clean)
            return null;

        // Re-checks Clean and verifies the stored SHA-256 (InvalidOperationException on mismatch).
        return await filePlatform.OpenDownloadVerifiedAsync(fileId, cancellationToken);
    }

    private static string? GetServiceClientId(ClaimsPrincipal caller)
    {
        if (!ServiceCallerPrincipal.IsServiceAccount(caller))
            return null;

        var clientId = ServiceCallerPrincipal.GetClientId(caller);
        return string.IsNullOrWhiteSpace(clientId) ? null : clientId;
    }

    private static Guid? ParseSubject(ClaimsPrincipal caller)
    {
        var value = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
