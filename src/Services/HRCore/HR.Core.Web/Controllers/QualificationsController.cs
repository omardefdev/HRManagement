using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

using HRManagement.SharedKernel;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using HR.Core.Domain.Entities;

using HR.Core.Domain.Services;

using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;

using HR.Core.Web.Services;

using HR.Core.Web.ViewModels;

using HRManagement.SharedKernel.Enums;



namespace HR.Core.Web.Controllers;



public class QualificationsController : Controller

{

    private readonly HRCoreDbContext _context;

    private readonly IEmployeeDetailsViewBuilder _detailsBuilder;

    private readonly IFileReferenceClient _fileRefs;

    private readonly OrgScopeService _orgScope;



    public QualificationsController(

        HRCoreDbContext context,

        IEmployeeDetailsViewBuilder detailsBuilder,

        IFileReferenceClient fileRefs,

        OrgScopeService orgScope)

    {

        _context = context;

        _detailsBuilder = detailsBuilder;

        _fileRefs = fileRefs;

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

            ? RedirectToAction("Details", "Employees", new { id = employeeId, openModal = "qualification" })

            : RedirectToAction(nameof(Index), new { employeeId, showCreate = true });



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Create(QualificationCreateViewModel vm)

    {

        if (!ModelState.IsValid)

            return await InvalidCreateAsync(vm);



        var qualification = new Qualification

        {

            EmployeeId = vm.EmployeeId,

            QualificationCode = vm.QualificationCode,

            Title = vm.Title,

            Provider = vm.Provider,

            CompletionDate = vm.CompletionDate.AsUtc(),

            FileId = vm.FileId

        };



        _context.Qualifications.Add(qualification);

        await _context.SaveChangesAsync();



        if (vm.FileId.HasValue)

        {

            await _fileRefs.RegisterReferenceAsync(

                vm.FileId.Value,

                FileReferenceClientExtensions.BuildHolderKey(

                    HrFileServices.HrCore, nameof(Qualification), qualification.Id, nameof(Qualification.FileId)));

        }



        TempData["Success"] = "تم إضافة المؤهل بنجاح";

        return vm.ReturnTo == "details"

            ? RedirectToAction("Details", "Employees", new { id = vm.EmployeeId })

            : RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });

    }



    public async Task<IActionResult> Edit(Guid id)

    {

        var qualification = await _context.Qualifications

            .Include(q => q.Employee)

            .FirstOrDefaultAsync(q => q.Id == id);



        if (qualification == null) return NotFound();



        return View(new QualificationEditViewModel

        {

            Id = qualification.Id,

            EmployeeId = qualification.EmployeeId,

            EmployeeName = qualification.Employee?.FullName,

            QualificationCode = qualification.QualificationCode,

            Title = qualification.Title,

            Provider = qualification.Provider,

            CompletionDate = qualification.CompletionDate,

            FileId = qualification.FileId

        });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Edit(Guid id, QualificationEditViewModel vm)

    {

        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid) return View(vm);



        var qualification = await _context.Qualifications.FindAsync(id);

        if (qualification == null) return NotFound();



        var oldFileId = qualification.FileId;

        qualification.QualificationCode = vm.QualificationCode;

        qualification.Title = vm.Title;

        qualification.Provider = vm.Provider;

        qualification.CompletionDate = vm.CompletionDate.AsUtc();

        qualification.FileId = vm.FileId;



        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(

            _fileRefs,

            HrFileServices.HrCore,

            nameof(Qualification),

            qualification.Id,

            nameof(Qualification.FileId),

            oldFileId,

            vm.FileId);



        await _context.SaveChangesAsync();



        TempData["Success"] = "تم تحديث المؤهل بنجاح";

        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Delete(Guid id)

    {

        var qualification = await _context.Qualifications.FindAsync(id);

        if (qualification == null) return NotFound();



        var employeeId = qualification.EmployeeId;

        await EntityFileReferenceHelper.ReleaseEntityFileAsync(

            _fileRefs,

            HrFileServices.HrCore,

            nameof(Qualification),

            qualification.Id,

            nameof(Qualification.FileId),

            qualification.FileId);



        _context.Qualifications.Remove(qualification);

        await _context.SaveChangesAsync();



        TempData["Success"] = "تم حذف المؤهل بنجاح";

        return RedirectToAction(nameof(Index), new { employeeId });

    }



    private async Task<IActionResult> InvalidCreateAsync(QualificationCreateViewModel vm)

    {

        if (vm.ReturnTo == "details")

        {

            var detailsVm = await _detailsBuilder.BuildAsync(vm.EmployeeId, modalState: new EmployeeDetailsModalState

            {

                QualificationCreate = vm,

                ShowQualificationCreateModal = true

            });

            if (detailsVm == null) return NotFound();

            return View("~/Views/Employees/Details.cshtml", detailsVm);

        }



        return View("Index", await BuildIndexViewModelAsync(vm.EmployeeId, vm, showCreateModal: true));

    }



    private async Task<QualificationsIndexViewModel> BuildIndexViewModelAsync(

        Guid employeeId,

        QualificationCreateViewModel? createModel = null,

        bool showCreateModal = false)

    {

        var employee = await _context.Employees.FindAsync(employeeId)

            ?? throw new InvalidOperationException("Employee not found");



        var qualifications = await _context.Qualifications

            .Where(q => q.EmployeeId == employeeId)

            .OrderByDescending(q => q.CompletionDate)

            .Take(SearchFilterBase.MaxPageSize)

            .AsNoTracking()

            .ToListAsync();



        createModel ??= new QualificationCreateViewModel

        {

            EmployeeId = employeeId,

            EmployeeName = employee.FullName

        };

        createModel.EmployeeId = employeeId;

        createModel.EmployeeName = employee.FullName;

        createModel.ReturnTo = "index";



        return new QualificationsIndexViewModel

        {

            Employee = employee,

            Qualifications = qualifications,

            CreateModel = createModel,

            ShowCreateModal = showCreateModal

        };

    }

}

