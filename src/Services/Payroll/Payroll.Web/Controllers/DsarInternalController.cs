using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>AC-27: masked pay profile summary for DSAR collection.</summary>
[ApiController]
[Route("api/internal/dsar-subjects")]
[Authorize(Policy = ServicePolicies.DsarSubjectRead)]
public sealed class DsarInternalController(PayrollDbContext db) : ControllerBase
{
    [HttpGet("{employeeId:guid}")]
    public async Task<ActionResult<object>> GetSubject(Guid employeeId, CancellationToken ct)
    {
        var profile = await db.EmployeePayProfiles.AsNoTracking()
            .Include(p => p.PayGrade)
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .Select(p => new
            {
                employeeId = p.EmployeeId,
                baseSalary = p.BaseSalary,
                gradeCode = p.PayGrade != null ? p.PayGrade.GradeCode : null,
                gradeName = p.PayGrade != null ? p.PayGrade.GradeNameAr : null,
                // PLAN-55: from the account entity. Present: an account in force or waiting for verification. Verified: one in force.
                bankAccountPresent = db.EmployeeBankAccounts.Any(a => a.EmployeeId == p.EmployeeId
                    && (a.Status == Payroll.Domain.Enums.BankAccountStatus.Active
                        || a.Status == Payroll.Domain.Enums.BankAccountStatus.PendingVerification)),
                bankAccountVerified = db.EmployeeBankAccounts.Any(a => a.EmployeeId == p.EmployeeId
                    && a.Status == Payroll.Domain.Enums.BankAccountStatus.Active),
                effectiveFrom = p.EffectiveFrom
            })
            .FirstOrDefaultAsync(ct);

        if (profile is null)
            return NotFound();

        return Ok(profile);
    }
}
