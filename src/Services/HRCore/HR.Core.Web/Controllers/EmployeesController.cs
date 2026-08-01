using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class EmployeesController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly HR.Core.Infrastructure.Integration.EmployeeIntegrationService _integration;
    private readonly HR.Core.Infrastructure.OrgScopeService _orgScope;
    private readonly IFileReferenceClient _fileRefs;
    private readonly IEmployeeDetailsViewBuilder _detailsBuilder;

    public EmployeesController(
        HRCoreDbContext context,
        HR.Core.Infrastructure.Integration.EmployeeIntegrationService integration,
        HR.Core.Infrastructure.OrgScopeService orgScope,
        IFileReferenceClient fileRefs,
        IEmployeeDetailsViewBuilder detailsBuilder)
    {
        _context = context;
        _integration = integration;
        _orgScope = orgScope;
        _fileRefs = fileRefs;
        _detailsBuilder = detailsBuilder;
    }

    public async Task<IActionResult> Index(EmployeeSearchFilter filter)
    {
        filter.NormalizePaging();

        var query = _context.Employees
            .Include(e => e.PrimaryOrgUnit)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(e =>
                e.FullName.Contains(term) ||
                e.NationalId.Contains(term) ||
                e.FirstName.Contains(term) ||
                e.LastName.Contains(term));
        }

        if (filter.OrgUnitId.HasValue)
            query = query.Where(e => e.PrimaryOrgUnitId == filter.OrgUnitId.Value);

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<EmploymentStatus>(filter.Status, out var status))
            query = query.Where(e => e.CurrentStatus == status);

        if (!string.IsNullOrEmpty(filter.Gender) && Enum.TryParse<Gender>(filter.Gender, out var gender))
            query = query.Where(e => e.Gender == gender);

        if (!string.IsNullOrEmpty(filter.Cadre) && Enum.TryParse<EmployeeCadre>(filter.Cadre, out var cadre))
            query = query.Where(e => e.Cadre == cadre);

        var restrictedCadre = HrCadreVisibility.GetRestrictedCadre(User);
        if (restrictedCadre.HasValue)
            query = query.Where(e => e.Cadre == restrictedCadre.Value);

        var orgScope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (orgScope.IsDenied) return Forbid();
        if (orgScope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && orgScope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        query = filter.SortBy?.ToLower() switch
        {
            "name" => filter.SortDirection == "desc" ? query.OrderByDescending(e => e.FullName) : query.OrderBy(e => e.FullName),
            "date" => filter.SortDirection == "desc" ? query.OrderByDescending(e => e.HireDate) : query.OrderBy(e => e.HireDate),
            "status" => filter.SortDirection == "desc" ? query.OrderByDescending(e => e.CurrentStatus) : query.OrderBy(e => e.CurrentStatus),
            _ => query.OrderByDescending(e => e.CreatedAt)
        };

        var count = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var vm = new EmployeeListViewModel
        {
            Employees = new PaginatedList<Employee>(items, count, filter.Page, filter.PageSize),
            Filter = filter
        };

        var filterUnits = _context.OrgUnits.AsQueryable();
        if (orgScope.MustFilter)
            filterUnits = filterUnits.Where(o => orgScope.AllowedOrgUnitIds.Contains(o.Id));
        ViewBag.OrgUnits = new SelectList(
            await filterUnits.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "NameAr", filter.OrgUnitId);
        ViewBag.CadreRestricted = HrCadreVisibility.GetRestrictedCadre(User);

        return View(vm);
    }

    public async Task<IActionResult> Details(Guid id, string? openModal = null)
    {
        var employee = await _context.Employees
            .Include(e => e.PrimaryOrgUnit)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null) return NotFound();
        if (!await CanAccessEmployeeAsync(employee)) return Forbid();

        var vm = await _detailsBuilder.BuildAsync(id, openModal);
        if (vm == null) return NotFound();

        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create()
    {
        var locked = HrCadreVisibility.GetRestrictedCadre(User);
        var vm = new EmployeeCreateViewModel();
        if (locked.HasValue)
            vm.Cadre = locked.Value;
        await PopulateOrgUnitsAsync(vm);
        ViewBag.CadreLocked = locked;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(EmployeeCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateOrgUnitsAsync(vm);
            ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
            return View(vm);
        }

        var cadre = vm.Cadre;
        if (!CanAssignCadre(cadre))
            cadre = HrCadreVisibility.GetRestrictedCadre(User) ?? EmployeeCadre.Executive;

        if (!await CanAssignOrgUnitAsync(vm.PrimaryOrgUnitId))
        {
            ModelState.AddModelError(nameof(vm.PrimaryOrgUnitId), "الوحدة التنظيمية خارج نطاق صلاحيتك");
            await PopulateOrgUnitsAsync(vm);
            return View(vm);
        }

        var employee = new Employee
        {
            NationalId = vm.NationalId,
            FirstName = vm.FirstName,
            MiddleName = vm.MiddleName,
            LastName = vm.LastName,
            FullName = $"{vm.FirstName} {vm.MiddleName} {vm.LastName}".Replace("  ", " ").Trim(),
            BirthDate = vm.BirthDate,
            Gender = vm.Gender,
            MaritalStatus = vm.MaritalStatus,
            PrimaryOrgUnitId = vm.PrimaryOrgUnitId,
            HireDate = vm.HireDate,
            CurrentStatus = EmploymentStatus.Active,
            Cadre = cadre,
            PhotoFileId = vm.PhotoFileId
        };

        _context.Employees.Add(employee);
        employee = await _integration.CreateEmployeeWithEventAsync(employee);

        if (vm.PhotoFileId.HasValue)
        {
            await _fileRefs.RegisterReferenceAsync(
                vm.PhotoFileId.Value,
                FileReferenceClientExtensions.BuildHolderKey(HrFileServices.HrCore, nameof(Employee), employee.Id, nameof(Employee.PhotoFileId)));
        }

        TempData["Success"] = $"تم إضافة الموظف {employee.FullName} بنجاح";
        return RedirectToAction(nameof(Details), new { id = employee.Id });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();
        if (!await CanAccessEmployeeAsync(employee)) return Forbid();

        var vm = new EmployeeEditViewModel
        {
            Id = employee.Id,
            NationalId = employee.NationalId,
            FirstName = employee.FirstName,
            MiddleName = employee.MiddleName,
            LastName = employee.LastName,
            BirthDate = employee.BirthDate,
            Gender = employee.Gender,
            MaritalStatus = employee.MaritalStatus,
            CurrentStatus = employee.CurrentStatus,
            PrimaryOrgUnitId = employee.PrimaryOrgUnitId,
            HireDate = employee.HireDate,
            PhotoFileId = employee.PhotoFileId,
            Cadre = employee.Cadre
        };

        await PopulateOrgUnitsAsync(vm);
        ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, EmployeeEditViewModel vm, string? secondApproverId)
    {
        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid)
        {
            await PopulateOrgUnitsAsync(vm);
            ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
            return View(vm);
        }

        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();
        if (!await CanAccessEmployeeAsync(employee)) return Forbid();
        if (!await CanAssignOrgUnitAsync(vm.PrimaryOrgUnitId))
        {
            ModelState.AddModelError(nameof(vm.PrimaryOrgUnitId), "الوحدة التنظيمية خارج نطاق صلاحيتك");
            await PopulateOrgUnitsAsync(vm);
            return View(vm);
        }

        var actorId = ParseUserId(User);
        if (actorId is null)
        {
            ModelState.AddModelError(string.Empty, "تعذر تحديد هوية المستخدم من التوكن");
            await PopulateOrgUnitsAsync(vm);
            return View(vm);
        }

        var changedLegalFields = new List<string>();
        if (!string.Equals(employee.NationalId, vm.NationalId, StringComparison.Ordinal))
            changedLegalFields.Add("NationalId");
        if (employee.BirthDate != vm.BirthDate)
            changedLegalFields.Add("DateOfBirth");
        if (employee.HireDate != vm.HireDate)
            changedLegalFields.Add("HireDate");
        if (!string.Equals(employee.FirstName, vm.FirstName, StringComparison.Ordinal)
            || !string.Equals(employee.LastName, vm.LastName, StringComparison.Ordinal))
            changedLegalFields.Add("FullName");

        Guid? secondApprover = Guid.TryParse(secondApproverId, out var sa) ? sa : null;
        var legalCheck = LegalDataChangeGuard.ValidateChange(actorId.Value, secondApprover, changedLegalFields);
        if (!legalCheck.Allowed)
        {
            ModelState.AddModelError(string.Empty, legalCheck.DenialReason ?? "يتطلب اعتماداً مزدوجاً");
            await PopulateOrgUnitsAsync(vm);
            ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
            return View(vm);
        }

        var oldStatus = employee.CurrentStatus;
        var statusChanged = oldStatus != vm.CurrentStatus;
        var oldPhotoFileId = employee.PhotoFileId;

        var newCadre = vm.Cadre;
        if (!CanAssignCadre(newCadre))
            newCadre = employee.Cadre;

        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(
            _fileRefs,
            HrFileServices.HrCore,
            nameof(Employee),
            employee.Id,
            nameof(Employee.PhotoFileId),
            oldPhotoFileId,
            vm.PhotoFileId);

        employee.PhotoFileId = vm.PhotoFileId;
        employee.NationalId = vm.NationalId;
        employee.FirstName = vm.FirstName;
        employee.MiddleName = vm.MiddleName;
        employee.LastName = vm.LastName;
        employee.FullName = $"{vm.FirstName} {vm.MiddleName} {vm.LastName}".Replace("  ", " ").Trim();
        employee.BirthDate = vm.BirthDate;
        employee.Gender = vm.Gender;
        employee.MaritalStatus = vm.MaritalStatus;
        employee.PrimaryOrgUnitId = vm.PrimaryOrgUnitId;
        employee.HireDate = vm.HireDate;
        employee.Cadre = newCadre;

        if (statusChanged)
        {
            var historyBatch = new List<EmploymentStatusHistory>();
            var (ok, statusError) = EmployeeLifecycleService.TryChangeStatus(
                employee,
                vm.CurrentStatus,
                "تحديث من واجهة إدارة الموظفين",
                actorId,
                historyBatch);

            if (!ok)
            {
                ModelState.AddModelError(nameof(vm.CurrentStatus), statusError ?? "تعذر تحديث الحالة.");
                await PopulateOrgUnitsAsync(vm);
                ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
                return View(vm);
            }

            foreach (var entry in historyBatch)
                _context.EmploymentStatusHistories.Add(entry);

            await _integration.PublishStatusChangedIfNeededAsync(
                employee,
                oldStatus,
                vm.CurrentStatus,
                "تحديث من واجهة إدارة الموظفين");
        }
        else
        {
            employee.CurrentStatus = vm.CurrentStatus;
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = $"تم تحديث بيانات الموظف {employee.FullName} بنجاح";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();
        if (!await CanAccessEmployeeAsync(employee)) return Forbid();

        // DATA-002: hard delete is never allowed — use separation, correction, or anonymize.
        return Conflict(new
        {
            error = "لا يُسمح بحذف سجل الموظف؛ استخدم إنهاء الخدمة أو التصحيح."
        });
    }

    private async Task PopulateOrgUnitsAsync(EmployeeCreateViewModel vm)
    {
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        var units = _context.OrgUnits.AsQueryable();
        if (scope.MustFilter)
            units = units.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        vm.OrgUnits = new SelectList(await units.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
    }

    private async Task PopulateOrgUnitsAsync(EmployeeEditViewModel vm)
    {
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        var units = _context.OrgUnits.AsQueryable();
        if (scope.MustFilter)
            units = units.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        vm.OrgUnits = new SelectList(await units.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", vm.PrimaryOrgUnitId);
    }

    private async Task<bool> CanAccessEmployeeAsync(Employee employee)
    {
        if (!CanAccessCadre(employee.Cadre))
            return false;
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        return scope.CanAccess(employee.PrimaryOrgUnitId);
    }

    private async Task<bool> CanAssignOrgUnitAsync(Guid? orgUnitId)
    {
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        return scope.CanAccess(orgUnitId);
    }

    private bool CanAccessCadre(EmployeeCadre cadre)
    {
        var r = HrCadreVisibility.GetRestrictedCadre(User);
        return !r.HasValue || r.Value == cadre;
    }

    private bool CanAssignCadre(EmployeeCadre cadre)
    {
        var r = HrCadreVisibility.GetRestrictedCadre(User);
        return !r.HasValue || r.Value == cadre;
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
