using Dev.Analytics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Web.Controllers;

/// <summary>Development-only hooks for AC-19 drift injection.</summary>
[ApiController]
[Route("api/analytics/test")]
public sealed class AnalyticsTestHooksController(
    AnalyticsDbContext db,
    IWebHostEnvironment env) : ControllerBase
{
    [HttpPost("inject-drift/{readModelId:guid}")]
    [Authorize(Policy = HrManagementPolicies.ModuleDevAnalytics)]
    public async Task<IActionResult> InjectDrift(Guid readModelId, CancellationToken ct = default)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var model = await db.ReadModels.FindAsync([readModelId], ct);
        if (model is null) return NotFound();

        model.LastFingerprint = "DEADBEEF";
        model.Schema = "{}";
        await db.SaveChangesAsync(ct);

        return Ok(new { readModelId, injected = true });
    }
}
