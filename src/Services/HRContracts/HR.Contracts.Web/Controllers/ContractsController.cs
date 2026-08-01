using HR.Contracts.Application.Features.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Web.Controllers;

public class ContractsController : Controller
{
    private readonly ContractsDbContext _db;
    private readonly IMediator _mediator;

    public ContractsController(ContractsDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _db.Contracts
            .Include(c => c.ContractType)
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var contract = await _db.Contracts
            .Include(c => c.ContractType)
            .Include(c => c.Signatures)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contract == null) return NotFound();
        return View(contract);
    }

    private async Task PopulateContractTypesAsync(ContractCreateViewModel vm)
    {
        var types = await _db.ContractTypes.OrderBy(t => t.NameAr).AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        vm.ContractTypes = new SelectList(types, "Id", "NameAr", vm.ContractTypeId);
    }

    public async Task<IActionResult> Create()
    {
        var vm = new ContractCreateViewModel();
        await PopulateContractTypesAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractCreateViewModel vm)
    {
        await PopulateContractTypesAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        _db.Contracts.Add(new Contract
        {
            EmployeeId = vm.EmployeeId,
            ContractTypeId = vm.ContractTypeId,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            // Never from the request: activation is a workflow step, not a form field.
            Status = ContractStatus.Pending,
            Terms = string.IsNullOrWhiteSpace(vm.Terms) ? null : vm.Terms.Trim(),
            SignedBy = vm.SignedBy,
            SignedDate = vm.SignedDate,
            ProbationMonths = vm.ProbationMonths
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء العقد";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var c = await _db.Contracts.FindAsync(id);
        if (c == null) return NotFound();

        var vm = new ContractEditViewModel
        {
            Id = c.Id,
            EmployeeId = c.EmployeeId,
            ContractTypeId = c.ContractTypeId,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            Status = c.Status,
            Terms = c.Terms,
            SignedBy = c.SignedBy,
            SignedDate = c.SignedDate,
            ProbationMonths = c.ProbationMonths
        };
        await PopulateContractTypesAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ContractEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        await PopulateContractTypesAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        var c = await _db.Contracts.FindAsync(id);
        if (c == null) return NotFound();

        c.EmployeeId = vm.EmployeeId;
        c.ContractTypeId = vm.ContractTypeId;
        c.StartDate = vm.StartDate;
        c.EndDate = vm.EndDate;
        // Status is intentionally not copied: editing contract details must not move it through
        // the lifecycle. Activation, expiry and termination each have their own guarded command.
        c.Terms = string.IsNullOrWhiteSpace(vm.Terms) ? null : vm.Terms.Trim();
        c.SignedBy = vm.SignedBy;
        c.SignedDate = vm.SignedDate;
        c.ProbationMonths = vm.ProbationMonths;

        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث العقد";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var c = await _db.Contracts.Include(x => x.Signatures).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();

        _db.Contracts.Remove(c);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف العقد";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id)
    {
        var exists = await _db.Contracts.AnyAsync(c => c.Id == id);
        if (!exists) return NotFound();

        var (success, error) = await _mediator.Send(new ActivateContractCommand(id));
        if (!success)
        {
            if (error is null)
                return NotFound();
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم تفعيل العقد وإشعار الأنظمة المرتبطة";
        return RedirectToAction(nameof(Details), new { id });
    }
}
