using System.Security.Claims;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-25 (G-02 / G-14): operator repair of approvals left without a workflow by builds before PLAN-25, and of orphan
/// RECRUITMENT-HIRE instances. Dry run by default; tenant of the caller; idempotent. Called by
/// <c>scripts/hcm025-repair-stuck-approvals.ps1</c>.
/// </summary>
[Route("api/recruitment/admin")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
public sealed class RecruitmentAdminController(ApprovalRepairService repair) : ControllerBase
{
    [HttpPost("repair-approvals")]
    public async Task<ActionResult<ApprovalRepairReport>> RepairApprovals([FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id
            : (Guid?)null;
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name;
        return Ok(await repair.RunAsync(dryRun, userId, userName, ct));
    }
}
