using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleEss)]
[Route("[controller]")]
public class EssController(PayrollDbContext db) : Controller
{
    /// <summary>ESS-001 / PAY-004: employee arrears balance (entitled / paid / arrears).</summary>
    [HttpGet("Arrears")]
    public async Task<IActionResult> ArrearsBalance()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null)
            return Forbid();

        var entries = await db.ArrearsLedgerEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .ToListAsync();
        var balance = ArrearsSettlementService.GetBalance(employeeId.Value, entries);
        return Json(balance);
    }

    [HttpGet("")]
    [HttpGet("Payslips")]
    public async Task<IActionResult> Payslips()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null)
            return Forbid();

        var lines = await db.SalaryLines
            .Include(l => l.PayrollRun)
            .Where(l => l.EmployeeId == employeeId)
            .OrderByDescending(l => l.PayrollRun!.PeriodEnd)
            .Take(24)
            .AsNoTracking()
            .ToListAsync();

        return View(lines);
    }

    [HttpGet("Payslip/{salaryLineId:guid}")]
    public async Task<IActionResult> Payslip(Guid salaryLineId)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null)
            return Forbid();

        var line = await db.SalaryLines
            .Include(l => l.PayrollRun)
            .FirstOrDefaultAsync(l => l.Id == salaryLineId && l.EmployeeId == employeeId);

        if (line is null)
            return NotFound();

        return RedirectToAction("Payslip", "PayrollReports", new { salaryLineId });
    }
}
