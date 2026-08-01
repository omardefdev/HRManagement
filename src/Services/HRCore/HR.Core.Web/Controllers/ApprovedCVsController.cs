using System.Security.Claims;

using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using HR.Core.Domain.Entities;

using HR.Core.Domain.Services;

using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;

using HR.Core.Web.ViewModels;

using HRManagement.SharedKernel.Enums;



namespace HR.Core.Web.Controllers;



public class ApprovedCVsController : Controller

{

    private readonly HRCoreDbContext _context;

    private readonly IFileReferenceClient _fileRefs;

    private readonly OrgScopeService _orgScope;



    public ApprovedCVsController(HRCoreDbContext context, IFileReferenceClient fileRefs, OrgScopeService orgScope)

    {

        _context = context;

        _fileRefs = fileRefs;

        _orgScope = orgScope;

    }



    public async Task<IActionResult> Index(Guid employeeId)

    {

        var employee = await _context.Employees.FindAsync(employeeId);

        if (employee == null) return NotFound();

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);

        if (this.ForbidIfOrgUnitOutOfScope(scope, employee.PrimaryOrgUnitId) is { } denied)

            return denied;



        var items = await _context.ApprovedCVs

            .Where(c => c.EmployeeId == employeeId)

            .OrderByDescending(c => c.ApprovedAt)

            .Take(SearchFilterBase.MaxPageSize)

            .AsNoTracking()

            .ToListAsync();



        ViewBag.Employee = employee;

        return View(items);

    }



    public async Task<IActionResult> Create(Guid employeeId)

    {

        var employee = await _context.Employees.FindAsync(employeeId);

        if (employee == null) return NotFound();

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);

        if (this.ForbidIfOrgUnitOutOfScope(scope, employee.PrimaryOrgUnitId) is { } denied)

            return denied;



        return View(new ApprovedCvCreateViewModel

        {

            EmployeeId = employeeId,

            EmployeeName = employee.FullName

        });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Create(ApprovedCvCreateViewModel vm)

    {

        if (!ModelState.IsValid) return View(vm);



        var cv = new ApprovedCV

        {

            EmployeeId = vm.EmployeeId,

            FileId = vm.FileId,

            Notes = vm.Notes,

            ApprovedBy = null,

            ApprovedAt = null

        };

        _context.ApprovedCVs.Add(cv);

        await _context.SaveChangesAsync();



        if (vm.FileId.HasValue)

        {

            await _fileRefs.RegisterReferenceAsync(

                vm.FileId.Value,

                FileReferenceClientExtensions.BuildHolderKey(

                    HrFileServices.HrCore, nameof(ApprovedCV), cv.Id, nameof(ApprovedCV.FileId)));

        }



        var userId = ParseUserId(User);

        var userName = User.Identity?.Name ?? "مجهول";

        var approval = ApprovalWorkflowService.CreateApprovalRequest(

            "ApprovedCV",

            cv.Id,

            $"اعتماد سيرة الموظف {vm.EmployeeName}",

            userId,

            userName,

            payloadJson: System.Text.Json.JsonSerializer.Serialize(new { cv.FileId }));

        _context.ApprovalRequests.Add(approval);

        await _context.SaveChangesAsync();



        TempData["Success"] = "تم إرسال السيرة لمسار الاعتماد";

        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> FinalizeApproval(Guid id, Guid approvalRequestId)

    {

        var cv = await _context.ApprovedCVs.FindAsync(id);

        var request = await _context.ApprovalRequests

            .Include(r => r.Steps)

            .FirstOrDefaultAsync(r => r.Id == approvalRequestId && r.EntityType == "ApprovedCV" && r.EntityId == id);

        if (cv is null || request is null) return NotFound();

        if (request.Status != ApprovalStatus.Approved)

        {

            TempData["Error"] = "مسار الاعتماد غير مكتمل";

            return RedirectToAction(nameof(Index), new { employeeId = cv.EmployeeId });

        }



        cv.ApprovedBy = request.ReviewedBy ?? ParseUserId(User);

        cv.ApprovedAt = request.ReviewedAt ?? DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["Success"] = "تم اعتماد السيرة";

        return RedirectToAction(nameof(Index), new { employeeId = cv.EmployeeId });

    }



    public async Task<IActionResult> Edit(Guid id)

    {

        var cv = await _context.ApprovedCVs.Include(c => c.Employee).FirstOrDefaultAsync(c => c.Id == id);

        if (cv == null) return NotFound();



        return View(new ApprovedCvEditViewModel

        {

            Id = cv.Id,

            EmployeeId = cv.EmployeeId,

            EmployeeName = cv.Employee?.FullName,

            FileId = cv.FileId,

            Notes = cv.Notes,

            ApprovedBy = cv.ApprovedBy,

            ApprovedAt = cv.ApprovedAt

        });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Edit(Guid id, ApprovedCvEditViewModel vm)

    {

        if (id != vm.Id) return BadRequest();

        if (!ModelState.IsValid) return View(vm);



        var cv = await _context.ApprovedCVs.FindAsync(id);

        if (cv == null) return NotFound();



        var oldFileId = cv.FileId;

        cv.Notes = vm.Notes;

        cv.FileId = vm.FileId;



        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(

            _fileRefs,

            HrFileServices.HrCore,

            nameof(ApprovedCV),

            cv.Id,

            nameof(ApprovedCV.FileId),

            oldFileId,

            vm.FileId);



        // Approver identity and timestamp belong to the review action, not to a details edit.
        // Copying them from the posted form let anyone rewrite who approved a CV and when.



        await _context.SaveChangesAsync();

        TempData["Success"] = "تم تحديث السيرة المعتمدة";

        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Delete(Guid id)

    {

        var cv = await _context.ApprovedCVs.FindAsync(id);

        if (cv == null) return NotFound();



        var employeeId = cv.EmployeeId;

        await EntityFileReferenceHelper.ReleaseEntityFileAsync(

            _fileRefs,

            HrFileServices.HrCore,

            nameof(ApprovedCV),

            cv.Id,

            nameof(ApprovedCV.FileId),

            cv.FileId);



        _context.ApprovedCVs.Remove(cv);

        await _context.SaveChangesAsync();

        TempData["Success"] = "تم حذف";

        return RedirectToAction(nameof(Index), new { employeeId });

    }



    private static Guid? ParseUserId(ClaimsPrincipal user)

    {

        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        return Guid.TryParse(v, out var g) ? g : null;

    }

}

