using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class RostersController(HRCoreDbContext db) : Controller
{
    private const decimal IndicativeHourlyWage = 100m;

    private async Task PopulateLookups()
    {
        ViewBag.OrgUnits = new SelectList(
            await db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        ViewBag.Shifts = new SelectList(
            await db.Shifts.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        ViewBag.Employees = new SelectList(
            await db.Employees.AsNoTracking().OrderBy(e => e.FullName).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "FullName");
    }

    public async Task<IActionResult> Index(Guid? orgUnitId, DateOnly? periodFrom, DateOnly? periodTo, string? q)
    {
        var query = db.ShiftRosters.AsNoTracking().Include(r => r.OrgUnit).AsQueryable();
        if (orgUnitId is Guid ou)
            query = query.Where(r => r.OrgUnitId == ou);
        if (periodFrom is DateOnly pf)
            query = query.Where(r => r.PeriodEnd >= pf);
        if (periodTo is DateOnly pt)
            query = query.Where(r => r.PeriodStart <= pt);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(r => r.Title.Contains(term) || (r.Notes != null && r.Notes.Contains(term)));
        }

        var items = await query.OrderByDescending(r => r.PeriodStart).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.OrgUnits = new SelectList(
            await db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", orgUnitId);
        return View(new RostersIndexViewModel { Items = items, OrgUnitId = orgUnitId, PeriodFrom = periodFrom, PeriodTo = periodTo, Q = q });
    }

    public async Task<IActionResult> Create()
    {
        await PopulateLookups();
        return View(new ShiftRosterCreateViewModel
        {
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShiftRosterCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookups();
            return View(vm);
        }
        var model = new ShiftRoster
        {
            Title = vm.Title,
            OrgUnitId = vm.OrgUnitId,
            PeriodStart = vm.PeriodStart,
            PeriodEnd = vm.PeriodEnd,
            Notes = vm.Notes
        };
        db.ShiftRosters.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء جدول المناوبات";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var roster = await db.ShiftRosters
            .Include(r => r.OrgUnit)
            .Include(r => r.Assignments).ThenInclude(a => a.Shift)
            .Include(r => r.Assignments).ThenInclude(a => a.Employee)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (roster == null) return NotFound();

        var breakdown = roster.Assignments
            .Where(a => a.Shift != null)
            .ToDictionary(
                a => a.Id,
                a => ShiftAllowanceRules.Compute(
                    a.Shift!.ShiftType, a.Shift.StartTime, a.Shift.EndTime, a.OvertimeHours, IndicativeHourlyWage));

        ViewBag.Allowances = breakdown;
        ViewBag.IndicativeHourlyWage = IndicativeHourlyWage;
        ViewBag.TotalAllowance = breakdown.Values.Sum(b => b.TotalAllowance);
        await PopulateLookups();
        return View(roster);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAssignment(Guid rosterId, Guid shiftId, Guid employeeId, DateOnly workDate, decimal overtimeHours)
    {
        var roster = await db.ShiftRosters.FindAsync(rosterId);
        if (roster == null) return NotFound();

        if (workDate < roster.PeriodStart || workDate > roster.PeriodEnd)
        {
            TempData["Error"] = "تاريخ العمل خارج فترة الجدول";
            return RedirectToAction(nameof(Details), new { id = rosterId });
        }

        db.ShiftAssignments.Add(new ShiftAssignment
        {
            ShiftRosterId = rosterId,
            ShiftId = shiftId,
            EmployeeId = employeeId,
            WorkDate = workDate,
            OvertimeHours = overtimeHours < 0 ? 0 : overtimeHours
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إسناد الوردية";
        return RedirectToAction(nameof(Details), new { id = rosterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAssignment(Guid id, Guid rosterId)
    {
        var assignment = await db.ShiftAssignments.FindAsync(id);
        if (assignment != null)
        {
            db.ShiftAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم حذف الإسناد";
        }
        return RedirectToAction(nameof(Details), new { id = rosterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var roster = await db.ShiftRosters.FindAsync(id);
        if (roster == null) return NotFound();
        db.ShiftRosters.Remove(roster);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الجدول";
        return RedirectToAction(nameof(Index));
    }
}
