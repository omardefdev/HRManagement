using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.EmployeePayProfiles;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// Pay profiles are read here and never written: PLAN-53 turned «ملف راتب جديد» into the salary change request form, so a salary or a
/// grade changes only through a request another person approves. Branch scope (the IAM study's note 3): the filter resolves every
/// <c>employeeId</c> through the employee's active profile before the action runs (a foreign employee is 404), and the list is filtered.
/// </summary>
[OrgScoped(typeof(EmployeePayProfile))]
public class EmployeePayProfilesController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;
    private readonly IEmployeeLookup _employeeLookup;
    private readonly IPayrollCalendar _calendar;

    public EmployeePayProfilesController(
        PayrollDbContext db,
        IMediator mediator,
        IEmployeeLookup employeeLookup,
        IPayrollCalendar calendar)
    {
        _db = db;
        _mediator = mediator;
        _employeeLookup = employeeLookup;
        _calendar = calendar;
    }

    public async Task<IActionResult> Index(Guid? employeeId, Guid? payGradeId, bool? isActive, DateTime? effectiveFrom, DateTime? effectiveTo)
    {
        var query = await _db.EmployeePayProfiles.Include(p => p.PayGrade).AsNoTracking().InOrgScopeAsync(HttpContext, _db);
        if (employeeId.HasValue) query = query.Where(p => p.EmployeeId == employeeId.Value);
        if (payGradeId.HasValue) query = query.Where(p => p.PayGradeId == payGradeId.Value);
        if (isActive.HasValue) query = query.Where(p => p.IsActive == isActive.Value);
        if (effectiveFrom.HasValue) query = query.Where(p => p.EffectiveFrom >= effectiveFrom.Value);
        if (effectiveTo.HasValue) query = query.Where(p => p.EffectiveFrom <= effectiveTo.Value);
        IList<EmployeePayProfile> items = await query.OrderByDescending(p => p.EffectiveFrom).Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(HttpContext.RequestAborted);

        ViewBag.EmployeeId = employeeId;
        ViewBag.PayGradeId = payGradeId;
        ViewBag.IsActive = isActive;
        ViewBag.EffectiveFrom = effectiveFrom;
        ViewBag.EffectiveTo = effectiveTo;
        ViewBag.CanRequest = PayrollPolicyRules.CanProcess(User);
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
        ViewBag.EmployeeId = employeeId;
        ViewBag.EmployeeName = await _employeeLookup.GetEmployeeDisplayNameAsync(
            employeeId, HttpContext.RequestAborted) ?? "غير متاح";
        var items = await _mediator.Send(new GetEmployeePayProfileHistoryQuery(employeeId));
        return View(items);
    }

    /// <summary>The salary change request form (PLAN-53): what is saved is a request, never a pay profile.</summary>
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(Guid? employeeId)
    {
        await PopulateGradesAsync();
        // D-53-1: the first day of a month, by default the next one (dates of the configured zone).
        return View(new PayProfileChangeRequestViewModel
        {
            EmployeeId = employeeId,
            EffectiveFrom = _calendar.FirstDayOfCurrentMonth.AddMonths(1),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(PayProfileChangeRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateGradesAsync();
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await _mediator.Send(new SubmitPayProfileChangeCommand(
            model.EmployeeId!.Value, model.ProposedBaseSalary, model.ProposedPayGradeId, model.EffectiveFrom!.Value, model.Reason,
            model.LegalReference, PayProfileChangeSource.Manual, SourceReference: null,
            PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User), await HttpContext.GetOrgScopeAsync()),
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            if (result.Failure == PayProfileChangeFailure.NotFound)
                return NotFound();
            // The same answer as the API (400 a broken rule, 409 a second open request), with the Arabic message on the form.
            ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر تقديم الطلب");
            await PopulateGradesAsync();
            Response.StatusCode = result.StatusCode;
            return View(model);
        }

        TempData["Success"] = "قُدّم طلب تغيير الراتب، وهو بانتظار اعتماد معتمد رواتب آخر";
        return RedirectToAction(nameof(PayProfileChangesController.Details), "PayProfileChanges", new { id = result.RequestId });
    }

    private async Task PopulateGradesAsync() =>
        ViewBag.Grades = new SelectList(
            await _db.PayGrades.AsNoTracking().OrderBy(g => g.GradeCode).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "GradeNameAr");
}
