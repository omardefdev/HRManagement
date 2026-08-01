using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class EmploymentRecordsController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly OrgScopeService _orgScope;

    public EmploymentRecordsController(HRCoreDbContext context, OrgScopeService orgScope)
    {
        _context = context;
        _orgScope = orgScope;
    }

    public async Task<IActionResult> Index(Guid employeeId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound();

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(scope, employee.PrimaryOrgUnitId) is { } denied)
            return denied;

        var records = await _context.EmploymentRecords
            .Include(r => r.OrgUnit)
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();

        ViewBag.Employee = employee;
        return View(records);
    }

    private async Task PopulateOrgUnitsAsync(EmploymentRecordCreateViewModel vm)
    {
        vm.OrgUnits = new SelectList(
            await _context.OrgUnits.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", vm.OrgUnitId);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(Guid employeeId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound();

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(scope, employee.PrimaryOrgUnitId) is { } denied)
            return denied;

        var vm = new EmploymentRecordCreateViewModel { EmployeeId = employeeId, EmployeeName = employee.FullName };
        await PopulateOrgUnitsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(EmploymentRecordCreateViewModel vm)
    {
        await PopulateOrgUnitsAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        var record = new EmploymentRecord
        {
            EmployeeId = vm.EmployeeId,
            PositionTitle = vm.PositionTitle,
            PositionCode = vm.PositionCode,
            OrgUnitId = vm.OrgUnitId,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            EmploymentType = vm.EmploymentType,
            Status = vm.Status,
            Notes = vm.Notes
        };
        _context.EmploymentRecords.Add(record);
        await _context.SaveChangesAsync();

        _context.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = record.EmployeeId,
            NewStatus = record.Status,
            EmploymentRecordId = record.Id,
            ChangedAt = DateTime.UtcNow,
            ChangedBy = ParseUserId(User),
            Reason = "إنشاء سجل توظيف"
        });
        await _context.SaveChangesAsync();

        TempData["Success"] = "تم إضافة السجل الوظيفي";
        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var record = await _context.EmploymentRecords.Include(r => r.Employee).FirstOrDefaultAsync(r => r.Id == id);
        if (record == null) return NotFound();

        var vm = new EmploymentRecordEditViewModel
        {
            Id = record.Id,
            EmployeeId = record.EmployeeId,
            EmployeeName = record.Employee?.FullName,
            PositionTitle = record.PositionTitle,
            PositionCode = record.PositionCode,
            OrgUnitId = record.OrgUnitId,
            StartDate = record.StartDate,
            EndDate = record.EndDate,
            EmploymentType = record.EmploymentType,
            Status = record.Status,
            Notes = record.Notes
        };
        await PopulateOrgUnitsAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, EmploymentRecordEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        await PopulateOrgUnitsAsync(vm);
        if (!ModelState.IsValid) return View(vm);

        var record = await _context.EmploymentRecords.FindAsync(id);
        if (record == null) return NotFound();

        var oldStatus = record.Status;

        record.PositionTitle = vm.PositionTitle;
        record.PositionCode = vm.PositionCode;
        record.OrgUnitId = vm.OrgUnitId;
        record.StartDate = vm.StartDate;
        record.EndDate = vm.EndDate;
        record.EmploymentType = vm.EmploymentType;
        record.Status = vm.Status;
        record.Notes = vm.Notes;

        if (!string.Equals(oldStatus, vm.Status, StringComparison.Ordinal))
        {
            _context.EmploymentStatusHistories.Add(new EmploymentStatusHistory
            {
                EmployeeId = record.EmployeeId,
                OldStatus = oldStatus,
                NewStatus = vm.Status,
                EmploymentRecordId = record.Id,
                ChangedAt = DateTime.UtcNow,
                ChangedBy = ParseUserId(User),
                Reason = "تحديث حالة سجل التوظيف"
            });
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = "تم تحديث السجل الوظيفي";
        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var record = await _context.EmploymentRecords.FindAsync(id);
        if (record == null) return NotFound();
        if (record.ApprovedBy != null)
        {
            TempData["Error"] = "السجل معتمد مسبقاً";
            return RedirectToAction(nameof(Index), new { employeeId = record.EmployeeId });
        }

        var by = ParseUserId(User);
        if (by == null)
        {
            TempData["Error"] = "تعذر تحديد المعتمد من التوكن";
            return RedirectToAction(nameof(Index), new { employeeId = record.EmployeeId });
        }

        if (record.CreatedBy.HasValue && by.Value == record.CreatedBy.Value)
        {
            TempData["Error"] = "لا يمكن للمنشئ اعتماد عمله الخاص.";
            return RedirectToAction(nameof(Index), new { employeeId = record.EmployeeId });
        }

        record.ApprovedBy = by;
        await _context.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد السجل الوظيفي في السيرة المركزية";
        return RedirectToAction(nameof(Index), new { employeeId = record.EmployeeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var record = await _context.EmploymentRecords.FindAsync(id);
        if (record == null) return NotFound();

        var employeeId = record.EmployeeId;
        _context.EmploymentRecords.Remove(record);
        await _context.SaveChangesAsync();
        TempData["Success"] = "تم حذف السجل الوظيفي";
        return RedirectToAction(nameof(Index), new { employeeId });
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
