using File.Storage.Domain.Entities;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace File.Storage.Web.Controllers;

/// <summary>
/// PLAN-30 (FS-030): service-to-service upload for files HR.Core relays on behalf of anonymous candidates,
/// and HR.Core's read-back of its own uploads (metadata, Clean content). Only
/// <see cref="ServicePolicies.FileStorageServiceUpload"/> applies: these actions stay out of
/// <see cref="FilesApiController"/>, whose class-level ModuleFileStorage policy would be required as well.
/// Service tokens carry no tenant claim, so the tenant comes from <c>X-Tenant-ID</c> (400 when missing).
/// </summary>
[ApiController]
[Route("api/files/service-upload")]
[Authorize(Policy = ServicePolicies.FileStorageServiceUpload)]
public sealed class FilesServiceUploadController(
    FileServiceUploadService serviceUploads,
    FileStorageDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    public sealed record ServiceMetadataRequestDto(IReadOnlyList<Guid>? FileIds);

    public sealed record ServiceFileMetadataDto(
        Guid FileId,
        string OriginalName,
        string MimeType,
        long Size,
        string ScanStatus,
        string Status,
        DateTime UploadedAt);

    /// <summary>Multipart form: <c>file</c> and <c>constraintProfile</c> (only <c>CandidateDocument</c>).</summary>
    [HttpPost]
    [RequestSizeLimit(FileServiceUploadService.MaxRequestBodyBytes)]
    public async Task<ActionResult<FilesApiController.FileUploadResponseDto>> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? constraintProfile,
        CancellationToken cancellationToken)
    {
        var (record, error) = await serviceUploads.UploadAsync(User, file, constraintProfile, cancellationToken);
        if (record is null)
            return BadRequest(error);

        await LogAccessAsync(record.Id, "service-upload", allowed: true, reason: "Service grant created", cancellationToken);

        return CreatedAtAction(
            nameof(GetContent),
            new { fileId = record.Id },
            new FilesApiController.FileUploadResponseDto(record.Id, record.OriginalName, record.Size, record.MimeType));
    }

    [HttpPost("metadata")]
    public async Task<ActionResult<IReadOnlyList<ServiceFileMetadataDto>>> Metadata(
        [FromBody] ServiceMetadataRequestDto request,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null)
            return BadRequest(FileServiceUploadService.MissingTenantMessage);

        var records = await serviceUploads.GetOwnedFilesAsync(User, request.FileIds ?? [], cancellationToken);
        return Ok(records
            .Select(r => new ServiceFileMetadataDto(r.Id, r.OriginalName, r.MimeType, r.Size, r.ScanStatus, r.Status, r.UploadedAt))
            .ToList());
    }

    [HttpGet("{fileId:guid}/content")]
    public async Task<IActionResult> GetContent(Guid fileId, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is null)
            return BadRequest(FileServiceUploadService.MissingTenantMessage);

        try
        {
            var content = await serviceUploads.OpenCleanContentAsync(User, fileId, cancellationToken);
            if (content is null)
            {
                // Missing, another caller's or tenant's file, or not Clean: one answer, no existence leak.
                await LogAccessAsync(fileId, "service-content-denied", allowed: false, reason: "Not a Clean service upload of the caller", cancellationToken);
                return NotFound();
            }

            await LogAccessAsync(fileId, "service-content", allowed: true, reason: "Service grant", cancellationToken);
            var (stream, contentType, fileName) = content.Value;
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            await LogAccessAsync(fileId, "service-content-checksum-fail", allowed: false, reason: ex.Message, cancellationToken);
            return Conflict(SafeClientError.ForUser(ex));
        }
    }

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

        if (action != "service-upload")
        {
            SecurityAudit.Log(
                HttpContext,
                SecurityAudit.EventTypes.FileAccessSensitive,
                resource: $"File:{fileId}",
                detailJson: $"{{\"action\":\"{action}\",\"allowed\":{allowed.ToString().ToLowerInvariant()},\"reason\":\"{reason.Replace("\"", "'")}\"}}");
        }
    }
}
