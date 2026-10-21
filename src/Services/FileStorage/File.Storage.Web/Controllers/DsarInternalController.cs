using File.Storage.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace File.Storage.Web.Controllers;

/// <summary>AC-27: file metadata indicators for DSAR (no content).</summary>
[ApiController]
[Route("api/internal/dsar-subjects")]
[Authorize(Policy = ServicePolicies.DsarSubjectRead)]
public sealed class DsarInternalController(FileStorageDbContext db) : ControllerBase
{
    [HttpGet("{employeeId:guid}/files")]
    public async Task<ActionResult<object>> GetFileIndicators(Guid employeeId, CancellationToken ct)
    {
        var files = await db.Files.AsNoTracking()
            .Where(f => f.UploadedBy == employeeId && !f.IsDeleted)
            .Select(f => new
            {
                f.Id,
                f.OriginalName,
                f.MimeType,
                f.Size,
                scanStatus = f.ScanStatus.ToString(),
                f.UploadedAt
            })
            .Take(100)
            .ToListAsync(ct);

        return Ok(new
        {
            employeeId,
            fileCount = files.Count,
            files
        });
    }
}
