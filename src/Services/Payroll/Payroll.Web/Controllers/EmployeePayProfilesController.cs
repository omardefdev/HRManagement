using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.EmployeePayProfiles;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

public class EmployeePayProfilesController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;

    public EmployeePayProfilesController(PayrollDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<IActionResult> Index()
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var items = await _mediator.Send(new GetEmployeePayProfilesQuery());
        if (scope.MustFilter)
            items = items.Where(p => p.OrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(p.OrgUnitId.Value)).Take(SearchFilterBase.MaxPageSize).ToList();
        else
            items = items.Take(SearchFilterBase.MaxPageSize).ToList();
        return View(items);
    }

    public async Task<IActionResult> History(Guid employeeId)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var profile = await _mediator.Send(new GetEmployeePayProfilesQuery());
        var match = profile.FirstOrDefault(p => p.EmployeeId == employeeId);
        if (match is not null && !scope.CanAccess(match.OrgUnitId))
            return Forbid();
        ViewBag.EmployeeId = employeeId;
        var items = await _mediator.Send(new GetEmployeePayProfileHistoryQuery(employeeId));
        return View(items);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Grades = new SelectList(await _db.PayGrades.AsNoTracking().OrderBy(g => g.GradeCode).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "GradeNameAr");
        return View(new EmployeePayProfile { EffectiveFrom = DateTime.UtcNow, IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeePayProfile model, string? changeReason)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Grades = new SelectList(await _db.PayGrades.AsNoTracking().OrderBy(g => g.GradeCode).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "GradeNameAr");
            return View(model);
        }

        var (success, _, error) = await _mediator.Send(new UpsertEmployeePayProfileCommand(
            model.EmployeeId, model.BaseSalary, model.PayGradeId,
            model.EffectiveFrom, model.EffectiveTo, model.IsActive, changeReason));

        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "فشل الحفظ");
            ViewBag.Grades = new SelectList(await _db.PayGrades.AsNoTracking().OrderBy(g => g.GradeCode).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "GradeNameAr");
            return View(model);
        }

        TempData["Success"] = "تم حفظ ملف الراتب";
        return RedirectToAction(nameof(Index));
    }
}
