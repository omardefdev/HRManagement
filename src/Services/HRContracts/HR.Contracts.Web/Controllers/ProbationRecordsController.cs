using HR.Contracts.Application.Features.Lookup;
using HR.Contracts.Application.Features.ProbationRecords;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using HR.Contracts.Web.ViewModels;
using MediatR;
using HRManagement.ServiceDefaults.Integration;

namespace HR.Contracts.Web.Controllers;

public class ProbationRecordsController : Controller
{
    private readonly IMediator _mediator;
    private readonly IHrCoreLookupClient _hrCoreLookup;

    public ProbationRecordsController(IMediator mediator, IHrCoreLookupClient hrCoreLookup)
    {
        _mediator = mediator;
        _hrCoreLookup = hrCoreLookup;
    }

    public async Task<IActionResult> Index(ProbationResult? result, Guid? employeeId)
    {
        var items = await _mediator.Send(new GetProbationRecordsQuery());
        if (result.HasValue)
            items = items.Where(x => x.Result == result.Value).ToList();
        if (employeeId.HasValue)
            items = items.Where(x => x.EmployeeId == employeeId.Value).ToList();

        ViewBag.ResultFilter = result;
        ViewBag.EmployeeId = employeeId;
        var nameIds = items.Select(x => x.EmployeeId).ToList();
        if (employeeId.HasValue)
            nameIds.Add(employeeId.Value);
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(nameIds);
        return View(items);
    }

    private async Task<Dictionary<Guid, string>> ResolveEmployeeNamesAsync(IEnumerable<Guid> ids)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => "غير متاح");
        try
        {
            foreach (var row in await _hrCoreLookup.GetEmployeesByIdsAsync(result.Keys.ToList()))
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ProbationRecordsController>>()
                .LogWarning(ex, "Employee name lookup failed for probation records.");
        }
        return result;
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
