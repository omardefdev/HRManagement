using MediatR;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Payroll.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.EndOfService;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using System.Security.Claims;
using Payroll.Web.Services;

namespace Payroll.Web.Controllers;

public class EndOfServiceController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;
    private readonly IEmployeeLookup _employeeLookup;

    public EndOfServiceController(
        PayrollDbContext db,
        IMediator mediator,
        IEmployeeLookup employeeLookup)
    {
        _db = db;
        _mediator = mediator;
        _employeeLookup = employeeLookup;
    }
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index(Guid? employeeId, EosStatus? status, DateTime? calculatedFrom, DateTime? calculatedTo)
    {
        // QA-034 (IAM-002): branch scope. Payroll has no employee snapshots, so the employee's org unit is the one on
        // the active pay profile (PayrollEmployeeScope). A restricted user gets 403 for an employee outside the scope
        // and lists only employees inside it (no active profile = outside, fail-closed); unrestricted is unchanged.
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;
        if (employeeId is { } scopedEmployeeId
            && await this.ForbidIfEmployeeOutOfScopeAsync(scope, scopedEmployeeId, _db.EmployeePayProfiles) is { } employeeDenied)
            return employeeDenied;

        var query = _db.EndOfServiceSettlements.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
        {
            var inScope = _db.EmployeePayProfiles.EmployeeIdsInScope(scope);
            query = query.Where(e => inScope.Contains(e.EmployeeId));
        }
        if (employeeId.HasValue) query = query.Where(e => e.EmployeeId == employeeId.Value);
        if (status.HasValue) query = query.Where(e => e.Status == status.Value);
        if (calculatedFrom.HasValue) query = query.Where(e => e.CalculatedAt >= calculatedFrom.Value);
        if (calculatedTo.HasValue) query = query.Where(e => e.CalculatedAt <= calculatedTo.Value);

        var items = await query.OrderByDescending(e => e.CalculatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.EmployeeId = employeeId;
        ViewBag.Status = status;
        ViewBag.CalculatedFrom = calculatedFrom;
        ViewBag.CalculatedTo = calculatedTo;
        ViewBag.EmployeeNames = await _employeeLookup.GetEmployeeDisplayNamesAsync(
            items.Select(e => e.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty),
            HttpContext.RequestAborted);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await _db.EndOfServiceSettlements.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.EmployeeName = await _employeeLookup.GetEmployeeDisplayNameAsync(
            e.EmployeeId, HttpContext.RequestAborted) ?? "غير متاح";
        return View(e);
    }

    public IActionResult Create() => View(new EndOfServiceSettlement());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(EndOfServiceSettlement model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Status = EosStatus.Draft;
        model.CalculatedAt = DateTime.UtcNow;
        _db.EndOfServiceSettlements.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء مسودة التسوية";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var userId = RequireUserId();
        var (success, error) = await _mediator.Send(new ApproveEosSettlementCommand(id, userId));
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "تم اعتماد التسوية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> MarkPaid(Guid id)
    {
        var (success, error) = await _mediator.Send(new MarkEosSettlementPaidCommand(id, RequireUserId()));
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل الصرف" : error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
