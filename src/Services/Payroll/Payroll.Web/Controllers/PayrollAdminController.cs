using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.PayProfileRepair;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-55 (§3, P-8): an operator's repair of pay profiles written by builds before PLAN-55 (a contract activation's version without
/// the unit and the regime). Administrators only; dry run by default; the caller's agency; idempotent. Called by
/// <c>scripts/pay055-backfill-profile-orgunit.ps1</c>. The write validates an antiforgery token (the API is reached with a gateway
/// session as well as with a bearer token).
/// </summary>
[ApiController]
[Route("api/payroll/admin")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeCentralOnly("fills the empty unit and regime of every active pay profile of the agency, whatever its branch")]
public sealed class PayrollAdminController(IMediator mediator) : ControllerBase
{
    /// <summary>The antiforgery token the POST of this controller must send; the matching cookie is set on this response.</summary>
    [HttpGet("antiforgery")]
    [Authorize(Policy = HrManagementPolicies.PayrollRepair)]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    /// <summary>
    /// For each active pay profile whose unit or regime is empty: copies the value from the employee's latest profile that carries it.
    /// It never overwrites a value and touches nothing else. <paramref name="dryRun"/> (the default) reports the counts and the
    /// profile ids and changes nothing; a real run changes exactly what the dry run listed (the caller sends that list back) and
    /// writes one audit entry, in one transaction. 409 while a payroll calculation of the agency is in progress.
    /// </summary>
    [HttpPost("backfill-profile-orgunit")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollRepair)]
    public async Task<ActionResult<ProfileOrgUnitBackfillReport>> BackfillProfileOrgUnit(
        [FromQuery] bool dryRun = true, [FromBody] BackfillProfileOrgUnitApiRequest? request = null, CancellationToken ct = default)
    {
        var report = await mediator.Send(
            new BackfillProfileOrgUnitCommand(
                dryRun, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User), request?.ProfileIds), ct);
        if (report.Refusal is not null)
            return Conflict(new { error = report.Refusal });
        if (!report.DryRun && report.Changed > 0)
        {
            SecurityAudit.Log(
                HttpContext,
                SecurityAudit.EventTypes.PayrollOverride,
                resource: $"PayProfileRepair:{report.AuditEntryId:D}",
                detailJson: FormattableString.Invariant($"{{\"action\":\"PayProfileOrgUnitBackfill\",\"changed\":{report.Changed}}}"));
        }

        return Ok(report);
    }
}

/// <summary>
/// The body of the repair: <c>profileIds</c> are the profiles a dry run listed. A real run that is given them changes nothing else
/// (a profile that became fillable since is reported in <c>notInTheDryRun</c>). Without them it fills whatever is listed now.
/// </summary>
public sealed record BackfillProfileOrgUnitApiRequest(IReadOnlyCollection<Guid>? ProfileIds);
