using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR.Contracts.Infrastructure.ExportJobs;

namespace HR.Contracts.Web.Controllers;

[Authorize]
[Route("export-jobs")]
public class ExportJobsController(IContractsExportJobService exportJobs) : Controller
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        var job = await exportJobs.GetAsync(id, ct);
        if (job is null) return NotFound();

        return Ok(new ExportJobStatusDto
        {
            Id = job.Id,
            ExportType = job.ExportType,
            Status = job.Status,
            TotalCount = job.TotalCount,
            ProcessedCount = job.ProcessedCount,
            Error = job.Error,
            ExpiresAt = job.ExpiresAt,
            HasResult = job.Status == ExportJobStatus.Completed && job.ResultFileId.HasValue,
            TemplateCode = job.TemplateCode,
            TemplateVersion = job.TemplateVersion
        });
    }

    [HttpPost("{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var (ok, error) = await exportJobs.CancelAsync(id, userId, ct);
        if (!ok) return BadRequest(error);
        return Ok(new { cancelled = true });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (ok, stream, fileName, contentType, error) = await exportJobs.OpenDownloadAsync(id, User, ct);
        if (!ok || stream is null)
        {
            if (error?.Contains("غير مصرح", StringComparison.Ordinal) == true)
                return Forbid();
            return BadRequest(error ?? "تعذّر التحميل.");
        }

        return File(stream, contentType ?? "application/octet-stream", fileName ?? "export.bin");
    }

    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : throw new UnauthorizedAccessException();
    }
}
