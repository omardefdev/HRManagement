using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Controllers;

/// <summary>
/// Extended personal profile of an employee: nationality, addresses, emergency contact.
/// <para>
/// Every action resolves the employee first and applies the same org-unit and cadre scope that
/// <see cref="EmployeesController"/> applies. Without it this controller was a way around branch
/// isolation: any authenticated user could read or edit the personal file of an employee outside
/// their scope simply by supplying that employee's id.
/// </para>
/// </summary>
public class EmployeeProfilesController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly HR.Core.Infrastructure.OrgScopeService _orgScope;

    public EmployeeProfilesController(
        HRCoreDbContext context,
        HR.Core.Infrastructure.OrgScopeService orgScope)
    {
        _context = context;
        _orgScope = orgScope;
    }

    /// <param name="id">معرّف الموظف</param>
    public async Task<IActionResult> Edit(Guid id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();
        if (!await CanAccessEmployeeAsync(employee)) return Forbid();

        var profile = await _context.EmployeeProfiles.FirstOrDefaultAsync(p => p.EmployeeId == id);
        if (profile == null)
        {
            return View("Create", new EmployeeProfileEditViewModel
            {
                EmployeeId = id,
                EmployeeName = employee.FullName
            });
        }

        return View("Edit", new EmployeeProfileEditViewModel
        {
            Id = profile.Id,
            EmployeeId = profile.EmployeeId,
            EmployeeName = employee.FullName,
            Nationality = profile.Nationality,
            PlaceOfBirth = profile.PlaceOfBirth,
            NationalIdIssuePlace = profile.NationalIdIssuePlace,
            ResidentialAddress = profile.ResidentialAddress,
            PermanentAddress = profile.PermanentAddress,
            EmergencyContact = profile.EmergencyContact,
            Notes = profile.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EmployeeProfileEditViewModel vm)
    {
        var isNew = vm.Id is null || vm.Id == Guid.Empty;
        if (!ModelState.IsValid)
            return View(isNew ? "Create" : "Edit", vm);

        var employee = await _context.Employees.FindAsync(vm.EmployeeId);
        if (employee == null) return NotFound();
        if (!await CanAccessEmployeeAsync(employee)) return Forbid();

        if (isNew)
        {
            if (await _context.EmployeeProfiles.AnyAsync(p => p.EmployeeId == vm.EmployeeId))
            {
                ModelState.AddModelError(string.Empty, "يوجد ملف شخصي لهذا الموظف بالفعل");
                return View("Create", vm);
            }

            _context.EmployeeProfiles.Add(new EmployeeProfile
            {
                EmployeeId = vm.EmployeeId,
                Nationality = vm.Nationality,
                PlaceOfBirth = vm.PlaceOfBirth,
                NationalIdIssuePlace = vm.NationalIdIssuePlace,
                ResidentialAddress = vm.ResidentialAddress,
                PermanentAddress = vm.PermanentAddress,
                EmergencyContact = vm.EmergencyContact,
                Notes = vm.Notes
            });
            await _context.SaveChangesAsync();
            TempData["Success"] = "تم حفظ الملف الشخصي الموسّع";
        }
        else
        {
            var profile = await _context.EmployeeProfiles.FindAsync(vm.Id!.Value);
            if (profile == null) return NotFound();

            // The profile must belong to the employee the caller was authorised against;
            // otherwise a valid EmployeeId could be paired with any profile id.
            if (profile.EmployeeId != vm.EmployeeId) return Forbid();

            profile.Nationality = vm.Nationality;
            profile.PlaceOfBirth = vm.PlaceOfBirth;
            profile.NationalIdIssuePlace = vm.NationalIdIssuePlace;
            profile.ResidentialAddress = vm.ResidentialAddress;
            profile.PermanentAddress = vm.PermanentAddress;
            profile.EmergencyContact = vm.EmergencyContact;
            profile.Notes = vm.Notes;

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تحديث الملف الشخصي الموسّع";
        }

        return RedirectToAction("Details", "Employees", new { id = vm.EmployeeId });
    }

    private async Task<bool> CanAccessEmployeeAsync(Employee employee)
    {
        var restrictedCadre = HrCadreVisibility.GetRestrictedCadre(User);
        if (restrictedCadre.HasValue && restrictedCadre.Value != employee.Cadre)
            return false;

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        return scope.CanAccess(employee.PrimaryOrgUnitId);
    }
}
