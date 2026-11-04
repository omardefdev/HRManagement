using System.Security.Claims;
using HR.Movements.Infrastructure.Messaging;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Movements.Web.Controllers;

/// <summary>
/// PLAN-40 (§6): the manual replay of events lost before the fix. A dry run unless <c>dryRun=false</c> is sent; run through
/// <c>scripts/msg040-replay-lost-events.ps1</c>. Never automatic.
/// </summary>
[ApiController]
[Route("api/admin/messaging")]
[Authorize(Policy = HrManagementPolicies.MessagingRepair)]
public class MessagingRepairController(MovementsMessagingRepair repair) : ControllerBase
{
    [HttpGet("antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpPost("replay/promotions")]
    [ValidateAntiForgeryToken]
    public Task<MessagingRepairResult> ReplayPromotions([FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        repair.ReplayPromotionsAsync(dryRun, Actor(), ct);

    [HttpPost("replay/delegations")]
    [ValidateAntiForgeryToken]
    public Task<MessagingRepairResult> ReplayDelegations([FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        repair.ReplayDelegationsAsync(dryRun, Actor(), ct);

    private Guid? Actor()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
