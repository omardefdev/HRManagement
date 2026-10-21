using Dev.Analytics.Application.Dtos;
using Dev.Analytics.Application.Services;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dev.Analytics.Web.Controllers;

[ApiController]
[Route("api/analytics/read-models")]
[Authorize(Policy = HrManagementPolicies.ModuleDevAnalytics)]
public sealed class AnalyticsReconciliationApiController(
    ReadModelReconciliationService reconciliation,
    IReadModelReconciliationStatusStore statusStore) : ControllerBase
{
    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<ReadModelReconciliationReport>> Reconcile(
        Guid id,
        [FromQuery] bool dryRun = false,
        [FromQuery] Guid? orgUnitId = null,
        CancellationToken ct = default)
    {
        try
        {
            var report = await reconciliation.ReconcileAsync(id, orgUnitId, dryRun, forceRebuild: false, ct);
            return Ok(report);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id:guid}/reconcile/status")]
    public ActionResult<ReadModelReconciliationReport> Status(Guid id)
    {
        var last = statusStore.GetLast(id);
        return last is null ? NotFound() : Ok(last);
    }
}
