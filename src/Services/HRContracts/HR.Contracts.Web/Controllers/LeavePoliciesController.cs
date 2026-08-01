using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Web.Controllers;

public class LeavePoliciesController : Controller
{
    private readonly IContractsRepository _repo;
    public LeavePoliciesController(IContractsRepository repo) => _repo = repo;

    public async Task<IActionResult> Index()
    {
        var policies = await _repo.LeavePolicies.AsNoTracking().OrderBy(p => p.LeaveType).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(policies);
    }

    public IActionResult Create() => View(new LeavePolicyCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeavePolicyCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var exists = await _repo.LeavePolicies.AnyAsync(p => p.LeaveType == vm.LeaveType);
        if (exists)
        {
            ModelState.AddModelError("LeaveType", "سياسة هذا النوع موجودة مسبقاً.");
            return View(vm);
        }

        await _repo.AddAsync(new LeavePolicy
        {
            LeaveType = vm.LeaveType,
            MaxDaysPerYear = vm.MaxDaysPerYear,
            RequiresMedicalCertificate = vm.RequiresMedicalCertificate,
            IsPaid = vm.IsPaid,
            MinNoticeDays = vm.MinNoticeDays,
            Description = vm.Description
        }, default);
        await _repo.SaveChangesAsync(default);
        TempData["Success"] = "تم إضافة السياسة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var policy = await _repo.LeavePolicies.FirstOrDefaultAsync(p => p.Id == id);
        if (policy is null) return NotFound();
        return View(new LeavePolicyEditViewModel
        {
            Id = policy.Id,
            LeaveType = policy.LeaveType,
            MaxDaysPerYear = policy.MaxDaysPerYear,
            RequiresMedicalCertificate = policy.RequiresMedicalCertificate,
            IsPaid = policy.IsPaid,
            MinNoticeDays = policy.MinNoticeDays,
            Description = policy.Description
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, LeavePolicyEditViewModel vm)
    {
        if (vm.Id != id) return BadRequest();
        if (!ModelState.IsValid) return View(vm);

        var entity = await _repo.LeavePolicies.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null) return NotFound();

        entity.MaxDaysPerYear = vm.MaxDaysPerYear;
        entity.RequiresMedicalCertificate = vm.RequiresMedicalCertificate;
        entity.IsPaid = vm.IsPaid;
        entity.MinNoticeDays = vm.MinNoticeDays;
        entity.Description = vm.Description;
        await _repo.SaveChangesAsync(default);
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entity = await _repo.LeavePolicies.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null) return NotFound();
        _repo.Remove(entity);
        await _repo.SaveChangesAsync(default);
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}
