using System.Security.Claims;

using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

using HRManagement.SharedKernel;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using HR.Core.Domain.Entities;

using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;

using HR.Core.Web.Services;

using HR.Core.Web.ViewModels;



namespace HR.Core.Web.Controllers;



public class DocumentsController : Controller

{

    private readonly HRCoreDbContext _context;

    private readonly IEmployeeDetailsViewBuilder _detailsBuilder;

    private readonly IFileReferenceClient _fileRefs;

    private readonly OrgScopeService _orgScope;



    public DocumentsController(

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

            ? RedirectToAction("Details", "Employees", new { id = employeeId, openModal = "document" })

            : RedirectToAction(nameof(Index), new { employeeId, showCreate = true });



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Create(DocumentCreateViewModel vm)

    {

        if (!ModelState.IsValid)

            return await InvalidCreateAsync(vm);



        var document = new EmployeeDocument

        {

            EmployeeId = vm.EmployeeId,

            DocType = vm.DocType,

            DocTitle = vm.DocTitle,

            IssuedBy = ServerAssignedIdentity.ResolveActorName(User),

            IssueDate = vm.IssueDate.AsUtc(),

            ExpiryDate = vm.ExpiryDate.AsUtc(),

            FileId = vm.FileId

        };



        _context.EmployeeDocuments.Add(document);

        await _context.SaveChangesAsync();



        if (vm.FileId.HasValue)

        {

            await _fileRefs.RegisterReferenceAsync(

                vm.FileId.Value,

                FileReferenceClientExtensions.BuildHolderKey(

                    HrFileServices.HrCore, nameof(EmployeeDocument), document.Id, nameof(EmployeeDocument.FileId)));

        }



        TempData["Success"] = "تم إضافة المستند بنجاح";

        return vm.ReturnTo == "details"

            ? RedirectToAction("Details", "Employees", new { id = vm.EmployeeId })

            : RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });

    }



    public async Task<IActionResult> Edit(Guid id)

    {

        var doc = await _context.EmployeeDocuments

            .Include(d => d.Employee)

            .FirstOrDefaultAsync(d => d.Id == id);



        if (doc == null) return NotFound();



        return View(new DocumentEditViewModel

        {

            Id = doc.Id,

            EmployeeId = doc.EmployeeId,

            EmployeeName = doc.Employee?.FullName,

            DocType = doc.DocType,

            DocTitle = doc.DocTitle,

            IssuedBy = doc.IssuedBy,

            IssueDate = doc.IssueDate,

            ExpiryDate = doc.ExpiryDate,

            FileId = doc.FileId,

            Verified = doc.Verified

        });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Edit(Guid id, DocumentEditViewModel vm)

    {

        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid) return View(vm);



        var doc = await _context.EmployeeDocuments.FindAsync(id);

        if (doc == null) return NotFound();



        var oldFileId = doc.FileId;

        doc.DocType = vm.DocType;

        doc.DocTitle = vm.DocTitle;

        doc.IssueDate = vm.IssueDate.AsUtc();

        doc.ExpiryDate = vm.ExpiryDate.AsUtc();

        doc.Verified = vm.Verified;

        doc.FileId = vm.FileId;



        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(

            _fileRefs,

            HrFileServices.HrCore,

            nameof(EmployeeDocument),

            doc.Id,

            nameof(EmployeeDocument.FileId),

            oldFileId,

            vm.FileId);



        await _context.SaveChangesAsync();



        TempData["Success"] = "تم تحديث المستند بنجاح";

        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Delete(Guid id)

    {

        var doc = await _context.EmployeeDocuments.FindAsync(id);

        if (doc == null) return NotFound();



        var employeeId = doc.EmployeeId;

        await EntityFileReferenceHelper.ReleaseEntityFileAsync(

            _fileRefs,

            HrFileServices.HrCore,

            nameof(EmployeeDocument),

            doc.Id,

            nameof(EmployeeDocument.FileId),

            doc.FileId);



        _context.EmployeeDocuments.Remove(doc);

        await _context.SaveChangesAsync();



        TempData["Success"] = "تم حذف المستند بنجاح";

        return RedirectToAction(nameof(Index), new { employeeId });

    }



    private async Task<IActionResult> InvalidCreateAsync(DocumentCreateViewModel vm)

    {

        if (vm.ReturnTo == "details")

        {

            var detailsVm = await _detailsBuilder.BuildAsync(vm.EmployeeId, modalState: new EmployeeDetailsModalState

            {

                DocumentCreate = vm,

                ShowDocumentCreateModal = true

            });

            if (detailsVm == null) return NotFound();

            return View("~/Views/Employees/Details.cshtml", detailsVm);

        }



        return View("Index", await BuildIndexViewModelAsync(vm.EmployeeId, vm, showCreateModal: true));

    }



    private async Task<DocumentsIndexViewModel> BuildIndexViewModelAsync(

        Guid employeeId,

        DocumentCreateViewModel? createModel = null,

        bool showCreateModal = false)

    {

        var employee = await _context.Employees.FindAsync(employeeId)

            ?? throw new InvalidOperationException("Employee not found");



        var documents = await _context.EmployeeDocuments

            .Where(d => d.EmployeeId == employeeId)

            .OrderByDescending(d => d.CreatedAt)

            .Take(SearchFilterBase.MaxPageSize)

            .AsNoTracking()

            .ToListAsync();



        createModel ??= new DocumentCreateViewModel

        {

            EmployeeId = employeeId,

            EmployeeName = employee.FullName

        };

        createModel.EmployeeId = employeeId;

        createModel.EmployeeName = employee.FullName;

        createModel.ReturnTo = "index";



        return new DocumentsIndexViewModel

        {

            Employee = employee,

            Documents = documents,

            CreateModel = createModel,

            ShowCreateModal = showCreateModal

        };

    }

}

