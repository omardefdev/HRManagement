using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.ServiceDefaults.Integration;

namespace HR.Contracts.Web.Controllers;

public class LeaveBalancesController : Controller
{
    private readonly IContractsRepository _repo;
    private readonly ContractsDbContext _db;
    private readonly IHrCoreLookupClient _hrCoreLookup;

    public LeaveBalancesController(
        IContractsRepository repo,
        ContractsDbContext db,
        IHrCoreLookupClient hrCoreLookup)
    {
        _repo = repo;
        _db = db;
        _hrCoreLookup = hrCoreLookup;
    }

    public async Task<IActionResult> Index(Guid? employeeId, int? year, LeaveType? leaveType)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        if (employeeId is { } eid)
        {
            if (await this.ForbidIfEmployeeOutOfScopeAsync(
                    User, eid, _db.EmployeeOrgUnitSnapshots, _db.OrgUnitSnapshots) is { } empDenied)
                return empDenied;
        }

        var currentYear = year ?? DateTime.UtcNow.Year;
        var query = _repo.LeaveBalances.AsNoTracking().Where(b => b.Year == currentYear);
        if (employeeId.HasValue)
            query = query.Where(b => b.EmployeeId == employeeId.Value);
        if (leaveType.HasValue)
            query = query.Where(b => b.LeaveType == leaveType.Value);
        else if (scope.MustFilter)
        {
            var allowedEmployees = _db.EmployeeOrgUnitSnapshots.AsNoTracking()
                .Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value))
                .Select(e => e.EmployeeId);
            query = query.Where(b => allowedEmployees.Contains(b.EmployeeId));
        }

        var balances = await query.OrderBy(b => b.EmployeeId).ThenBy(b => b.LeaveType).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Year = currentYear;
        ViewBag.EmployeeId = employeeId;
        ViewBag.LeaveTypeFilter = leaveType;
        var nameIds = balances.Select(b => b.EmployeeId).ToList();
        if (employeeId.HasValue)
            nameIds.Add(employeeId.Value);
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(nameIds);
        return View(balances);
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
            HttpContext.RequestServices.GetRequiredService<ILogger<LeaveBalancesController>>()
                .LogWarning(ex, "Employee name lookup failed for leave balances.");
        }
        return result;
    }

    public IActionResult Create()
    {
        return View(new LeaveBalanceCreateViewModel { Year = DateTime.UtcNow.Year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeaveBalanceCreateViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var existing = await _repo.LeaveBalances
            .AnyAsync(b => b.EmployeeId == vm.EmployeeId && b.LeaveType == vm.LeaveType && b.Year == vm.Year);
        if (existing)
        {
            ModelState.AddModelError("", "يوجد رصيد مسجل لهذا الموظف/النوع/السنة بالفعل.");
            return View(vm);
        }

        // A new balance always starts with nothing consumed. Used is maintained by leave approval,
        // so accepting it here would let a requester grant themselves days or erase days taken.
        await _repo.AddAsync(new LeaveBalance
        {
            EmployeeId = vm.EmployeeId,
            LeaveType = vm.LeaveType,
            Year = vm.Year,
            TotalEntitlement = vm.TotalEntitlement,
            Used = 0
        }, default);

        await _repo.SaveChangesAsync(default);
        TempData["Success"] = "تم إضافة الرصيد";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> InitializeYear(int? year)
    {
        ViewBag.Year = year ?? DateTime.UtcNow.Year;
        var policies = await _repo.LeavePolicies.AsNoTracking().OrderBy(p => p.LeaveType).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        ViewBag.Policies = policies;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InitializeYear(int year, List<Guid> employeeIds)
    {
        var policies = await _repo.LeavePolicies.AsNoTracking().OrderBy(p => p.LeaveType).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        if (!policies.Any())
        {
            TempData["Error"] = "لا توجد سياسات إجازات محددة. أضف السياسات أولاً.";
            return RedirectToAction(nameof(Index));
        }

        int count = 0;
        foreach (var empId in employeeIds)
        {
            foreach (var policy in policies)
            {
                var exists = await _repo.LeaveBalances
                    .AnyAsync(b => b.EmployeeId == empId && b.LeaveType == policy.LeaveType && b.Year == year);
                if (exists) continue;

                await _repo.AddAsync(new LeaveBalance
                {
                    EmployeeId = empId,
                    LeaveType = policy.LeaveType,
                    Year = year,
                    TotalEntitlement = policy.MaxDaysPerYear,
                    Used = 0
                }, default);
                count++;
            }
        }

        await _repo.SaveChangesAsync(default);
        TempData["Success"] = $"تم تهيئة {count} سجل رصيد للسنة {year}";
        return RedirectToAction(nameof(Index), new { year });
    }
}
