using System.Security.Claims;
using Dev.Analytics.Infrastructure.ExportJobs;
using HRManagement.SharedKernel.Exports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dev.Analytics.Web.Controllers;

[Authorize]
[Route("export-jobs")]
public class ExportJobsController(IAnalyticsExportJobService exportJobs) : Controller
{
    [HttpPost("workforce")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnqueueWorkforce([FromForm] Guid? orgUnitId, CancellationToken ct)
    {
        var (ok, jobId, error) = await exportJobs.EnqueueAsync(
            ExportJobTemplates.AnalyticsWorkforceCsv,
            new { orgUnitId },
            User,
            ct);
        if (!ok) return BadRequest(error);
        return Accepted(new { jobId });
    }

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

    [HttpPost("{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(v, out var userId)) return Unauthorized();
        var (ok, error) = await exportJobs.CancelAsync(id, userId, ct);
        if (!ok) return BadRequest(error);
        return Ok(new { cancelled = true });
    }
}
