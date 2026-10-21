using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using HR.Core.Domain.Entities;

using HR.Core.Infrastructure.Data;

using HR.Core.Infrastructure.Services.Establishment;

using HR.Core.Web.Services;

using HR.Core.Web.ViewModels;

using HRManagement.ServiceDefaults;



namespace HR.Core.Web.Controllers;



[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]

public class JobCatalogController(

    HRCoreDbContext db,

    EstablishmentScopeService establishmentScope,

    EstablishmentChangeLifecycleService changeLifecycle,

    EstablishmentReferenceValidator referenceValidator,

    IJobCatalogImportService jobCatalogImportService,

    IJobCatalogVersionService jobCatalogVersions) : Controller

{

    public async Task<IActionResult> Index(JobCatalogIndexViewModel filter)

    {

        var accessError = await establishmentScope.ValidateJobCatalogAccessAsync(User);

        if (accessError is not null)

            return Forbid();



        filter.NormalizePaging();



        var query = db.JobCatalogEntries.AsNoTracking().AsQueryable();



        if (filter.IsActive is bool active)

            query = query.Where(e => e.IsActive == active);



        if (!string.IsNullOrWhiteSpace(filter.GradeCode))

        {

            var grade = filter.GradeCode.Trim();

            query = query.Where(e => e.GradeCode == grade);

        }



        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))

        {

            var term = filter.SearchTerm.Trim();

            query = query.Where(e =>

                e.JobCode.Contains(term) ||

                e.TitleAr.Contains(term) ||

                (e.TitleEn != null && e.TitleEn.Contains(term)));

        }



        query = query.OrderBy(e => e.JobCode);



        filter.Items = await query

            .Skip((filter.Page - 1) * filter.PageSize)

            .Take(filter.PageSize)

            .ToListAsync();



        return View(filter);

    }



    public async Task<IActionResult> Details(Guid id, DateTime? asOf)

    {

        var accessError = await establishmentScope.ValidateJobCatalogAccessAsync(User);

        if (accessError is not null)

            return Forbid();



        var entry = await db.JobCatalogEntries

            .AsNoTracking()

            .FirstOrDefaultAsync(e => e.Id == id);

        if (entry is null) return NotFound();



        ViewBag.LinkedPositionCount = await db.FundedPositions

            .AsNoTracking()

            .CountAsync(p => p.JobCatalogEntryId == id);



        ViewBag.ChangeLogs = await db.EstablishmentChangeLogs

            .AsNoTracking()

            .Where(l => l.EntityType == nameof(JobCatalogEntry) && l.EntityId == id)

            .OrderByDescending(l => l.OccurredAt)

            .Take(20)

            .ToListAsync();



        ViewBag.VersionTimeline = await jobCatalogVersions.ListHistoryAsync(id);

        ViewBag.CanMutate = await establishmentScope.ValidateCentralWriteAsync(User) is null;



        if (asOf is DateTime at)

        {

            ViewBag.AsOfDate = at;

            ViewBag.AsOfSnapshot = await jobCatalogVersions.ResolveAsOfAsync(id, at);

        }



        return View(entry);

    }



    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Create()

    {

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        return View(new JobCatalogFormViewModel());

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Create(JobCatalogFormViewModel vm)

    {

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        await ValidateJobCatalogReferencesAsync(vm);



        if (!ModelState.IsValid)

            return View(vm);



        var request = await changeLifecycle.CreateDraftAsync(

            EstablishmentChangeType.CreateJobCatalog,

            ToPayload(vm, includeJobCode: false),

            createdBy: ParseUserId(User));



        TempData["Success"] = "تم حفظ مسودة طلب إنشاء دليل الوظيفة";

        return RedirectToAction("Details", "EstablishmentChanges", new { id = request.Id });

    }



    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Import()

    {

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        return View(new JobCatalogImportViewModel());

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Import(JobCatalogImportViewModel vm)

    {

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        if (vm.File is null || vm.File.Length == 0)

        {

            ModelState.AddModelError(nameof(vm.File), "اختر ملف CSV");

            return View(vm);

        }



        var userId = ParseUserId(User);

        await using var stream = vm.File.OpenReadStream();

        var (_, changeRequest, error) = await jobCatalogImportService.ImportCsvAsync(

            stream,

            vm.File.FileName,

            vm.ImportMode,

            userId);



        if (changeRequest is null)

        {

            TempData["Error"] = error ?? "فشل الاستيراد";

            return View(vm);

        }



        TempData["Success"] = "تم إنشاء مسودة استيراد جماعي لدليل الوظائف";

        return RedirectToAction("Details", "EstablishmentChanges", new { id = changeRequest.Id });

    }



    [Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]

    public IActionResult ImportTemplate()

    {

        const string template = """

            TitleAr,TitleEn,DescriptionAr,GradeCode,JobClassificationCode,IsActive,LegacyJobCode

            مدير إداري,Admin Manager,,GR-05,JC-ADM,true,

            """;

        return File(System.Text.Encoding.UTF8.GetBytes(template), "text/csv", "job-catalog-template.csv");

    }



    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Edit(Guid id)

    {

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        var entry = await db.JobCatalogEntries.FindAsync(id);

        if (entry is null) return NotFound();



        return View(new JobCatalogEditViewModel

        {

            Id = entry.Id,

            JobCode = entry.JobCode,

            TitleAr = entry.TitleAr,

            TitleEn = entry.TitleEn,

            DescriptionAr = entry.DescriptionAr,

            GradeCode = entry.GradeCode,

            OccupationalGroup = entry.OccupationalGroup,

            IsActive = entry.IsActive

        });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Edit(Guid id, JobCatalogEditViewModel vm)

    {

        if (id != vm.Id) return BadRequest();

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        var entry = await db.JobCatalogEntries.FindAsync(id);

        if (entry is null) return NotFound();



        if (string.IsNullOrWhiteSpace(vm.JobCode))

            vm.JobCode = entry.JobCode;



        await ValidateJobCatalogReferencesAsync(vm);



        if (!ModelState.IsValid)

            return View(vm);



        var request = await changeLifecycle.CreateDraftAsync(

            EstablishmentChangeType.UpdateJobCatalog,

            ToPayload(vm, includeJobCode: true),

            targetEntityId: id,

            createdBy: ParseUserId(User));



        TempData["Success"] = "تم حفظ مسودة طلب تعديل دليل الوظيفة";

        return RedirectToAction("Details", "EstablishmentChanges", new { id = request.Id });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]

    public async Task<IActionResult> Deactivate(Guid id)

    {

        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)

            return Forbid();



        var entry = await db.JobCatalogEntries.FindAsync(id);

        if (entry is null) return NotFound();



        var request = await changeLifecycle.CreateDraftAsync(

            EstablishmentChangeType.DeactivateJobCatalog,

            new { },

            targetEntityId: id,

            createdBy: ParseUserId(User));



        TempData["Success"] = "تم حفظ مسودة طلب تعطيل دليل الوظيفة";

        return RedirectToAction("Details", "EstablishmentChanges", new { id = request.Id });

    }



    private async Task ValidateJobCatalogReferencesAsync(JobCatalogFormViewModel vm)

    {

        if (await referenceValidator.ValidateGradeCodeAsync(vm.GradeCode) is { } gradeError)

            ModelState.AddModelError(nameof(vm.GradeCode), gradeError);



        if (await referenceValidator.ValidateJobClassificationAsync(vm.OccupationalGroup) is { } classError)

            ModelState.AddModelError(nameof(vm.OccupationalGroup), classError);

    }



    private static JobCatalogPayload ToPayload(JobCatalogFormViewModel vm, bool includeJobCode) => new()

    {

        JobCode = includeJobCode && !string.IsNullOrWhiteSpace(vm.JobCode) ? vm.JobCode.Trim() : string.Empty,

        TitleAr = vm.TitleAr.Trim(),

        TitleEn = string.IsNullOrWhiteSpace(vm.TitleEn) ? null : vm.TitleEn.Trim(),

        DescriptionAr = string.IsNullOrWhiteSpace(vm.DescriptionAr) ? null : vm.DescriptionAr.Trim(),

        GradeCode = vm.GradeCode.Trim(),

        OccupationalGroup = string.IsNullOrWhiteSpace(vm.OccupationalGroup) ? null : vm.OccupationalGroup.Trim(),

        IsActive = vm.IsActive

    };



    private static Guid? ParseUserId(ClaimsPrincipal user)

    {

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? id : null;

    }

}


