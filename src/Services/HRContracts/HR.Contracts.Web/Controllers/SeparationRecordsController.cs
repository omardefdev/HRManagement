using HR.Contracts.Application.Features.Lookup;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using HR.Contracts.Web.ViewModels;
using HRManagement.ServiceDefaults;
using MediatR;

namespace HR.Contracts.Web.Controllers;

public class SeparationRecordsController : Controller
{
    private readonly IMediator _mediator;
    public SeparationRecordsController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index()
    {
        var items = await _mediator.Send(new GetSeparationRecordsQuery());
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var r = await _mediator.Send(new GetSeparationRecordByIdQuery(id));
        if (r == null) return NotFound();
        return View(r);
    }

    private async Task PopulateContractsAsync(SeparationRecordCreateViewModel vm)
    {
        var contracts = await _mediator.Send(new GetContractsLookupQuery(vm.ContractId));
        var items = contracts.Select(c => new { Id = c.Id, Text = c.Label });
        vm.Contracts = new SelectList(items, "Id", "Text", vm.ContractId);
    }

    public async Task<IActionResult> Create()
    {
        var vm = new SeparationRecordCreateViewModel();
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SeparationRecordCreateViewModel vm)
    {
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        await _mediator.Send(new CreateSeparationRecordCommand(MapCreate(vm, ParseUserId())));
        TempData["Success"] = "تم تسجيل سجل الفصل";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var r = await _mediator.Send(new GetSeparationRecordByIdQuery(id));
        if (r == null) return NotFound();

        var vm = new SeparationRecordEditViewModel
        {
            Id = r.Id,
            EmployeeId = r.EmployeeId,
            ContractId = r.ContractId,
            SeparationType = r.SeparationType,
            EffectiveDate = r.EffectiveDate,
            Reason = r.Reason,
            Documents = r.Documents,
            SeparationEventPublished = r.SeparationEventPublished
        };
        ViewBag.ApprovedBy = r.ApprovedBy;
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SeparationRecordEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        var existing = await _mediator.Send(new GetSeparationRecordByIdQuery(id));
        if (existing is null) return NotFound();
        var updated = await _mediator.Send(new UpdateSeparationRecordCommand(id, MapEdit(vm, existing.ApprovedBy)));
        if (!updated) return NotFound();
        TempData["Success"] = "تم تحديث سجل الفصل";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _mediator.Send(new DeleteSeparationRecordCommand(id));
        if (!deleted)
        {
            TempData["Warning"] = "تعذر حذف سجل الفصل بعد نشره أو لعدم وجوده.";
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> PublishSeparation(Guid id)
    {
        var result = await _mediator.Send(new PublishSeparationCommand(id));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Warning"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "تم نشر حدث إكمال الفصل";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var result = await _mediator.Send(new CancelSeparationCommand(id));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Warning"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "تم إلغاء سجل الفصل";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> OverrideChecklistStep(Guid id, HR.Contracts.Domain.Enums.SeparationStep step, string reason, Guid approvedBy)
    {
        var actorId = ParseUserId();
        if (actorId is null)
        {
            TempData["Warning"] = "تعذر تحديد المستخدم الحالي لتنفيذ الاستثناء.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _mediator.Send(new OverrideSeparationChecklistStepCommand(id, step, reason, actorId.Value, approvedBy));
        if (!result.Success)
        {
            if (result.Error is null) return NotFound();
            TempData["Warning"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم تسجيل استثناء على خطوة الإخلاء.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Guid? ParseUserId()
    {
        var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static SeparationRecordDto MapCreate(SeparationRecordCreateViewModel vm, Guid? approvedBy) =>
        new(null, vm.EmployeeId, vm.ContractId, vm.SeparationType, vm.EffectiveDate, vm.Reason, approvedBy, vm.Documents, false);

    private static SeparationRecordDto MapEdit(SeparationRecordEditViewModel vm, Guid? approvedBy) =>
        new(vm.Id, vm.EmployeeId, vm.ContractId, vm.SeparationType, vm.EffectiveDate, vm.Reason, approvedBy, vm.Documents, vm.SeparationEventPublished);
}
