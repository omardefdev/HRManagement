using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Files;
using HRManagement.SharedKernel.Files;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileVisibility = HRManagement.SharedKernel.Files.FileVisibility;

namespace File.Storage.Web.Controllers;

[Route("api/files")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.ModuleFileStorage)]
public sealed class FilesApiController(
    FilePlatformService filePlatform,
    IFileStorageService fileStorage,
    FileStorageDbContext db,
    IFileAccessPolicy fileAccessPolicy,
    FileAccessGrantService grantService,
    ITenantContext tenantContext) : ControllerBase
{
    public sealed record FileUploadResponseDto(
        Guid FileId,
        string OriginalName,
        long Size,
        string MimeType);

    public sealed record FileMetadataDto(
        Guid FileId,
        string OriginalName,
        string Extension,
        string MimeType,
        long Size,
        string? Sha256,
        string Status,
        string ScanStatus,
        string Visibility,
        DateTime UploadedAt,
        Guid? UploadedBy,
        bool LegalHold,
        DateTime? RetainUntil,
        string? ThreatName,
        bool CdrApplied);

    public sealed record PresignedUrlDto(string Url, DateTime ExpiresAt);

    public sealed record ReferenceRequestDto(string HolderKey, string? Module = null, string? HolderType = null);

    public sealed record ReferenceCountDto(int ReferenceCount);

    public sealed record BatchMetadataRequestDto(IReadOnlyList<Guid> FileIds);

    public sealed record ForceDeleteRequestDto(string Reason, bool OverrideLiveRefs = false);

    public sealed record LegalHoldRequestDto(bool LegalHold, DateTime? RetainUntil);

    public sealed record FileStorageMetricsDto(
        int TotalFiles,
        int PendingOrphans,
        int UnreferencedActive,
        int DeletedFiles,
        int PendingPurge,
        int DeadPurgeWorkItems,
        int PendingScan,
        int InfectedCount,
        int QuarantinedCount,
        int ScanErrorCount,
        int DeadScanWorkItems);

    public sealed record OrphanScanDto(
        int DbWithoutObject,
        int ObjectWithoutDb,
        int DeletedWithObject,
        int RefCountDriftRepaired,
        int UnresolvedExceptions);

    public sealed record PurgeWorkItemDto(
        Guid Id,
        Guid FileId,
        string Bucket,
        string ObjectKey,
        int Attempt,
        DateTime NextAttemptAt,
        string? LastError,
        string Status);

    [HttpPost]
    [RequestSizeLimit(26_214_400)]
    public async Task<ActionResult<FileUploadResponseDto>> Upload(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest("الملف فارغ.");

        try
        {
            var uploadedBy = ParseUserId();
            var record = await filePlatform.UploadAsync(
                file,
                FileUploadConstraints.Document,
                uploadedBy,
                cancellationToken);

            // SEC-003: create default private grant for uploader.
            await grantService.CreateGrantAsync(
                record.Id,
                fileReference: $"{record.Bucket}/{record.ObjectKey}",
                module: "File.Storage",
                holderType: "Uploader",
                holderId: uploadedBy?.ToString() ?? "anonymous",
                visibility: FileVisibility.Private,
                granteeId: uploadedBy?.ToString(),
                cancellationToken: cancellationToken);

            await LogAccessAsync(record.Id, "upload", allowed: true, reason: "Upload grant created", cancellationToken);

            return CreatedAtAction(
                nameof(GetMetadata),
                new { fileId = record.Id },
                new FileUploadResponseDto(record.Id, record.OriginalName, record.Size, record.MimeType));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(SafeClientError.ForUser(ex));
        }
    }

    [HttpGet("{fileId:guid}")]
    public async Task<ActionResult<FileMetadataDto>> GetMetadata(Guid fileId, CancellationToken cancellationToken)
    {
        var decision = await AuthorizeAccessAsync(fileId, forDelete: false, cancellationToken);
        if (!decision.Allowed)
            return NotFound(); // SEC-003: no info leakage (403 vs missing)

        var record = await filePlatform.GetAsync(fileId, cancellationToken);
        if (record is null)
            return NotFound();

        await LogAccessAsync(fileId, "metadata", decision.Allowed, decision.Reason, cancellationToken);
        return Ok(ToMetadataDto(record));
    }

    [HttpPost("batch/metadata")]
    public async Task<ActionResult<IReadOnlyList<FileMetadataDto>>> BatchMetadata(
        [FromBody] BatchMetadataRequestDto request,
        CancellationToken cancellationToken)
    {
        var fileIds = (request.FileIds ?? []).Take(SearchFilterBase.LookupMaxPageSize).ToArray();
        var allowed = new List<FileRecord>();
        foreach (var fileId in fileIds)
        {
            var decision = await AuthorizeAccessAsync(fileId, forDelete: false, cancellationToken);
            if (!decision.Allowed)
                continue;
            var record = await filePlatform.GetAsync(fileId, cancellationToken);
            if (record is not null)
                allowed.Add(record);
        }

        return Ok(allowed.Select(ToMetadataDto).Take(SearchFilterBase.LookupMaxPageSize).ToList());
    }

    [HttpGet("{fileId:guid}/presigned-url")]
    public async Task<ActionResult<PresignedUrlDto>> PresignedUrl(
        Guid fileId,
        [FromQuery] int expiryMinutes = 5,
        CancellationToken cancellationToken = default)
    {
        var (decision, grant) = await AuthorizeAccessWithGrantAsync(fileId, forDelete: false, cancellationToken);
        if (!decision.Allowed || grant is null)
            return NotFound();

        var record = await filePlatform.GetAsync(fileId, cancellationToken);
        if (record is null)
            return NotFound();

        try
        {
            filePlatform.EnsureDownloadAllowed(record);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(SafeClientError.ForUser(ex));
        }

        // SEC-003: hard cap at grant.PresignedUrlMaxLifetimeSeconds (≤ 300s / 5 min).
        var maxSeconds = Math.Clamp(grant.PresignedUrlMaxLifetimeSeconds, 1, 300);
        var requestedSeconds = Math.Clamp(expiryMinutes, 1, 1440) * 60;
        var effectiveSeconds = Math.Min(requestedSeconds, maxSeconds);

        var reference = ToStorageReference(record);
        var url = await fileStorage.GetPresignedDownloadUrlAsync(
            reference,
            TimeSpan.FromSeconds(effectiveSeconds),
            cancellationToken);

        await LogAccessAsync(fileId, "presigned-url", decision.Allowed, decision.Reason, cancellationToken);
        return Ok(new PresignedUrlDto(url, DateTime.UtcNow.AddSeconds(effectiveSeconds)));
    }

    [HttpGet("{fileId:guid}/download")]
    public async Task<IActionResult> Download(Guid fileId, CancellationToken cancellationToken)
    {
        var decision = await AuthorizeAccessAsync(fileId, forDelete: false, cancellationToken);
        if (!decision.Allowed)
            return NotFound();

        try
        {
            var (stream, contentType, fileName) = await filePlatform.OpenDownloadVerifiedAsync(fileId, cancellationToken);
            await LogAccessAsync(fileId, "download", decision.Allowed, decision.Reason, cancellationToken);
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            await LogAccessAsync(fileId, "download-checksum-fail", false, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status409Conflict, SafeClientError.ForUser(ex));
        }
    }

    [HttpPost("{fileId:guid}/references")]
    [Authorize(Policy = ServicePolicies.FileStorageReferences)]
    [Idempotent]
    public async Task<ActionResult<ReferenceCountDto>> RegisterReference(
        Guid fileId,
        [FromBody] ReferenceRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = await filePlatform.RegisterReferenceAsync(fileId, request.HolderKey, cancellationToken);

            var record = await filePlatform.GetAsync(fileId, cancellationToken);
            if (record is not null)
            {
                var holderParts = ParseHolderKey(request.HolderKey);
                await grantService.CreateGrantAsync(
                    fileId,
                    fileReference: $"{record.Bucket}/{record.ObjectKey}",
                    module: request.Module ?? holderParts.Module,
                    holderType: request.HolderType ?? holderParts.HolderType,
                    holderId: holderParts.HolderId,
                    visibility: FileVisibility.Module,
                    cancellationToken: cancellationToken);
            }

            await LogAccessAsync(fileId, "register-reference", true, "Reference registered", cancellationToken);
            return Ok(new ReferenceCountDto(count));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(SafeClientError.ForUser(ex));
        }
    }

    [HttpDelete("{fileId:guid}/references")]
    [Authorize(Policy = ServicePolicies.FileStorageReferences)]
    public async Task<ActionResult<ReferenceCountDto>> ReleaseReference(
        Guid fileId,
        [FromBody] ReferenceRequestDto request,
        CancellationToken cancellationToken)
    {
        var count = await filePlatform.ReleaseReferenceAsync(fileId, request.HolderKey, cancellationToken);
        await LogAccessAsync(fileId, "release-reference", true, "Reference released", cancellationToken);

        if (count == 0)
        {
            await grantService.RevokeGrantsForFileAsync(fileId, cancellationToken);
            await filePlatform.TryDeleteWhenUnreferencedAsync(fileId, ParseUserId(), cancellationToken);
        }

        return Ok(new ReferenceCountDto(count));
    }

    [HttpDelete("{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken cancellationToken)
    {
        var decision = await AuthorizeAccessAsync(fileId, forDelete: true, cancellationToken);
        if (!decision.Allowed)
            return NotFound();

        var deleted = await filePlatform.TryDeleteWhenUnreferencedAsync(fileId, ParseUserId(), cancellationToken);
        if (!deleted)
        {
            var record = await filePlatform.GetAsync(fileId, cancellationToken);
            if (record is null)
                return NotFound();
            return Conflict("الملف لا يزال مرتبطاً بمراجع أو خاضعاً للاحتفاظ.");
        }

        await grantService.RevokeGrantsForFileAsync(fileId, cancellationToken);
        await LogAccessAsync(fileId, "delete", decision.Allowed, decision.Reason, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{fileId:guid}/force")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<IActionResult> ForceDelete(
        Guid fileId,
        [FromBody] ForceDeleteRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await filePlatform.ForceDeleteAsync(
                fileId,
                ParseUserId(),
                request.Reason,
                request.OverrideLiveRefs,
                cancellationToken);
            await grantService.RevokeGrantsForFileAsync(fileId, cancellationToken);
            await LogAccessAsync(fileId, "force-delete", true, "Force delete", cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(SafeClientError.ForUser(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(SafeClientError.ForUser(ex));
        }
    }

    [HttpPut("{fileId:guid}/retention")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<IActionResult> SetRetention(
        Guid fileId,
        [FromBody] LegalHoldRequestDto request,
        CancellationToken cancellationToken)
    {
        var record = await db.Files.FirstOrDefaultAsync(x => x.Id == fileId && !x.IsDeleted, cancellationToken);
        if (record is null)
            return NotFound();

        record.LegalHold = request.LegalHold;
        record.RetainUntil = request.RetainUntil;
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = ParseUserId();
        await db.SaveChangesAsync(cancellationToken);
        await LogAccessAsync(fileId, "retention", true, "Retention updated", cancellationToken);
        return NoContent();
    }

    [HttpGet("metrics")]
    public ActionResult<FileStorageMetricsDto> Metrics()
    {
        var snapshot = filePlatform.GetMetricsSnapshot();
        return Ok(new FileStorageMetricsDto(
            snapshot.TotalFiles,
            snapshot.PendingOrphans,
            snapshot.UnreferencedActive,
            snapshot.DeletedFiles,
            snapshot.PendingPurge,
            snapshot.DeadPurgeWorkItems,
            snapshot.PendingScan,
            snapshot.InfectedCount,
            snapshot.QuarantinedCount,
            snapshot.ScanErrorCount,
            snapshot.DeadScanWorkItems));
    }

    [HttpGet("admin/scan-status/{scanStatus}")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<ActionResult<IReadOnlyList<FileMetadataDto>>> ListByScanStatus(
        string scanStatus,
        CancellationToken cancellationToken)
    {
        var records = await filePlatform.ListByScanStatusAsync(scanStatus, cancellationToken);
        return Ok(records.Select(ToMetadataDto).Take(SearchFilterBase.MaxPageSize).ToList());
    }

    [HttpPost("admin/process-scan")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<ActionResult<object>> ProcessScan(CancellationToken cancellationToken)
    {
        var completed = 0;
        await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db,
            AdvisoryLockKeys.FileScanProcessor,
            async ct => completed = await filePlatform.ProcessScanQueueAsync(ct),
            cancellationToken);
        return Ok(new { completed });
    }

    [HttpPost("{fileId:guid}/admin/rescan")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<IActionResult> Rescan(Guid fileId, CancellationToken cancellationToken)
    {
        try
        {
            await filePlatform.EnqueueRescanAsync(fileId, cancellationToken);
            await LogAccessAsync(fileId, "rescan", true, "Rescan enqueued", cancellationToken);
            return Accepted();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health() => Ok(new { status = "healthy", service = "file-storage" });

    [HttpPost("admin/reconcile-ref-counts")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<ActionResult<object>> ReconcileRefCounts(CancellationToken cancellationToken)
    {
        var repaired = await filePlatform.ReconcileReferenceCountsAsync(cancellationToken);
        return Ok(new { repaired });
    }

    [HttpPost("admin/orphan-scan")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<ActionResult<OrphanScanDto>> OrphanScan(
        [FromQuery] bool persist = true,
        CancellationToken cancellationToken = default)
    {
        var result = await filePlatform.ScanOrphansAsync(persist, cancellationToken);
        return Ok(new OrphanScanDto(
            result.DbWithoutObject,
            result.ObjectWithoutDb,
            result.DeletedWithObject,
            result.RefCountDriftRepaired,
            result.UnresolvedExceptions));
    }

    [HttpGet("admin/purge-queue")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<ActionResult<IReadOnlyList<PurgeWorkItemDto>>> PurgeQueue(
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var items = await filePlatform.GetPurgeQueueAsync(status, cancellationToken);
        return Ok(items.Take(SearchFilterBase.MaxPageSize).Select(x => new PurgeWorkItemDto(
            x.Id, x.FileId, x.Bucket, x.ObjectKey, x.Attempt, x.NextAttemptAt, x.LastError, x.Status)).ToList());
    }

    [HttpPost("admin/process-purge")]
    [Authorize(Policy = HrManagementPolicies.FileStorageForceDelete)]
    public async Task<ActionResult<object>> ProcessPurge(CancellationToken cancellationToken)
    {
        var succeeded = await filePlatform.ProcessPurgeQueueAsync(cancellationToken);
        return Ok(new { succeeded });
    }

    private async Task<FileAccessDecision> AuthorizeAccessAsync(
        Guid fileId,
        bool forDelete,
        CancellationToken cancellationToken)
    {
        var (decision, _) = await AuthorizeAccessWithGrantAsync(fileId, forDelete, cancellationToken);
        return decision;
    }

    private async Task<(FileAccessDecision Decision, FileAccessGrant? Grant)> AuthorizeAccessWithGrantAsync(
        Guid fileId,
        bool forDelete,
        CancellationToken cancellationToken)
    {
        Guid requestTenantId;
        try
        {
            requestTenantId = tenantContext.RequireTenantId();
        }
        catch (InvalidOperationException)
        {
            return (FileAccessDecision.Deny("Missing tenant context"), null);
        }

        var grant = await grantService.FindGrantByFileAsync(fileId, cancellationToken);
        if (grant is null)
        {
            // Fail-closed: no grant means no access (prevents BOLA on orphaned legacy rows
            // until backfill / EnsureDefaultGrant runs).
            var deny = FileAccessDecision.Deny("No access grant");
            await LogAccessAsync(fileId, forDelete ? "delete-denied" : "access-denied", false, deny.Reason, cancellationToken);
            return (deny, null);
        }

        var decision = forDelete
            ? fileAccessPolicy.EvaluateDelete(User, grant, requestTenantId)
            : fileAccessPolicy.Evaluate(User, grant, requestTenantId);

        if (!decision.Allowed)
            await LogAccessAsync(fileId, forDelete ? "delete-denied" : "access-denied", false, decision.Reason, cancellationToken);

        return (decision, grant);
    }

    private static (string Module, string HolderType, string HolderId) ParseHolderKey(string holderKey)
    {
        // Expected forms: "HR.Core:Employee:{guid}" or "Employee:{guid}" or raw key.
        var parts = holderKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            >= 3 => (parts[0], parts[1], parts[2]),
            2 => ("File.Storage", parts[0], parts[1]),
            _ => ("File.Storage", "Holder", holderKey)
        };
    }

    private static FileMetadataDto ToMetadataDto(FileRecord record) =>
        new(record.Id, record.OriginalName, record.Extension, record.MimeType, record.Size,
            record.Sha256, record.Status, record.ScanStatus, record.Visibility, record.UploadedAt, record.UploadedBy,
            record.LegalHold, record.RetainUntil, record.ThreatName, record.CdrApplied);

    private static HRManagement.FileStorage.FileReference ToStorageReference(FileRecord record) =>
        new(record.Id, record.Bucket, record.ObjectKey, record.MimeType, record.Size, record.Sha256);

    private Guid? ParseUserId()
    {
        var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private async Task LogAccessAsync(
        Guid fileId,
        string action,
        bool allowed,
        string reason,
        CancellationToken cancellationToken)
    {
        db.FileAccessAuditLogs.Add(new FileAccessAuditLog
        {
            FileId = fileId,
            Action = action,
            UserId = ParseUserId(),
            UserName = User.Identity?.Name,
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            DecisionAllowed = allowed,
            DecisionReason = reason.Length > 256 ? reason[..256] : reason
        });
        await db.SaveChangesAsync(cancellationToken);

        if (action is "download" or "presigned-url" or "force-delete" or "delete" or "access-denied" or "delete-denied")
        {
            SecurityAudit.Log(
                HttpContext,
                SecurityAudit.EventTypes.FileAccessSensitive,
                resource: $"File:{fileId}",
                detailJson: $"{{\"action\":\"{action}\",\"allowed\":{allowed.ToString().ToLowerInvariant()},\"reason\":\"{reason.Replace("\"", "'")}\"}}");
        }
    }
}
