using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Tenancy;
using HRManagement.SharedKernel;
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
    private readonly TenantQuarantineGuard _quarantineGuard;

    public EmployeesController(
        HRCoreDbContext context,
        HR.Core.Infrastructure.Integration.EmployeeIntegrationService integration,
        HR.Core.Infrastructure.OrgScopeService orgScope,
        IFileReferenceClient fileRefs,
        IEmployeeDetailsViewBuilder detailsBuilder,
        TenantQuarantineGuard quarantineGuard)
    {
        _context = context;
        _integration = integration;
        _orgScope = orgScope;
        _fileRefs = fileRefs;
        _detailsBuilder = detailsBuilder;
        _quarantineGuard = quarantineGuard;
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
            if (NationalIdSearch.LooksLikeNationalId(term))
            {
                var hash = NationalIdSearch.ComputeHash(term);
                query = query.Where(e => e.NationalIdHash == hash);
            }
            else
            {
                query = query.Where(e =>
                    e.FullName.Contains(term) ||
                    e.FirstName.Contains(term) ||
                    e.LastName.Contains(term));
            }
        }

        if (filter.OrgUnitId.HasValue)
        {
            var selectedPath = await _context.OrgUnits.AsNoTracking()
                .Where(o => o.Id == filter.OrgUnitId.Value)
                .Select(o => o.MaterializedPath)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                var descendantIds = _context.OrgUnits.AsNoTracking()
                    .Where(o => o.MaterializedPath.StartsWith(selectedPath))
                    .Select(o => o.Id);
                query = query.Where(e => e.PrimaryOrgUnitId.HasValue
                    && descendantIds.Contains(e.PrimaryOrgUnitId.Value));
            }
            else
            {
                query = query.Where(e => e.PrimaryOrgUnitId == filter.OrgUnitId.Value);
            }
        }

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
        if (this.ForbidIfOrgScopeDenied(orgScope) is { } scopeDenied)
            return scopeDenied;
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

        var filterUnits = _context.OrgUnits.AsNoTracking().AsQueryable();
        if (orgScope.MustFilter)
            filterUnits = filterUnits.Where(o => orgScope.AllowedOrgUnitIds.Contains(o.Id));
        var filterUnitRows = await filterUnits.ToListAsync();
        ViewBag.OrgUnits = new SelectList(
            OrgUnitDisplayService.BuildOptions(filterUnitRows, filter.OrgUnitId),
            "Value", "Text", filter.OrgUnitId?.ToString());
        ViewBag.OrgUnitLabels = OrgUnitDisplayService.BuildLabels(filterUnitRows);
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

        vm.DisplayNationalId = EmployeeNationalIdPresentation.ResolveDisplayNationalId(vm.Employee, User);
        vm.CanViewFullNationalId = EmployeeNationalIdPresentation.CanViewFullNationalId(User);

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
    public async Task<IActionResult> Edit(Guid id, EmployeeEditViewModel vm)
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

        try
        {
            await _quarantineGuard.ThrowIfQuarantinedAsync("Employees", employee.Id);
        }
        catch (QuarantinedRecordException ex)
        {
            return Conflict(new { errorCode = QuarantinedRecordException.ErrorCode, error = ex.Message });
        }

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
            changedLegalFields.Add(nameof(Employee.BirthDate));
        if (employee.HireDate != vm.HireDate)
            changedLegalFields.Add(nameof(Employee.HireDate));
        if (!string.Equals(employee.FirstName, vm.FirstName, StringComparison.Ordinal))
            changedLegalFields.Add(nameof(Employee.FirstName));
        if (!string.Equals(employee.MiddleName, vm.MiddleName, StringComparison.Ordinal))
            changedLegalFields.Add(nameof(Employee.MiddleName));
        if (!string.Equals(employee.LastName, vm.LastName, StringComparison.Ordinal))
            changedLegalFields.Add(nameof(Employee.LastName));

        if (changedLegalFields.Count > 0 && string.IsNullOrWhiteSpace(vm.CorrectionReason))
        {
            ModelState.AddModelError(nameof(vm.CorrectionReason), "سبب التصحيح مطلوب عند تعديل البيانات القانونية.");
            await PopulateOrgUnitsAsync(vm);
            ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
            return View(vm);
        }

        if (changedLegalFields.Count > 0)
        {
            var proposed = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (changedLegalFields.Contains(nameof(Employee.NationalId), StringComparer.OrdinalIgnoreCase))
                proposed[nameof(Employee.NationalId)] = JsonSerializer.SerializeToElement(vm.NationalId);
            if (changedLegalFields.Contains(nameof(Employee.FirstName), StringComparer.OrdinalIgnoreCase))
                proposed[nameof(Employee.FirstName)] = JsonSerializer.SerializeToElement(vm.FirstName);
            if (changedLegalFields.Contains(nameof(Employee.MiddleName), StringComparer.OrdinalIgnoreCase))
                proposed[nameof(Employee.MiddleName)] = JsonSerializer.SerializeToElement(vm.MiddleName);
            if (changedLegalFields.Contains(nameof(Employee.LastName), StringComparer.OrdinalIgnoreCase))
                proposed[nameof(Employee.LastName)] = JsonSerializer.SerializeToElement(vm.LastName);
            if (changedLegalFields.Contains(nameof(Employee.BirthDate), StringComparer.OrdinalIgnoreCase))
                proposed[nameof(Employee.BirthDate)] = JsonSerializer.SerializeToElement(vm.BirthDate);
            if (changedLegalFields.Contains(nameof(Employee.HireDate), StringComparer.OrdinalIgnoreCase))
                proposed[nameof(Employee.HireDate)] = JsonSerializer.SerializeToElement(vm.HireDate);

            var (correction, correctionError) = EmployeeCorrectionService.CreateRequest(
                employee, proposed, vm.CorrectionReason, actorId.Value);
            if (correction is null)
            {
                ModelState.AddModelError(string.Empty, correctionError ?? "تعذر إنشاء طلب التصحيح.");
                await PopulateOrgUnitsAsync(vm);
                ViewBag.CadreLocked = HrCadreVisibility.GetRestrictedCadre(User);
                return View(vm);
            }

            _context.EmployeeCorrections.Add(correction);
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
        if (!changedLegalFields.Contains(nameof(Employee.NationalId), StringComparer.OrdinalIgnoreCase))
            employee.NationalId = vm.NationalId;
        if (!changedLegalFields.Contains(nameof(Employee.FirstName), StringComparer.OrdinalIgnoreCase))
            employee.FirstName = vm.FirstName;
        if (!changedLegalFields.Contains(nameof(Employee.MiddleName), StringComparer.OrdinalIgnoreCase))
            employee.MiddleName = vm.MiddleName;
        if (!changedLegalFields.Contains(nameof(Employee.LastName), StringComparer.OrdinalIgnoreCase))
            employee.LastName = vm.LastName;
        if (changedLegalFields.Count == 0)
            employee.FullName = $"{vm.FirstName} {vm.MiddleName} {vm.LastName}".Replace("  ", " ").Trim();
        if (!changedLegalFields.Contains(nameof(Employee.BirthDate), StringComparer.OrdinalIgnoreCase))
            employee.BirthDate = vm.BirthDate;
        employee.Gender = vm.Gender;
        employee.MaritalStatus = vm.MaritalStatus;
        employee.PrimaryOrgUnitId = vm.PrimaryOrgUnitId;
        if (!changedLegalFields.Contains(nameof(Employee.HireDate), StringComparer.OrdinalIgnoreCase))
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

        TempData["Success"] = changedLegalFields.Count > 0
            ? $"تم حفظ التغييرات غير القانونية وإنشاء طلب تصحيح قانوني للموظف {employee.FullName} بانتظار الاعتماد."
            : $"تم تحديث بيانات الموظف {employee.FullName} بنجاح";
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

        try
        {
            await _quarantineGuard.ThrowIfQuarantinedAsync("Employees", employee.Id);
        }
        catch (QuarantinedRecordException ex)
        {
            return Conflict(new { errorCode = QuarantinedRecordException.ErrorCode, error = ex.Message });
        }

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
        vm.OrgUnits = new SelectList(
            OrgUnitDisplayService.BuildOptions(await units.ToListAsync()),
            "Value", "Text");
    }

    private async Task PopulateOrgUnitsAsync(EmployeeEditViewModel vm)
    {
        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        var units = _context.OrgUnits.AsQueryable();
        if (scope.MustFilter)
            units = units.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        vm.OrgUnits = new SelectList(
            OrgUnitDisplayService.BuildOptions(await units.ToListAsync(), vm.PrimaryOrgUnitId),
            "Value", "Text", vm.PrimaryOrgUnitId?.ToString());
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
