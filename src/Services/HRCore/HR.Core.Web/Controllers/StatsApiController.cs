using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.Controllers;

[Route("api/stats")]
[ApiController]
[Authorize]
public class StatsApiController(HrCoreWorkInboxService inboxService) : ControllerBase
{
    [HttpGet("pending-approvals")]
    public async Task<IActionResult> PendingApprovals(CancellationToken ct)
    {
        var userId = WorkflowPanelBuilder.ParseUserId(User);
        if (userId is null)
            return Ok(new { count = 0, legacyCount = 0, workflowCount = 0 });

        var breakdown = await inboxService.GetPendingBreakdownAsync(User, ct);
        return Ok(new
        {
            count = breakdown.Total,
            legacyCount = breakdown.LegacyCount,
            workflowCount = breakdown.WorkflowCount
        });
    }
}
