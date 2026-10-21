using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Payroll.Application.Abstractions;
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
    private readonly IEmployeeLookup _employeeLookup;

    public EmployeePayProfilesController(
        PayrollDbContext db,
        IMediator mediator,
        IEmployeeLookup employeeLookup)
    {
        _db = db;
        _mediator = mediator;
        _employeeLookup = employeeLookup;
    }

    public async Task<IActionResult> Index(Guid? employeeId, Guid? payGradeId, bool? isActive, DateTime? effectiveFrom, DateTime? effectiveTo)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var items = await _mediator.Send(new GetEmployeePayProfilesQuery());
        if (scope.MustFilter)
            items = items.Where(p => p.OrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(p.OrgUnitId.Value)).ToList();
        if (employeeId.HasValue) items = items.Where(p => p.EmployeeId == employeeId.Value).ToList();
        if (payGradeId.HasValue) items = items.Where(p => p.PayGradeId == payGradeId.Value).ToList();
        if (isActive.HasValue) items = items.Where(p => p.IsActive == isActive.Value).ToList();
        if (effectiveFrom.HasValue) items = items.Where(p => p.EffectiveFrom >= effectiveFrom.Value).ToList();
        if (effectiveTo.HasValue) items = items.Where(p => p.EffectiveFrom <= effectiveTo.Value).ToList();
        items = items.Take(SearchFilterBase.MaxPageSize).ToList();

        ViewBag.EmployeeId = employeeId;
        ViewBag.PayGradeId = payGradeId;
        ViewBag.IsActive = isActive;
        ViewBag.EffectiveFrom = effectiveFrom;
        ViewBag.EffectiveTo = effectiveTo;
        ViewBag.PayGrades = new SelectList(
            await _db.PayGrades.AsNoTracking().OrderBy(g => g.GradeCode).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "GradeNameAr", payGradeId);
        ViewBag.EmployeeNames = await _employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(p => p.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
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
        ViewBag.EmployeeName = await _employeeLookup.GetEmployeeDisplayNameAsync(
            employeeId, HttpContext.RequestAborted) ?? "غير متاح";
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
