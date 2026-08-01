using HR.Core.Domain.Entities;

using HR.Core.Domain.Enums;

using HR.Core.Domain.Services;

using HR.Core.Infrastructure.Data;

using HR.Core.Infrastructure.Integration;

using HR.Core.Web.ViewModels;

using HRManagement.SharedKernel;
using HRManagement.SharedKernel.ViewModels;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.Rendering;

using Microsoft.EntityFrameworkCore;



namespace HR.Core.Web.Controllers;



public class RecruitmentApplicationsController(

    HRCoreDbContext db,

    RecruitmentIntegrationService integration,

    IConfiguration configuration) : Controller

{

    public async Task<IActionResult> Index(bool showCreate = false) =>

        View(await BuildIndexViewModelAsync(showCreateModal: showCreate));



    public IActionResult Create() =>

        RedirectToAction(nameof(Index), new { showCreate = true });



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Create(RecruitmentApplicationCreateViewModel vm)

    {

        if (!ModelState.IsValid)

        {

            var indexVm = await BuildIndexViewModelAsync(vm, showCreateModal: true);

            return View("Index", indexVm);

        }

        db.RecruitmentApplications.Add(new RecruitmentApplication
        {
            ApplicantFullName = vm.ApplicantFullName,
            NationalId = vm.NationalId,
            AppliedPosition = vm.AppliedPosition,
            Notes = vm.Notes,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Submitted
        });

        await db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل طلب التوظيف";

        return RedirectToAction(nameof(Index));

    }



    public async Task<IActionResult> Hire(Guid id)

    {

        var app = await db.RecruitmentApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();



        if (!RecruitmentWorkflowRules.CanHire(app.Status))

        {

            TempData["Error"] = "يمكن التعيين فقط للطلبات في حالة «القائمة المختصرة».";

            return RedirectToAction(nameof(Index));

        }



        var vm = RecruitmentHireViewModel.FromApplication(app.Id, app.ApplicantFullName, app.AppliedPosition, app.NationalId);

        await PopulateSelectListsAsync(vm);

        return View(vm);

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Hire(RecruitmentHireViewModel vm)

    {

        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == vm.ApplicationId);

        if (app is null) return NotFound();



        if (!RecruitmentWorkflowRules.CanHire(app.Status))

        {

            TempData["Error"] = "يمكن التعيين فقط للطلبات في حالة «القائمة المختصرة».";

            return RedirectToAction(nameof(Index));

        }



        if (vm.Mode == RecruitmentHireMode.LinkExisting)

        {

            if (!vm.ExistingEmployeeId.HasValue)

                ModelState.AddModelError(nameof(vm.ExistingEmployeeId), "يجب اختيار موظف موجود.");

        }

        else

        {

            if (string.IsNullOrWhiteSpace(vm.NationalId))

                ModelState.AddModelError(nameof(vm.NationalId), "الرقم الوطني مطلوب.");

            if (string.IsNullOrWhiteSpace(vm.FirstName))

                ModelState.AddModelError(nameof(vm.FirstName), "الاسم الأول مطلوب.");

            if (string.IsNullOrWhiteSpace(vm.LastName))

                ModelState.AddModelError(nameof(vm.LastName), "اسم العائلة مطلوب.");

        }



        if (!ModelState.IsValid)

        {

            vm.ApplicantFullName = app.ApplicantFullName;

            vm.AppliedPosition = app.AppliedPosition;

            await PopulateSelectListsAsync(vm);

            return View(vm);

        }



        var start = vm.ExpectedStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        Employee employee;



        try

        {

            if (vm.Mode == RecruitmentHireMode.LinkExisting)

            {

                employee = await integration.HireExistingEmployeeAsync(

                    app, vm.ExistingEmployeeId!.Value, start, vm.EmploymentCategory);

            }

            else

            {

                employee = await integration.HireAndCreateEmployeeAsync(

                    app,

                    vm.NationalId.Trim(),

                    vm.FirstName.Trim(),

                    string.IsNullOrWhiteSpace(vm.MiddleName) ? null : vm.MiddleName.Trim(),

                    vm.LastName.Trim(),

                    vm.BirthDate.AsUtc(),

                    vm.Gender,

                    vm.PrimaryOrgUnitId,

                    start,

                    vm.EmploymentCategory);

            }

        }

        catch (InvalidOperationException ex)

        {

            ModelState.AddModelError(string.Empty, ex.Message);

            vm.ApplicantFullName = app.ApplicantFullName;

            vm.AppliedPosition = app.AppliedPosition;

            await PopulateSelectListsAsync(vm);

            return View(vm);

        }



        TempData["Success"] = $"تم تعيين {app.ApplicantFullName} بنجاح. جاري فتح معالج التعيين…";

        var gateway = configuration["HrGateway:PublicBaseUrl"] ?? "http://localhost:5000";

        return Redirect($"{gateway.TrimEnd('/')}/ui/hr-contracts/HireWizard/Continue?employeeId={employee.Id}");

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Obsolete("Use the Hire form instead.")]

    public async Task<IActionResult> HireLegacy(Guid id, Guid? hiredEmployeeId, DateOnly? expectedStartDate, string employmentCategory = "DEFAULT")

    {

        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();



        if (!RecruitmentWorkflowRules.CanHire(app.Status))

        {

            TempData["Error"] = "يمكن التعيين فقط للطلبات في حالة «القائمة المختصرة».";

            return RedirectToAction(nameof(Index));

        }



        if (!hiredEmployeeId.HasValue)

        {

            TempData["Error"] = "يجب ربط الموظف المعيّن بسجل في النواة.";

            return RedirectToAction(nameof(Index));

        }



        var start = expectedStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        await integration.HireExistingEmployeeAsync(app, hiredEmployeeId.Value, start, employmentCategory);



        TempData["Success"] = "تم التعيين وإرسال حدث التوظيف إلى الخدمة والعقود.";

        return RedirectToAction(nameof(Index));

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> SetStatus(Guid id, RecruitmentApplicationStatus status)

    {

        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();



        if (!RecruitmentWorkflowRules.CanTransition(app.Status, status))

        {

            TempData["Error"] = $"لا يمكن الانتقال من {app.Status} إلى {status}.";

            return RedirectToAction(nameof(Index));

        }



        app.Status = status;

        app.ReviewedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        TempData["Success"] = "تم تحديث حالة الطلب";

        return RedirectToAction(nameof(Index));

    }



    private static RecruitmentApplicationCreateViewModel NewCreateModel() => new();

    private async Task<RecruitmentApplicationsIndexViewModel> BuildIndexViewModelAsync(

        RecruitmentApplicationCreateViewModel? createModel = null,

        bool showCreateModal = false)

    {

        var applications = await db.RecruitmentApplications

            .AsNoTracking()

            .Include(a => a.HiredEmployee)

            .OrderByDescending(x => x.AppliedAt)

            .Take(SearchFilterBase.MaxPageSize)

            .ToListAsync();



        return new RecruitmentApplicationsIndexViewModel

        {

            Applications = applications,

            CreateModel = createModel ?? NewCreateModel(),

            ShowCreateModal = showCreateModal

        };

    }



    private async Task PopulateSelectListsAsync(RecruitmentHireViewModel vm)

    {

        var orgUnits = await db.OrgUnits.AsNoTracking().OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();

        vm.OrgUnits = new SelectList(orgUnits, "Id", "NameAr", vm.PrimaryOrgUnitId);



        var employees = await db.Employees.AsNoTracking()

            .OrderBy(e => e.FullName)

            .Take(SearchFilterBase.LookupMaxPageSize)

            .Select(e => new { e.Id, Display = e.FullName + " (" + e.NationalId + ")" })

            .ToListAsync();

        vm.Employees = new SelectList(employees, "Id", "Display", vm.ExistingEmployeeId);

    }

}


