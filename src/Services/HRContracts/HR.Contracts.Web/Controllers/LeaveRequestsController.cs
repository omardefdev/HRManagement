using System.Security.Claims;
using FluentValidation;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Application.Models;
using HR.Contracts.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR.Contracts.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

public class LeaveRequestsController : Controller
{
    private readonly IMediator _mediator;
    private readonly ContractsDbContext _db;

    public LeaveRequestsController(IMediator mediator, ContractsDbContext db)
    {
        _mediator = mediator;
        _db = db;
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index()
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var items = await _mediator.Send(new GetLeaveRequestsQuery());
        if (scope.MustFilter)
            items = items.Where(x => x.OrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(x.OrgUnitId.Value)).ToList();
        return View(items);
    }

    public IActionResult Create() => View(new LeaveRequestViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeaveRequestViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _mediator.Send(new CreateLeaveRequestCommand(Map(vm)));
            TempData["Success"] = "تم تسجيل طلب الإجازة";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, SafeClientError.ForUser(ex));
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(id);
        if (denied is not null) return denied;
        var e = await _mediator.Send(new GetLeaveRequestByIdQuery(id));
        if (e == null) return NotFound();

        return View(new LeaveRequestViewModel
        {
            Id = e.Id,
            EmployeeId = e.EmployeeId,
            LeaveType = e.LeaveType,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            DurationDays = e.DurationDays,
            Status = e.Status,
            ApprovedBy = e.ApprovedBy,
            Notes = e.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LeaveRequestViewModel vm)
    {
        if (vm.Id != id) return BadRequest();
        var (denied, _) = await EnsureLeaveInScopeAsync(id);
        if (denied is not null) return denied;
        if (!ModelState.IsValid) return View(vm);
        try
        {
            var updated = await _mediator.Send(new UpdateLeaveRequestCommand(id, Map(vm)));
            if (!updated) return NotFound();
            TempData["Success"] = "تم التحديث";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex)
        {
            ModelState.AddModelError(string.Empty, SafeClientError.ForUser(ex));
            return View(vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(id);
        if (denied is not null) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new CancelLeaveRequestCommand(id, by));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "تم إلغاء طلب الإجازة وإعادة الرصيد";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(id);
        if (denied is not null) return denied;
        var deleted = await _mediator.Send(new DeleteLeaveRequestCommand(id));
        if (!deleted) return NotFound();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    [Idempotent]
    public async Task<IActionResult> Approve(Guid id)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(id);
        if (denied is not null) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new ApproveLeaveRequestCommand(id, by));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "تم اعتماد طلب الإجازة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(id);
        if (denied is not null) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new RejectLeaveRequestCommand(id, by));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "تم رفض طلب الإجازة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> ApproveAjax([FromBody] AjaxIdRequest req)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(req.Id);
        if (denied is not null) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new ApproveLeaveRequestCommand(req.Id, by));
        if (!result.Success)
            return result.Error is null ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(new { success = true, message = "تم اعتماد طلب الإجازة" });
    }

    [HttpPost]
    public async Task<IActionResult> RejectAjax([FromBody] AjaxIdRequest req)
    {
        var (denied, _) = await EnsureLeaveInScopeAsync(req.Id);
        if (denied is not null) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new RejectLeaveRequestCommand(req.Id, by));
        if (!result.Success)
            return result.Error is null ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(new { success = true, message = "تم رفض طلب الإجازة" });
    }


    private async Task<(IActionResult? Denied, Guid? OrgUnitId)> EnsureLeaveInScopeAsync(Guid id)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return (Forbid(), null);
        var e = await _mediator.Send(new GetLeaveRequestByIdQuery(id));
        if (e is null) return (NotFound(), null);
        if (!scope.CanAccess(e.OrgUnitId)) return (Forbid(), e.OrgUnitId);
        return (null, e.OrgUnitId);
    }

    private static LeaveRequestDto Map(LeaveRequestViewModel vm) =>
        new(vm.Id, vm.EmployeeId, vm.LeaveType, vm.StartDate, vm.EndDate, vm.DurationDays, vm.Status, vm.ApprovedBy, vm.Notes);
}

public record AjaxIdRequest(Guid Id);

