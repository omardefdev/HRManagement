using System.Security.Claims;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31a): HR screens of multi-vacancy competitions — read with <c>HrCoreReadOnly</c>, change
/// with <c>HrCoreWrite</c>, approve through the workflow (<c>RECRUITMENT-COMPETITION-APPROVAL</c>).
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public sealed class CompetitionsController(
    HRCoreDbContext db,
    CompetitionLifecycleService lifecycle,
    IWorkflowInstanceActionService workflowActions,
    ILegislationRegisterProvider legislationProvider,
    IAuthorizationService authorization,
    IOptions<RecruitmentOptions> options) : Controller
{
    public async Task<IActionResult> Index(RecruitmentCompetitionStatus? status, CancellationToken ct)
    {
        var query = db.RecruitmentCompetitions.AsNoTracking();
        if (status is not null)
            query = query.Where(c => c.Status == status);
        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CompetitionIndexRow(
                c.Id,
                c.Number,
                c.Title,
                c.Status,
                c.ApplicationClosingDate,
                c.Lines.Count,
                c.Lines.Sum(l => l.RequiredCount)))
            .Take(200)
            .ToListAsync(ct);
        return View(new CompetitionIndexViewModel
        {
            Rows = rows,
            Status = status,
            CanWrite = await CanAsync(HrManagementPolicies.HrCoreWrite)
        });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var defaults = options.Value.Competition.Defaults;
        var vm = new CompetitionFormViewModel
        {
            ApplicationClosingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Math.Max(7, options.Value.MinPostingDays) + 7),
            ObjectionWindowDays = defaults.ObjectionWindowDays,
            ReserveValidityMonths = defaults.ReserveValidityMonths,
            MaxLinesPerCandidate = defaults.MaxLinesPerCandidate,
            PassMark = defaults.PassMark,
            ReconcileThreshold = defaults.ReconcileThreshold,
            ReconcileRule = defaults.ReconcileRule,
            TieBreakOrder = defaults.TieBreakOrder?.Select(t => (CompetitionTieBreak?)t).ToList() ?? [],
            PrefilledFromDefaults = !defaults.IsEmpty
        };
        await PopulateFormListsAsync(ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(CompetitionFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var (competition, error) = await lifecycle.CreateAsync(vm.ToInput(), ParseUserId(User), ct);
            if (competition is not null)
            {
                TempData["Success"] = $"تم إنشاء المسابقة {competition.Number} كمسودة.";
                return RedirectToAction(nameof(Details), new { id = competition.Id });
            }
            ModelState.AddModelError(string.Empty, error!);
        }
        await PopulateFormListsAsync(ct);
        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null) return NotFound();
        if (!CompetitionWorkflowRules.CanEdit(competition.Status))
        {
            TempData["Error"] = CompetitionWorkflowRules.NotEditableMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
        await PopulateFormListsAsync(ct);
        return View(CompetitionFormViewModel.From(competition));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, CompetitionFormViewModel vm, CancellationToken ct)
    {
        if (vm.Id != id) return BadRequest();
        if (ModelState.IsValid)
        {
            var (success, error) = await lifecycle.UpdateAsync(id, vm.ToInput(), ParseUserId(User), ct);
            if (success)
            {
                TempData["Success"] = "تم حفظ بيانات المسابقة.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(string.Empty, error!);
        }
        await PopulateFormListsAsync(ct);
        return View(vm);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking()
            .Include(c => c.Lines).ThenInclude(l => l.JobCatalogEntry)
            .Include(c => c.Lines).ThenInclude(l => l.OrgUnit)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null) return NotFound();

        var lineIds = competition.Lines.Select(l => l.Id).ToList();
        var requisitions = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.CompetitionLineId != null && lineIds.Contains(r.CompetitionLineId.Value))
            .OrderBy(r => r.RequisitionNumber)
            .Select(r => new
            {
                LineId = r.CompetitionLineId!.Value,
                Row = new CompetitionLineRequisitionRow(
                    r.Id, r.RequisitionNumber, r.JobTitle, r.Status,
                    r.FundedPosition != null && r.FundedPosition.OrgUnit != null ? r.FundedPosition.OrgUnit.NameAr : null)
            })
            .ToListAsync(ct);

        var editable = CompetitionWorkflowRules.CanEdit(competition.Status);
        var lines = new List<CompetitionLineRow>();
        foreach (var line in competition.Lines.OrderBy(l => l.LineNo))
        {
            lines.Add(new CompetitionLineRow(
                line,
                requisitions.Where(r => r.LineId == line.Id).Select(r => r.Row).ToList(),
                editable ? await lifecycle.GetAttachableRequisitionsAsync(line.Id, ct) : []));
        }

        var legalSource = await legislationProvider.GetByIdAsync(competition.LegalSourceId, ct);
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, competition.ApprovalWorkflowInstanceId, ct);
        var workflow = WorkflowPanelBuilder.Build(
            instance,
            definition,
            User,
            WorkflowPanelBuilder.ParseUserId(User),
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = competition.Status == RecruitmentCompetitionStatus.PendingApproval,
                EntityCreatedBy = competition.CreatedBy,
                CreatorSoDMessage = "لا يمكن للمنشئ اعتماد مسابقته الخاصة."
            });

        return View(new CompetitionDetailsViewModel
        {
            Competition = competition,
            LegalSourceLabel = legalSource?.DisplayLabel ?? competition.LegalSourceId,
            Lines = lines,
            TieBreakOrder = CompetitionWorkflowRules.ParseTieBreakOrder(competition.TieBreakOrder),
            Workflow = workflow,
            CanWrite = await CanAsync(HrManagementPolicies.HrCoreWrite),
            CanApprove = await CanAsync(HrManagementPolicies.HrCoreApprove),
            ApplicationCount = await db.RecruitmentApplications.CountAsync(a => a.CompetitionId == id, ct)
        });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AddLine(Guid id, CancellationToken ct)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null) return NotFound();
        await PopulateLineListsAsync(ct);
        return View("LineForm", new CompetitionLineFormViewModel { CompetitionId = id });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> EditLine(Guid id, CancellationToken ct)
    {
        var line = await db.CompetitionLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (line is null) return NotFound();
        await PopulateLineListsAsync(ct);
        return View("LineForm", new CompetitionLineFormViewModel
        {
            CompetitionId = line.CompetitionId,
            LineId = line.Id,
            Title = line.Title,
            GradeCode = line.GradeCode,
            JobCatalogEntryId = line.JobCatalogEntryId,
            OrgUnitId = line.OrgUnitId,
            LinePassMark = line.LinePassMark
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SaveLine(CompetitionLineFormViewModel vm, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var error = vm.LineId is Guid lineId
                ? (await lifecycle.UpdateLineAsync(lineId, vm.ToInput(), ParseUserId(User), ct)).Error
                : (await lifecycle.AddLineAsync(vm.CompetitionId, vm.ToInput(), ParseUserId(User), ct)).Error;
            if (error is null)
            {
                TempData["Success"] = "تم حفظ سطر المسابقة.";
                return RedirectToAction(nameof(Details), new { id = vm.CompetitionId });
            }
            ModelState.AddModelError(string.Empty, error);
        }
        await PopulateLineListsAsync(ct);
        return View("LineForm", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId, CancellationToken ct) =>
        Result(await lifecycle.RemoveLineAsync(lineId, ParseUserId(User), ct), id, "تم حذف السطر.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AttachRequisition(Guid id, Guid lineId, Guid? requisitionId, CancellationToken ct)
    {
        if (requisitionId is not Guid reqId)
        {
            TempData["Error"] = "اختر طلب الشاغر المراد ربطه.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return Result(await lifecycle.AttachRequisitionAsync(lineId, reqId, ParseUserId(User), ct), id, "تم ربط طلب الشاغر بالسطر.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> DetachRequisition(Guid id, Guid requisitionId, CancellationToken ct) =>
        Result(await lifecycle.DetachRequisitionAsync(requisitionId, ParseUserId(User), ct), id, "تم فصل طلب الشاغر عن السطر.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        Result(await lifecycle.SubmitForApprovalAsync(id, ParseUserId(User), UserName(User), ct), id, "تم تقديم المسابقة للاعتماد.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveWorkflow(Guid id, string? comments, CancellationToken ct)
    {
        var instanceId = await db.RecruitmentCompetitions.Where(c => c.Id == id).Select(c => c.ApprovalWorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is not Guid workflowId)
            return Result((false, "لا يوجد مسار اعتماد مرتبط بهذه المسابقة."), id, null);
        var result = await workflowActions.ApproveAsync(workflowId, CreateWorkflowActor(User), comments, targetStepLevel: null, ct);
        return Result((result.Success, result.Error ?? "تعذّر اعتماد الخطوة."), id, "تم اعتماد خطوة مسار المسابقة.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectWorkflow(Guid id, string? comments, CancellationToken ct)
    {
        var instanceId = await db.RecruitmentCompetitions.Where(c => c.Id == id).Select(c => c.ApprovalWorkflowInstanceId).FirstOrDefaultAsync(ct);
        if (instanceId is not Guid workflowId)
            return Result((false, "لا يوجد مسار اعتماد مرتبط بهذه المسابقة."), id, null);
        var result = await workflowActions.RejectAsync(workflowId, CreateWorkflowActor(User), comments, ct);
        return Result((result.Success, result.Error ?? "تعذّر رفض الخطوة."), id, "أُعيدت المسابقة إلى المسودة.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Announce(Guid id, CancellationToken ct) =>
        Result(await lifecycle.AnnounceAsync(id, ParseUserId(User), ct), id, "تم إعلان المسابقة.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CloseIntake(Guid id, CancellationToken ct) =>
        Result(await lifecycle.CloseIntakeAsync(id, ParseUserId(User), ct), id, "تم إغلاق التقديم على المسابقة.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Cancel(Guid id, string? reason, CancellationToken ct) =>
        Result(await lifecycle.CancelAsync(id, reason, ParseUserId(User), ct), id, "تم إلغاء المسابقة.");

    private IActionResult Result((bool Success, string? Error) result, Guid id, string? success)
    {
        TempData[result.Success ? "Success" : "Error"] = result.Success ? success : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanAsync(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;

    private async Task PopulateFormListsAsync(CancellationToken ct)
    {
        var sources = await legislationProvider.ListActiveAsync(ct);
        ViewBag.LegalSources = sources.Select(s => new SelectListItem(s.DisplayLabel, s.LegalSourceId)).ToList();
    }

    private async Task PopulateLineListsAsync(CancellationToken ct)
    {
        ViewBag.JobCatalogEntries = await db.JobCatalogEntries.AsNoTracking()
            .Where(j => j.IsActive)
            .OrderBy(j => j.JobCode)
            .Select(j => new SelectListItem($"{j.JobCode} — {j.TitleAr} ({j.GradeCode})", j.Id.ToString()))
            .ToListAsync(ct);
        ViewBag.OrgUnits = await db.OrgUnits.AsNoTracking()
            .OrderBy(o => o.NameAr)
            .Select(o => new SelectListItem(o.NameAr, o.Id.ToString()))
            .ToListAsync(ct);
    }

    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user)
    {
        var actorRole = user.FindFirstValue(ClaimTypes.Role) ?? user.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        return new WorkflowActionActor(ParseUserId(user), UserName(user), actorRole);
    }

    private static string? UserName(ClaimsPrincipal user) => user.FindFirstValue("preferred_username") ?? user.Identity?.Name;

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
