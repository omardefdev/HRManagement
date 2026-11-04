using System.Security.Claims;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-40 (§6): the manual replay of custody outcomes and the org-unit re-sync. A dry run unless <c>dryRun=false</c> is
/// sent; run through <c>scripts/msg040-replay-lost-events.ps1</c>. Never automatic.
/// </summary>
[ApiController]
[Route("api/admin/messaging")]
[Authorize(Policy = HrManagementPolicies.MessagingRepair)]
public class MessagingRepairController(HrCoreMessagingRepair repair) : ControllerBase
{
    [HttpPost("replay/asset-outcomes")]
    public Task<MessagingRepairResult> ReplayAssetOutcomes([FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        repair.ReplayAssetOutcomesAsync(dryRun, Actor(), ct);

    [HttpPost("resync/org-units")]
    public Task<MessagingRepairResult> ResyncOrgUnits([FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        repair.ResyncOrgUnitsAsync(dryRun, Actor(), ct);

    private Guid? Actor()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
