using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

public class ContactsController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly IEmployeeDetailsViewBuilder _detailsBuilder;
    private readonly OrgScopeService _orgScope;

    public ContactsController(
        HRCoreDbContext context,
        IEmployeeDetailsViewBuilder detailsBuilder,
        OrgScopeService orgScope)
    {
        _context = context;
        _detailsBuilder = detailsBuilder;
        _orgScope = orgScope;
    }

    public async Task<IActionResult> Index(Guid employeeId, bool showCreate = false)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee is null)
            return NotFound();

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgUnitOutOfScope(scope, employee.PrimaryOrgUnitId) is { } denied)
            return denied;

        return View(await BuildIndexViewModelAsync(employeeId, showCreateModal: showCreate));
    }

    public IActionResult Create(Guid employeeId, string? returnTo = null) =>
        returnTo == "details"
            ? RedirectToAction("Details", "Employees", new { id = employeeId, openModal = "contact" })
            : RedirectToAction(nameof(Index), new { employeeId, showCreate = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContactCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            if (vm.ReturnTo == "details")
            {
                var detailsVm = await _detailsBuilder.BuildAsync(vm.EmployeeId, modalState: new EmployeeDetailsModalState
                {
                    ContactCreate = vm,
                    ShowContactCreateModal = true
                });
                if (detailsVm == null) return NotFound();
                return View("~/Views/Employees/Details.cshtml", detailsVm);
            }

            return View("Index", await BuildIndexViewModelAsync(vm.EmployeeId, vm, showCreateModal: true));
        }

        var contact = new ContactInfo
        {
            EmployeeId = vm.EmployeeId,
            Type = vm.Type,
            Value = vm.Value,
            IsPrimary = vm.IsPrimary
        };

        _context.ContactInfos.Add(contact);
        await _context.SaveChangesAsync();

        TempData["Success"] = "تم إضافة معلومات الاتصال بنجاح";
        return vm.ReturnTo == "details"
            ? RedirectToAction("Details", "Employees", new { id = vm.EmployeeId })
            : RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var contact = await _context.ContactInfos
            .Include(c => c.Employee)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contact == null) return NotFound();

        return View(new ContactEditViewModel
        {
            Id = contact.Id,
            EmployeeId = contact.EmployeeId,
            EmployeeName = contact.Employee?.FullName,
            Type = contact.Type,
            Value = contact.Value,
            IsPrimary = contact.IsPrimary
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ContactEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) return View(vm);

        var contact = await _context.ContactInfos.FindAsync(id);
        if (contact == null) return NotFound();

        contact.Type = vm.Type;
        contact.Value = vm.Value;
        contact.IsPrimary = vm.IsPrimary;

        await _context.SaveChangesAsync();

        TempData["Success"] = "تم تحديث معلومات الاتصال بنجاح";
        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var contact = await _context.ContactInfos.FindAsync(id);
        if (contact == null) return NotFound();

        var employeeId = contact.EmployeeId;
        _context.ContactInfos.Remove(contact);
        await _context.SaveChangesAsync();

        TempData["Success"] = "تم حذف معلومات الاتصال بنجاح";
        return RedirectToAction(nameof(Index), new { employeeId });
    }

    private async Task<ContactsIndexViewModel> BuildIndexViewModelAsync(
        Guid employeeId,
        ContactCreateViewModel? createModel = null,
        bool showCreateModal = false)
    {
        var employee = await _context.Employees.FindAsync(employeeId)
            ?? throw new InvalidOperationException("Employee not found");

        var contacts = await _context.ContactInfos
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Type)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();

        createModel ??= new ContactCreateViewModel
        {
            EmployeeId = employeeId,
            EmployeeName = employee.FullName
        };
        createModel.EmployeeId = employeeId;
        createModel.EmployeeName = employee.FullName;
        createModel.ReturnTo = "index";

        return new ContactsIndexViewModel
        {
            Employee = employee,
            Contacts = contacts,
            CreateModel = createModel,
            ShowCreateModal = showCreateModal
        };
    }
}
