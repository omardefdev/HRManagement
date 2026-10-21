using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// HCM-027 (E2): grade bounds of the salary scale effective at a date, for HR.Core offer letters.
/// The caller's tenant comes from <c>X-Tenant-ID</c> (TenantIdMiddleware) and the EF tenant query filters
/// apply, exactly as in <see cref="DsarInternalController"/>.
/// </summary>
[ApiController]
[Route("api/internal/pay-grades")]
[Authorize(Policy = ServicePolicies.PayrollGradeLookup)]
public sealed class PayGradeLookupInternalController(PayrollDbContext db) : ControllerBase
{
    [HttpGet("{gradeCode}")]
    public async Task<ActionResult<object>> GetGrade(string gradeCode, [FromQuery] string? asOf, CancellationToken ct)
    {
        DateOnly asOfDate;
        if (string.IsNullOrWhiteSpace(asOf))
            asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);
        else if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOfDate))
            return BadRequest("asOf must be yyyy-MM-dd.");

        var code = gradeCode.Trim();
        var dayStart = DateTime.SpecifyKind(asOfDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var nextDay = dayStart.AddDays(1);

        var grade = await db.PayGrades.AsNoTracking()
            .Where(g => g.GradeCode == code
                && g.SalaryScale.IsActive
                && g.SalaryScale.EffectiveFrom < nextDay
                && (g.SalaryScale.EffectiveTo == null || g.SalaryScale.EffectiveTo >= dayStart))
            .OrderByDescending(g => g.SalaryScale.EffectiveFrom)
            .Select(g => new
            {
                gradeCode = g.GradeCode,
                gradeNameAr = g.GradeNameAr,
                minSalary = g.MinSalary,
                maxSalary = g.MaxSalary,
                baseSalary = g.BaseSalary,
                salaryScaleCode = g.SalaryScale.Code,
                scaleEffectiveFrom = g.SalaryScale.EffectiveFrom,
                scaleEffectiveTo = g.SalaryScale.EffectiveTo
            })
            .FirstOrDefaultAsync(ct);

        if (grade is null)
            return NotFound();

        return Ok(grade);
    }
}
