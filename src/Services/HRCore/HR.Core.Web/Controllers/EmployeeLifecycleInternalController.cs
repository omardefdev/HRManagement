using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Controllers;

/// <summary>FUN-002 / GOV: internal lifecycle flags for peer services (S2S).</summary>
[ApiController]
[Route("api/internal/employees")]
[Authorize(Policy = ServicePolicies.HrCoreLookupS2S)]
public class EmployeeLifecycleInternalController(HRCoreDbContext db) : ControllerBase
{
    [HttpGet("{employeeId:guid}/lifecycle-flags")]
    public async Task<ActionResult<object>> GetFlags(Guid employeeId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.LegalHold, e.LegalHoldReason, e.CurrentStatus })
            .FirstOrDefaultAsync(ct);
        if (employee is null) return NotFound();
        return Ok(new
        {
            employeeId = employee.Id,
            legalHold = employee.LegalHold,
            legalHoldReason = employee.LegalHoldReason,
            status = employee.CurrentStatus.ToString()
        });
    }
}
