using HR.Contracts.Application.Features.Lookup;
using HR.Contracts.Application.Features.ProbationRecords;
using HR.Contracts.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using HR.Contracts.Web.ViewModels;
using MediatR;

namespace HR.Contracts.Web.Controllers;

public class ProbationRecordsController : Controller
{
    private readonly IMediator _mediator;
    public ProbationRecordsController(IMediator mediator) => _mediator = mediator;

    public async Task<IActionResult> Index()
    {
        var items = await _mediator.Send(new GetProbationRecordsQuery());
        return View(items);
    }

    private async Task PopulateContractsAsync(ProbationRecordViewModel vm)
    {
        var contracts = await _mediator.Send(new GetContractsLookupQuery(vm.ContractId));
        var items = contracts.Select(c => new { Id = c.Id, Text = c.Label });
        vm.Contracts = new SelectList(items, "Id", "Text", vm.ContractId);
    }

    public async Task<IActionResult> Create()
    {
        var vm = new ProbationRecordViewModel();
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProbationRecordViewModel vm)
    {
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        await _mediator.Send(new CreateProbationRecordCommand(Map(vm)));
        TempData["Success"] = "تم تسجيل فترة الاختبار";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _mediator.Send(new GetProbationRecordByIdQuery(id));
        if (e == null) return NotFound();

        var vm = new ProbationRecordViewModel
        {
            Id = e.Id,
            ContractId = e.ContractId,
            EmployeeId = e.EmployeeId,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            Result = e.Result,
            DecisionBy = e.DecisionBy,
            DecisionDate = e.DecisionDate,
            Notes = e.Notes
        };
        await PopulateContractsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ProbationRecordViewModel vm)
    {
        if (vm.Id != id) return BadRequest();
        await PopulateContractsAsync(vm);
        if (!ModelState.IsValid) return View(vm);
        var updated = await _mediator.Send(new UpdateProbationRecordCommand(id, Map(vm)));
        if (!updated) return NotFound();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _mediator.Send(new DeleteProbationRecordCommand(id));
        if (!deleted) return NotFound();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }

    private static ProbationRecordDto Map(ProbationRecordViewModel vm) =>
        new(vm.Id, vm.ContractId, vm.EmployeeId, vm.StartDate, vm.EndDate, vm.Result, vm.DecisionBy, vm.DecisionDate, vm.Notes);
}
