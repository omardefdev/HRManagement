using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleEss)]
[Route("[controller]")]
public class EssTalentController(HRCoreDbContext db) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var vacancies = await db.InternalVacancies
            .Include(v => v.OrgUnit)
            .Where(v => v.IsOpen && (v.ClosingDate == null || v.ClosingDate >= today))
            .OrderByDescending(v => v.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();
        return View(vacancies);
    }

    [HttpPost("Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid vacancyId, string? motivation)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var vacancy = await db.InternalVacancies.FindAsync(vacancyId);
        if (vacancy is null || !vacancy.IsOpen)
        {
            TempData["Error"] = "الشاغر غير متاح للترشّح";
            return RedirectToAction(nameof(Index));
        }

        var alreadyApplied = await db.InternalApplications
            .AnyAsync(a => a.InternalVacancyId == vacancyId && a.EmployeeId == employeeId.Value);
        if (alreadyApplied)
        {
            TempData["Error"] = "سبق أن ترشّحت لهذا الشاغر";
            return RedirectToAction(nameof(MyApplications));
        }

        db.InternalApplications.Add(new InternalApplication
        {
            InternalVacancyId = vacancyId,
            EmployeeId = employeeId.Value,
            Motivation = motivation,
            Status = InternalApplicationStatus.Submitted
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تقديم ترشّحك بنجاح";
        return RedirectToAction(nameof(MyApplications));
    }

    [HttpGet("MyApplications")]
    public async Task<IActionResult> MyApplications()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var applications = await db.InternalApplications
            .Include(a => a.InternalVacancy)
            .Where(a => a.EmployeeId == employeeId.Value)
            .OrderByDescending(a => a.AppliedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();
        return View(applications);
    }
}
