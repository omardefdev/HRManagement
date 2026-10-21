using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class RecruitmentRequisitionsController(
    HRCoreDbContext db,
    RecruitmentRequisitionService requisitionService,
    RecruitmentRequisitionLifecycleService lifecycleService,
    RecruitmentRequisitionWorkflowUiService workflowUiService,
    IWorkflowInstanceActionService workflowActions,
    ILegislationRegisterProvider legislationProvider,
    RecruitmentLegalSourceValidator legalSourceValidator,
    ICodeGenerator codeGenerator,
    EstablishmentScopeService establishmentScope,
    IAuthorizationService authorization,
    RecruitmentAssessmentCriteriaService criteriaService,
    ReserveListService reserveList) : Controller
{
    [HttpGet("/api/recruitment/vacant-positions")]
    public async Task<IActionResult> VacantPositions(
        [FromQuery] int? budgetYear,
        [FromQuery] Guid orgUnitId,
        [FromQuery] string? q,
        [FromQuery] Guid? includeId,
        CancellationToken ct)
    {
        if (orgUnitId == Guid.Empty)
            return BadRequest(new { error = "orgUnitId مطلوب." });

        var scopeError = await establishmentScope.ValidateScopeOrgUnitAccessAsync(User, orgUnitId, ct);
        if (scopeError is not null)
            return Forbid();

        var year = budgetYear ?? DateTime.UtcNow.Year;
        var query = await establishmentScope.ApplyScopeOrgUnitFilterAsync(QueryVacantPositions(year), orgUnitId, ct);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                p.PositionCode.Contains(term) ||
                p.GradeCode.Contains(term) ||
                (p.JobCatalogEntry != null && p.JobCatalogEntry.TitleAr.Contains(term)) ||
                (p.OrgUnit != null && p.OrgUnit.NameAr.Contains(term)));
        }

        var items = await query
            .Select(p => new VacantPositionOption(
                p.Id,
                p.PositionCode,
                p.GradeCode,
                p.BudgetYear,
                p.JobCatalogEntry != null ? p.JobCatalogEntry.TitleAr : null,
                p.OrgUnit != null ? p.OrgUnit.NameAr : null))
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync(ct);

        if (includeId is Guid id && items.All(x => x.Id != id))
        {
            var current = await db.FundedPositions
                .AsNoTracking()
                .Include(p => p.OrgUnit)
                .Include(p => p.JobCatalogEntry)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (current is not null)
            {
                items.Insert(0, new VacantPositionOption(
                    current.Id,
                    current.PositionCode,
                    current.GradeCode,
                    current.BudgetYear,
                    current.JobCatalogEntry?.TitleAr,
                    current.OrgUnit?.NameAr));
            }
        }

        return Json(items);
    }

    public async Task<IActionResult> Index(
        RequisitionStatus? status,
        Guid? orgUnitId,
        int? budgetYear,
        string? jobTitle)
    {
        var query = db.RecruitmentRequisitions
            .AsNoTracking()
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.OrgUnit)
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.JobCatalogEntry)
            .Include(r => r.PublicJobPosting)
            .AsQueryable();

        if (status is RequisitionStatus s)
            query = query.Where(r => r.Status == s);
        if (orgUnitId is Guid ou)
            query = query.Where(r => r.FundedPosition != null && r.FundedPosition.OrgUnitId == ou);
        if (budgetYear is int by)
            query = query.Where(r => r.FundedPosition != null && r.FundedPosition.BudgetYear == by);
        if (!string.IsNullOrWhiteSpace(jobTitle))
        {
            var term = jobTitle.Trim();
            query = query.Where(r => r.JobTitle.Contains(term));
        }

        var items = await query
            .OrderByDescending(r => r.RequestedDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        return View(new RecruitmentRequisitionsIndexViewModel
        {
            Items = items,
            Status = status,
            OrgUnitId = orgUnitId,
            BudgetYear = budgetYear,
            JobTitle = jobTitle
        });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(int? budgetYear)
    {
        var vm = new RecruitmentRequisitionCreateViewModel
        {
            BudgetYear = budgetYear ?? DateTime.UtcNow.Year
        };
        await establishmentScope.ApplyDefaultScopeOrgUnitAsync(User, id => vm.ScopeOrgUnitId = id);
        await PopulateBranchScopesAsync(vm.ScopeOrgUnitId != Guid.Empty ? vm.ScopeOrgUnitId : null);
        await PopulateLegalSourcesAsync(vm.LegalSourceId);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(RecruitmentRequisitionCreateViewModel vm)
    {
        var position = await db.FundedPositions
            .Include(p => p.JobCatalogEntry)
            .Include(p => p.OrgUnit)
            .FirstOrDefaultAsync(p => p.Id == vm.FundedPositionId);

        await ValidateRecruitmentScopeAsync(vm, position);

        if (position is null)
        {
            ModelState.AddModelError(nameof(vm.FundedPositionId), "المنصب الممول غير موجود.");
        }
        else
        {
            var hasActive = await HasActiveRequisitionOnPositionAsync(position.Id);
            var error = requisitionService.ValidateCreate(position, hasActive);
            if (error is not null)
                ModelState.AddModelError(string.Empty, error);

            vm.JobTitle = RecruitmentRequisitionService.DefaultJobTitle(position.JobCatalogEntry);
        }

        vm.LegalSourceId = NormalizeLegalSourceId(vm.LegalSourceId);
        var legalError = await legalSourceValidator.ValidateOptionalAsync(vm.LegalSourceId);
        if (legalError is not null)
            ModelState.AddModelError(nameof(vm.LegalSourceId), legalError);

        if (vm.ApplicationClosingDate is DateOnly closing && closing < DateOnly.FromDateTime(DateTime.UtcNow))
            ModelState.AddModelError(nameof(vm.ApplicationClosingDate), "تاريخ إغلاق التقديم يجب ألا يكون في الماضي.");

        if (!ModelState.IsValid)
        {
            await PopulateBranchScopesAsync(vm.ScopeOrgUnitId != Guid.Empty ? vm.ScopeOrgUnitId : null);
            await PopulateLegalSourcesAsync(vm.LegalSourceId);
            return View(vm);
        }

        var codeResult = await codeGenerator.GenerateAsync(
            CodeGenerationRequests.RecruitmentRequisition(position!.BudgetYear));

        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = codeResult.Code,
            FundedPositionId = position.Id,
            JobTitle = RecruitmentRequisitionService.DefaultJobTitle(position!.JobCatalogEntry).Trim(),
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            JustificationNotes = vm.JustificationNotes,
            LegalSourceId = vm.LegalSourceId,
            VacancyCount = 1,
            PublishingChannel = vm.PublishingChannel,
            ApplicationClosingDate = vm.ApplicationClosingDate
        };

        db.RecruitmentRequisitions.Add(requisition);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء طلب الشاغر كمسودة";
        return RedirectToAction(nameof(Details), new { id = requisition.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var requisition = await db.RecruitmentRequisitions
            .AsNoTracking()
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.OrgUnit)
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.JobCatalogEntry)
            .Include(r => r.PublicJobPosting)
            .Include(r => r.InternalVacancy)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(requisition.LegalSourceId))
        {
            var legalSource = await legislationProvider.GetByIdAsync(requisition.LegalSourceId);
            ViewBag.LegalSourceLabel = legalSource?.DisplayLabel ?? requisition.LegalSourceId;
        }
        else
        {
            ViewBag.LegalSourceLabel = "—";
        }

        var vm = await workflowUiService.BuildDetailsAsync(requisition, User);
        vm.CanWrite = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreWrite)).Succeeded;
        vm.CanApprove = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreApprove)).Succeeded;
        var criteriaVersions = await criteriaService.GetVersionsAsync(requisition.Id);
        vm.CriteriaPanel = new AssessmentCriteriaPanelViewModel
        {
            RequisitionId = requisition.Id,
            RequisitionStatus = requisition.Status,
            Versions = criteriaVersions
                .Select(c => new AssessmentCriteriaVersionRow(c, AssessmentCriteriaSerializer.Deserialize(c.CriteriaJson)))
                .ToList(),
            CanWrite = vm.CanWrite,
            CanApprove = vm.CanApprove
        };
        vm.NextCandidate = await reserveList.GetNextCandidateSuggestionAsync(requisition.Id);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveWorkflow(Guid id, RequisitionWorkflowActionViewModel vm)
    {
        if (id != vm.RequisitionId) return BadRequest();

        var requisition = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            requisition.FundedPosition?.OrgUnitId);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (requisition.WorkflowInstanceId is not Guid workflowId)
        {
            TempData["Error"] = "لا يوجد مسار اعتماد مرتبط بهذا الطلب.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await workflowActions.ApproveAsync(
            workflowId,
            CreateWorkflowActor(User),
            vm.Comments,
            targetStepLevel: null);

        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تم اعتماد خطوة مسار طلب الشاغر"
            : result.Error ?? "تعذّر اعتماد الطلب.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectWorkflow(Guid id, RequisitionWorkflowActionViewModel vm)
    {
        if (id != vm.RequisitionId) return BadRequest();

        var requisition = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            requisition.FundedPosition?.OrgUnitId);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (requisition.WorkflowInstanceId is not Guid workflowId)
        {
            TempData["Error"] = "لا يوجد مسار اعتماد مرتبط بهذا الطلب.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await workflowActions.RejectAsync(
            workflowId,
            CreateWorkflowActor(User),
            vm.Comments);

        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تم رفض طلب الشاغر وإرجاعه إلى المسودة"
            : result.Error ?? "تعذّر رفض الطلب.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Submit(Guid id)
    {
        var requisition = await db.RecruitmentRequisitions.FindAsync(id);
        if (requisition is null) return NotFound();

        var (success, error) = await lifecycleService.SubmitForApprovalAsync(
            requisition, ParseUserId(User), User.Identity?.Name);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم تقديم طلب الشاغر للاعتماد";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Publish(Guid id, DateOnly? applicationClosingDate)
    {
        var requisition = await db.RecruitmentRequisitions.FindAsync(id);
        if (requisition is null) return NotFound();

        var (success, error) = await lifecycleService.PublishAsync(
            requisition, ParseUserId(User), User.Identity?.Name, applicationClosingDate: applicationClosingDate);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم نشر الإعلان العام من طلب الشاغر";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> PublishInternal(Guid id, DateOnly? applicationClosingDate)
    {
        var requisition = await db.RecruitmentRequisitions.FindAsync(id);
        if (requisition is null) return NotFound();

        var (success, error) = await lifecycleService.PublishInternalAsync(
            requisition, ParseUserId(User), User.Identity?.Name, applicationClosingDate: applicationClosingDate);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم نشر الشاغر الداخلي من طلب الشاغر";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>HCM-026 (E3): extend the application window (later date + reason); reopens intake if it was closed.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ExtendClosingDate(Guid id, DateOnly? newDate, string? reason)
    {
        if (newDate is null)
        {
            TempData["Error"] = "تاريخ الإغلاق الجديد مطلوب.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var (success, error) = await lifecycleService.ExtendClosingDateAsync(id, newDate.Value, reason, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم تمديد تاريخ إغلاق التقديم" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Close(Guid id)
    {
        var (success, error) = await lifecycleService.CloseAsync(id);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم إغلاق طلب الشاغر وإخفاء الإعلان";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var canApprove = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreApprove)).Succeeded;
        var (success, error) = await lifecycleService.CancelAsync(id, reason, ParseUserId(User), canApprove);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم إلغاء طلب الشاغر";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var requisition = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.OrgUnit)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return NotFound();

        var editError = requisitionService.ValidateEdit(requisition.Status);
        if (editError is not null)
        {
            TempData["Error"] = editError;
            return RedirectToAction(nameof(Details), new { id });
        }

        var scopeOrgUnitId = requisition.FundedPosition?.OrgUnitId is Guid orgUnitId
            ? await establishmentScope.ResolveScopeOrgUnitIdForOrgUnitAsync(orgUnitId)
            : null;

        var vm = new RecruitmentRequisitionEditViewModel
        {
            Id = requisition.Id,
            ScopeOrgUnitId = scopeOrgUnitId ?? Guid.Empty,
            FundedPositionId = requisition.FundedPositionId,
            JobTitle = requisition.JobTitle,
            JustificationNotes = requisition.JustificationNotes,
            LegalSourceId = requisition.LegalSourceId,
            BudgetYear = requisition.FundedPosition?.BudgetYear ?? DateTime.UtcNow.Year,
            ApplicationClosingDate = requisition.ApplicationClosingDate
        };
        await PopulateBranchScopesAsync(vm.ScopeOrgUnitId != Guid.Empty ? vm.ScopeOrgUnitId : null);
        await PopulateLegalSourcesAsync(vm.LegalSourceId);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, RecruitmentRequisitionEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        var requisition = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.OrgUnit)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (requisition is null) return NotFound();

        var editError = requisitionService.ValidateEdit(requisition.Status);
        if (editError is not null)
        {
            TempData["Error"] = editError;
            return RedirectToAction(nameof(Details), new { id });
        }

        var position = await db.FundedPositions
            .Include(p => p.JobCatalogEntry)
            .Include(p => p.OrgUnit)
            .FirstOrDefaultAsync(p => p.Id == vm.FundedPositionId);

        await ValidateRecruitmentScopeAsync(vm, position);

        if (position is null)
        {
            ModelState.AddModelError(nameof(vm.FundedPositionId), "المنصب الممول غير موجود.");
        }
        else if (vm.FundedPositionId != requisition.FundedPositionId)
        {
            var hasActive = await HasActiveRequisitionOnPositionAsync(position.Id, excludeRequisitionId: requisition.Id);
            var createError = requisitionService.ValidateCreate(position, hasActive);
            if (createError is not null)
                ModelState.AddModelError(string.Empty, createError);
        }

        vm.LegalSourceId = NormalizeLegalSourceId(vm.LegalSourceId);
        var legalError = await legalSourceValidator.ValidateOptionalAsync(vm.LegalSourceId);
        if (legalError is not null)
            ModelState.AddModelError(nameof(vm.LegalSourceId), legalError);

        if (vm.ApplicationClosingDate is DateOnly closing && closing < DateOnly.FromDateTime(DateTime.UtcNow))
            ModelState.AddModelError(nameof(vm.ApplicationClosingDate), "تاريخ إغلاق التقديم يجب ألا يكون في الماضي.");

        if (!ModelState.IsValid)
        {
            await PopulateBranchScopesAsync(vm.ScopeOrgUnitId != Guid.Empty ? vm.ScopeOrgUnitId : null);
            await PopulateLegalSourcesAsync(vm.LegalSourceId);
            return View(vm);
        }

        requisition.FundedPositionId = vm.FundedPositionId;
        requisition.JobTitle = RecruitmentRequisitionService.DefaultJobTitle(position!.JobCatalogEntry);
        requisition.JustificationNotes = vm.JustificationNotes;
        requisition.LegalSourceId = vm.LegalSourceId;
        requisition.ApplicationClosingDate = vm.ApplicationClosingDate;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث طلب الشاغر";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var requisition = await db.RecruitmentRequisitions.FindAsync(id);
        if (requisition is null) return NotFound();

        var deleteError = requisitionService.ValidateDelete(requisition.Status);
        if (deleteError is not null)
        {
            TempData["Error"] = deleteError;
            return RedirectToAction(nameof(Details), new { id });
        }

        db.RecruitmentRequisitions.Remove(requisition);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف مسودة طلب الشاغر";
        return RedirectToAction(nameof(Index));
    }

    private IQueryable<FundedPosition> QueryVacantPositions(int budgetYear) =>
        db.FundedPositions
            .AsNoTracking()
            .Include(p => p.OrgUnit)
            .Include(p => p.JobCatalogEntry)
            .Where(p => p.Status == FundedPositionStatus.Vacant && p.BudgetYear == budgetYear)
            .OrderBy(p => p.PositionCode);

    private async Task PopulateBranchScopesAsync(Guid? selectedId = null) =>
        await establishmentScope.PopulateBranchScopesAsync(
            User,
            list => ViewBag.BranchScopes = list,
            locked => ViewBag.BranchScopeLocked = locked,
            selectedId);

    private async Task ValidateRecruitmentScopeAsync(
        RecruitmentRequisitionCreateViewModel vm,
        FundedPosition? position)
    {
        if (vm.ScopeOrgUnitId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(vm.ScopeOrgUnitId), "حدّد الفرع أو المركزية قبل اختيار المنصب.");
            return;
        }

        var accessError = await establishmentScope.ValidateScopeOrgUnitAccessAsync(User, vm.ScopeOrgUnitId);
        if (accessError is not null)
            ModelState.AddModelError(nameof(vm.ScopeOrgUnitId), accessError);

        if (position?.OrgUnit is null)
            return;

        if (!await establishmentScope.IsOrgUnitUnderScopeAsync(position.OrgUnitId, vm.ScopeOrgUnitId))
            ModelState.AddModelError(nameof(vm.FundedPositionId), "المنصب المختار لا ينتمي إلى الفرع/المركزية المحدّدة.");
    }

    private async Task PopulateLegalSourcesAsync(string? selectedId = null)
    {
        var sources = await legislationProvider.ListActiveAsync();
        ViewBag.LegalSources = new SelectList(
            sources.Select(s => new { s.LegalSourceId, s.DisplayLabel }),
            "LegalSourceId",
            "DisplayLabel",
            selectedId);
    }

    private static string? NormalizeLegalSourceId(string? legalSourceId) =>
        string.IsNullOrWhiteSpace(legalSourceId) ? null : legalSourceId.Trim();

    private Task<bool> HasActiveRequisitionOnPositionAsync(
        Guid fundedPositionId,
        Guid? excludeRequisitionId = null) =>
        db.RecruitmentRequisitions
            .Where(r => r.FundedPositionId == fundedPositionId
                && RequisitionWorkflowRules.BlockingStatuses.Contains(r.Status)
                && (excludeRequisitionId == null || r.Id != excludeRequisitionId))
            .AnyAsync();

    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user)
    {
        var userId = ParseUserId(user);
        var userName = user.FindFirstValue("preferred_username") ?? user.Identity?.Name;
        var actorRole = user.FindFirstValue(ClaimTypes.Role) ?? user.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        return new WorkflowActionActor(userId, userName, actorRole);
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
