using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Web.Controllers;

[Route("api/stats")]
[ApiController]
[Authorize]
public class StatsApiController : ControllerBase
{
    private readonly HRCoreDbContext _context;

    public StatsApiController(HRCoreDbContext context) => _context = context;

    [HttpGet("pending-approvals")]
    public async Task<IActionResult> PendingApprovals(CancellationToken ct)
    {
        var count = await _context.ApprovalRequests
            .CountAsync(r => r.Status == ApprovalStatus.Pending, ct);
        return Ok(new { count });
    }
}
