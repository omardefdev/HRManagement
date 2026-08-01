using System.Security.Claims;
using FluentValidation;
using HR.Contracts.Application.Features.AdvanceRequests;
using HR.Contracts.Application.Models;
using HR.Contracts.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR.Contracts.Web.ViewModels;
using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

public class AdvanceRequestsController : Controller
{
    private readonly IMediator _mediator;
    private readonly ContractsDbContext _db;

    public AdvanceRequestsController(IMediator mediator, ContractsDbContext db)
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
        var items = await _mediator.Send(new GetAdvanceRequestsQuery());
        if (scope.MustFilter)
            items = items.Where(x => x.OrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(x.OrgUnitId.Value)).ToList();
        return View(items);
    }

    public IActionResult Create() => View(new AdvanceRequestViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdvanceRequestViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _mediator.Send(new CreateAdvanceRequestCommand(Map(vm)));
            TempData["Success"] = "تم تسجيل طلب السلفة";
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
        if (await EnsureAdvanceInScopeAsync(id) is { } denied) return denied;
        var e = await _mediator.Send(new GetAdvanceRequestByIdQuery(id));
        if (e == null) return NotFound();

        return View(new AdvanceRequestViewModel
        {
            Id = e.Id,
            EmployeeId = e.EmployeeId,
            Amount = e.Amount,
            Reason = e.Reason,
            Status = e.Status,
            ApprovedBy = e.ApprovedBy
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AdvanceRequestViewModel vm)
    {
        if (vm.Id != id) return BadRequest();
        if (await EnsureAdvanceInScopeAsync(id) is { } denied) return denied;
        if (!ModelState.IsValid) return View(vm);
        try
        {
            var updated = await _mediator.Send(new UpdateAdvanceRequestCommand(id, Map(vm)));
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
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await EnsureAdvanceInScopeAsync(id) is { } denied) return denied;
        var deleted = await _mediator.Send(new DeleteAdvanceRequestCommand(id));
        if (!deleted) return NotFound();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (await EnsureAdvanceInScopeAsync(id) is { } denied) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new ApproveAdvanceRequestCommand(id, by));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "تم اعتماد طلب السلفة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id)
    {
        if (await EnsureAdvanceInScopeAsync(id) is { } denied) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new RejectAdvanceRequestCommand(id, by));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        TempData["Success"] = "تم رفض طلب السلفة";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> ApproveAjax([FromBody] AjaxIdRequest req)
    {
        if (await EnsureAdvanceInScopeAsync(req.Id) is { } denied) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new ApproveAdvanceRequestCommand(req.Id, by));
        if (!result.Success)
            return result.Error is null ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(new { success = true, message = "تم اعتماد طلب السلفة" });
    }

    [HttpPost]
    public async Task<IActionResult> RejectAjax([FromBody] AjaxIdRequest req)
    {
        if (await EnsureAdvanceInScopeAsync(req.Id) is { } denied) return denied;
        var by = ParseUserId(User);
        var result = await _mediator.Send(new RejectAdvanceRequestCommand(req.Id, by));
        if (!result.Success)
            return result.Error is null ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(new { success = true, message = "تم رفض طلب السلفة" });
    }


    private async Task<IActionResult?> EnsureAdvanceInScopeAsync(Guid id)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var e = await _mediator.Send(new GetAdvanceRequestByIdQuery(id));
        if (e is null) return NotFound();
        if (!scope.CanAccess(e.OrgUnitId)) return Forbid();
        return null;
    }

    private static AdvanceRequestDto Map(AdvanceRequestViewModel vm) =>
        new(vm.Id, vm.EmployeeId, vm.Amount, vm.Reason, vm.Status, vm.ApprovedBy);
}
