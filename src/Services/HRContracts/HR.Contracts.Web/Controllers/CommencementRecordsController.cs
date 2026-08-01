using HR.Contracts.Application.Features.CommencementRecords;
using HR.Contracts.Application.Features.Lookup;
using HR.Contracts.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using HR.Contracts.Web.ViewModels;
using MediatR;

namespace HR.Contracts.Web.Controllers;

public class CommencementRecordsController : Controller
{
    private readonly IMediator _mediator;
    public CommencementRecordsController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index()
    {
        var items = await _mediator.Send(new GetCommencementRecordsQuery());
        return View(items);
    }

    private async Task PopulateContractsAsync(CommencementRecordViewModel vm)
    {
        var contracts = await _mediator.Send(new GetContractsLookupQuery(vm.ContractId));
        var items = contracts.Select(c => new { Id = c.Id, Text = c.Label });
        vm.Contracts = new SelectList(items, "Id", "Text", vm.ContractId);
    }

    public async Task<IActionResult> Create()
    {
        var vm = new CommencementRecordViewModel();
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CommencementRecordViewModel vm)
    {
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        await _mediator.Send(new CreateCommencementRecordCommand(Map(vm)));
        TempData["Success"] = "تم تسجيل إذن المباشرة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _mediator.Send(new GetCommencementRecordByIdQuery(id));
        if (e == null) return NotFound();

        var vm = new CommencementRecordViewModel
        {
            Id = e.Id,
            EmployeeId = e.EmployeeId,
            ContractId = e.ContractId,
            StartDate = e.StartDate,
            NotifiedBy = e.NotifiedBy,
            NotificationDate = e.NotificationDate
        };
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CommencementRecordViewModel vm)
    {
        if (vm.Id != id) return BadRequest();
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        var updated = await _mediator.Send(new UpdateCommencementRecordCommand(id, Map(vm)));
        if (!updated) return NotFound();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _mediator.Send(new DeleteCommencementRecordCommand(id));
        if (!deleted) return NotFound();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    private static CommencementRecordDto Map(CommencementRecordViewModel vm) =>
        new(vm.Id, vm.EmployeeId, vm.ContractId, vm.StartDate, vm.NotifiedBy, vm.NotificationDate);
}
