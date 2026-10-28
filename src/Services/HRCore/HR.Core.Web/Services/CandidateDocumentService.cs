using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

/// <summary>HCM-030 (E6): one row of the «المستندات» card.</summary>
public sealed record CandidateDocumentRow(
    Guid Id,
    string DocumentType,
    string TypeLabel,
    string FileName,
    long SizeBytes,
    DateTime UploadedAt,
    string? ScanStatus,
    string ScanLabel,
    bool CanDownload);

/// <summary>HCM-030 (E6): document completeness of an application on the HR list.</summary>
public sealed record CandidateDocumentCompleteness(int DocumentCount, int RequiredCount, int MissingCount);

/// <summary>
/// HCM-030 (E3/E5/E6): candidate documents — relaying the files of a public application to File.Storage, staging the
/// rows and their file references (no save), and the HR card and download. HR users read the files through HR.Core's
/// service identity: the gateway files route needs <c>ModuleFileStorage</c>, which ordinary HR users do not hold.
/// </summary>
public sealed class CandidateDocumentService(
    HRCoreDbContext db,
    ICandidateDocumentUploadClient files,
    IFileReferenceOutbox fileReferenceOutbox)
{
    public const string UploadFailedMessage = "تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.";
    public const string DownloadUnavailableMessage = "تعذّر تنزيل المستند حالياً، يرجى المحاولة لاحقاً.";

    /// <summary>
    /// Relays every file to File.Storage; null as soon as one fails (files already stored stay unreferenced
    /// <c>Pending</c> in File.Storage).
    /// </summary>
    public async Task<IReadOnlyList<(PreparedCandidateFile File, CandidateDocumentUploadResult.Uploaded Upload)>?> UploadAllAsync(
        Guid tenantId,
        IReadOnlyList<PreparedCandidateFile> prepared,
        CancellationToken ct = default)
    {
        var uploaded = new List<(PreparedCandidateFile, CandidateDocumentUploadResult.Uploaded)>();
        foreach (var file in prepared)
        {
            if (await files.UploadAsync(tenantId, file, ct) is not CandidateDocumentUploadResult.Uploaded result)
                return null;
            uploaded.Add((file, result));
        }
        return uploaded;
    }

    /// <summary>
    /// Stages (no save) the document rows and their File.Storage reference registrations. The anonymous portal has no
    /// ambient tenant, so every row — the outbox rows included — carries <paramref name="tenantId"/> explicitly.
    /// </summary>
    public IReadOnlyList<CandidateDocument> Stage(
        Guid tenantId,
        Guid recruitmentApplicationId,
        Guid? citizenApplicationId,
        IEnumerable<(PreparedCandidateFile File, CandidateDocumentUploadResult.Uploaded Upload)> uploads,
        string uploadedBy)
    {
        var staged = new List<CandidateDocument>();
        foreach (var (file, upload) in uploads)
        {
            var document = new CandidateDocument
            {
                TenantId = tenantId,
                RecruitmentApplicationId = recruitmentApplicationId,
                CitizenApplicationId = citizenApplicationId,
                DocumentType = file.DocumentType,
                FileId = upload.FileId,
                OriginalFileName = file.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.Size,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = uploadedBy
            };
            db.CandidateDocuments.Add(document);
            fileReferenceOutbox.EnqueueRegister(document.FileId, HolderKey(document));
            staged.Add(document);
        }

        // EfFileReferenceOutbox stamps the ambient tenant (none on the portal → Guid.Empty, refused by the stamp
        // interceptor); the rows belong to the posting's tenant.
        var fileIds = staged.Select(d => d.FileId).ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<FileReferenceOutboxMessage>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty && fileIds.Contains(entry.Entity.FileId))
                entry.Entity.TenantId = tenantId;
        }
        return staged;
    }

    public static string HolderKey(CandidateDocument document) =>
        FileReferenceClientExtensions.BuildHolderKey(
            HrFileServices.HrCore, nameof(CandidateDocument), document.Id, nameof(CandidateDocument.FileId));

    /// <summary>The card rows; scan states come from File.Storage (unknown, so no link, when it cannot be asked).</summary>
    public async Task<IReadOnlyList<CandidateDocumentRow>> GetRowsAsync(RecruitmentApplication app, CancellationToken ct = default)
    {
        var documents = await db.CandidateDocuments.AsNoTracking()
            .Where(d => d.RecruitmentApplicationId == app.Id)
            .OrderBy(d => d.UploadedAt)
            .ToListAsync(ct);
        if (documents.Count == 0)
            return [];

        var metadata = await files.GetMetadataAsync(app.TenantId, documents.Select(d => d.FileId).ToList(), ct);
        return documents.Select(d =>
        {
            CandidateFileMetadata? meta = null;
            metadata?.TryGetValue(d.FileId, out meta);
            return new CandidateDocumentRow(
                d.Id,
                d.DocumentType,
                CandidateDocumentTypes.LabelAr(d.DocumentType),
                d.OriginalFileName,
                meta is { Size: > 0 } ? meta.Size : d.SizeBytes,
                d.UploadedAt,
                meta?.ScanStatus,
                CandidateDocumentScanStatus.LabelAr(meta?.ScanStatus),
                CandidateDocumentScanStatus.CanDownload(meta?.ScanStatus));
        }).ToList();
    }

    /// <summary>Completeness per application of an HR list page (one grouped query).</summary>
    public async Task<IReadOnlyDictionary<Guid, CandidateDocumentCompleteness>> GetCompletenessAsync(
        IReadOnlyList<RecruitmentApplication> applications,
        CancellationToken ct = default)
    {
        var ids = applications.Select(a => a.Id).ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, CandidateDocumentCompleteness>();

        var types = (await db.CandidateDocuments.AsNoTracking()
                .Where(d => ids.Contains(d.RecruitmentApplicationId))
                .Select(d => new { d.RecruitmentApplicationId, d.DocumentType })
                .ToListAsync(ct))
            .ToLookup(d => d.RecruitmentApplicationId, d => d.DocumentType);

        var result = new Dictionary<Guid, CandidateDocumentCompleteness>();
        foreach (var app in applications)
        {
            var provided = types[app.Id].ToList();
            // Same rule as the MissingRequiredDocuments flag: only public applications have required documents.
            var required = CandidateDocumentTypes.RequiredFor(app.IntakeChannel, app.Requisition?.RequiredDocumentTypes);
            if (provided.Count == 0 && required.Count == 0)
                continue;
            result[app.Id] = new CandidateDocumentCompleteness(
                provided.Count, required.Count, CandidateDocumentTypes.Missing(required, provided).Count);
        }
        return result;
    }

    /// <summary>
    /// The bytes of a Clean document of the caller's tenant (tenant query filter, checked again here). Non-Clean files
    /// are refused before File.Storage is asked for content (it refuses them too).
    /// </summary>
    public async Task<(CandidateDocument? Document, CandidateFileContentResult Result)> OpenAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await db.CandidateDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null || (!db.BypassTenantFilter && document.TenantId != db.EffectiveTenantId))
            return (null, new CandidateFileContentResult.NotFound());

        var metadata = await files.GetMetadataAsync(document.TenantId, [document.FileId], ct);
        if (metadata is null)
            return (document, new CandidateFileContentResult.Unavailable("Metadata unavailable"));
        if (!metadata.TryGetValue(document.FileId, out var meta) || !CandidateDocumentScanStatus.CanDownload(meta.ScanStatus))
            return (document, new CandidateFileContentResult.NotFound());

        return (document, await files.GetContentAsync(document.TenantId, document.FileId, ct));
    }
}
