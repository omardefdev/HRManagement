using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Web.Controllers;

public class DashboardController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly HR.Core.Infrastructure.OrgScopeService _orgScope;

    public DashboardController(HRCoreDbContext context, HR.Core.Infrastructure.OrgScopeService orgScope)
    {
        _context = context;
        _orgScope = orgScope;
    }

    public async Task<IActionResult> Index()
    {
        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (orgScope.IsDenied) return Forbid();
        var employees = _context.Employees.AsQueryable();
        if (orgScope.MustFilter)
            employees = employees.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        var totalEmployees = await employees.CountAsync();
        var activeEmployees = await employees.CountAsync(e => e.CurrentStatus == EmploymentStatus.Active);

        var orgUnits = _context.OrgUnits.AsQueryable();
        if (orgScope.MustFilter)
            orgUnits = orgUnits.Where(o => orgScope.AllowedOrgUnitIds.Contains(o.Id));

        var vm = new DashboardViewModel
        {
            TotalEmployees = totalEmployees,
            ActiveEmployees = activeEmployees,
            InactiveEmployees = totalEmployees - activeEmployees,
            TotalOrgUnits = await orgUnits.CountAsync(),
            TotalQualifications = await _context.Qualifications.CountAsync(),
            TotalDocuments = await _context.EmployeeDocuments.CountAsync(),

            EmployeesByStatus = await employees
                .GroupBy(e => e.CurrentStatus)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToDictionaryAsync(x => x.Status switch
                {
                    "Active" => "نشط",
                    "Suspended" => "موقوف",
                    "Terminated" => "منتهي",
                    "Retired" => "متقاعد",
                    "OnLeave" => "في إجازة",
                    "Seconded" => "منتدب",
                    _ => x.Status
                }, x => x.Count),

            EmployeesByGender = await employees
                .GroupBy(e => e.Gender)
                .Select(g => new { Gender = g.Key.ToString(), Count = g.Count() })
                .ToDictionaryAsync(x => x.Gender switch
                {
                    "Male" => "ذكر",
                    "Female" => "أنثى",
                    _ => "آخر"
                }, x => x.Count),

            RecentActivities = await employees
                .OrderByDescending(e => e.CreatedAt)
                .Take(10)
                .Select(e => new RecentActivityItem
                {
                    Description = $"تم إضافة الموظف: {e.FullName}",
                    Date = e.CreatedAt,
                    Icon = "fas fa-user-plus",
                    CssClass = "success"
                })
                .ToListAsync()
        };

        var byOrgQuery = _context.Employees.Where(e => e.PrimaryOrgUnit != null);
        if (orgScope.MustFilter)
            byOrgQuery = byOrgQuery.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));
        var orgUnitsWithEmployees = await byOrgQuery
            .GroupBy(e => e.PrimaryOrgUnit!.NameAr)
            .Select(g => new { Unit = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Unit, x => x.Count);
        vm.EmployeesByOrgUnit = orgUnitsWithEmployees;

        return View(vm);
    }
}
