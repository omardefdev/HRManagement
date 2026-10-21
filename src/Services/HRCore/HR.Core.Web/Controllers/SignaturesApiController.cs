using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Route("api/signatures")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class SignaturesApiController(HRCoreDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? subjectType, [FromQuery] string? subjectId, CancellationToken ct)
    {
        var query = db.SignatureRecords.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(subjectType))
            query = query.Where(s => s.SubjectType == subjectType);
        if (!string.IsNullOrWhiteSpace(subjectId))
            query = query.Where(s => s.SubjectId == subjectId);

        var records = await query
            .OrderByDescending(s => s.OccurredAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return Ok(records);
    }
}
