using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-028 (F2, G-05/G-09): the single appeal path. Central HR assigns the independent reviewer and answers
/// information requests; the assigned compliance reviewer decides. Exempt from the HR.Core module filter, so a
/// compliance-only user reaches it (and sees only the appeals assigned to their employee).
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreAppealReview)]
public class RecruitmentAppealsController(
    HRCoreDbContext db,
    RecruitmentAppealService appealService,
    RecruitmentAppealWorkflowUiService workflowUi,
    RecruitmentSelectionService selection,
    OrgScopeService orgScope,
    IAuthorizationService authorization) : Controller
{
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        var showAll = string.Equals(status, "all", StringComparison.OrdinalIgnoreCase);
        var query = db.RecruitmentAppeals.AsNoTracking()
            .Include(a => a.RecruitmentApplication!)
                .ThenInclude(app => app.Requisition!)
                    .ThenInclude(r => r.FundedPosition)
            .AsQueryable();
        if (!showAll)
            query = query.Where(a => a.Status == RecruitmentAppealStatus.Pending);

        var assignedOnly = RecruitmentAppealAccess.IsComplianceOnly(User);
        if (assignedOnly)
        {
            var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
            var userId = WorkflowPanelBuilder.ParseUserId(User);
            query = query.Where(a =>
                (a.IndependentReviewerEmployeeId != null && a.IndependentReviewerEmployeeId == employeeId)
                || (a.IndependentReviewerEmployeeId == null && userId != null && a.IndependentReviewerId == userId));
        }

        var appeals = await query.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);
        if (!assignedOnly)
        {
            var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
            appeals = appeals.Where(a => InScope(scope, a)).ToList();
        }

        var names = await ReviewerNamesAsync(appeals, ct);
        return View(new RecruitmentAppealsIndexViewModel
        {
            Items = appeals.Select(a => new RecruitmentAppealListItem(
                    a,
                    a.RecruitmentApplication?.ApplicantFullName ?? "—",
                    a.RecruitmentApplication?.Requisition?.RequisitionNumber,
                    a.IndependentReviewerEmployeeId is Guid r && names.TryGetValue(r, out var n) ? n : null))
                .ToList(),
            StatusFilter = showAll ? "all" : "pending",
            AssignedOnly = assignedOnly
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var appeal = await db.RecruitmentAppeals.AsNoTracking()
            .Include(a => a.RecruitmentApplication!)
                .ThenInclude(app => app.Requisition!)
                    .ThenInclude(r => r.FundedPosition)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appeal?.RecruitmentApplication is null || !await CanViewAsync(appeal, ct))
            return NotFound();

        var app = appeal.RecruitmentApplication;
        var requisition = app.Requisition;
        var canWrite = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreWrite)).Succeeded;
        var pending = appeal.Status == RecruitmentAppealStatus.Pending;
        var hasReviewer = appeal.IndependentReviewerEmployeeId is not null || appeal.IndependentReviewerId is not null;

        string? reviewerName = null;
        if (appeal.IndependentReviewerEmployeeId is Guid reviewerId)
            reviewerName = await db.Employees.AsNoTracking()
                .Where(e => e.Id == reviewerId)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync(ct);

        var assessorCount = await db.RecruitmentAssessmentScores.AsNoTracking()
            .Where(s => s.RecruitmentApplicationId == app.Id)
            .Select(s => s.AssessorUserId)
            .Distinct()
            .CountAsync(ct);
        var committeeDecision = app.RequisitionId is Guid requisitionId
            ? (await selection.GetCommitteeRankingGateAsync(requisitionId, ct)).DecisionFor(app.Id)
            : null;

        var allowedOutcomes = RecruitmentAppealWorkflowRules.DecidableOutcomes
            .Where(o => RecruitmentAppealWorkflowRules.ValidateOutcome(
                o, requisition?.Status, app.RankBeforeRejection ?? app.Rank, correctedRank: 1) is null)
            .ToList();

        return View(new RecruitmentAppealDetailsViewModel
        {
            Appeal = appeal,
            Application = app,
            Requisition = requisition,
            ReviewerName = reviewerName,
            AssessorCount = assessorCount,
            CommitteeDecision = committeeDecision,
            Workflow = await workflowUi.BuildPanelAsync(appeal, User, ct),
            CanAssign = canWrite && pending,
            CanDecide = pending && hasReviewer && !appeal.IsAwaitingInfo && RecruitmentAppealAccess.CanDecide(appeal, User),
            CanResubmit = canWrite && pending && appeal.IsAwaitingInfo,
            AllowedOutcomes = allowedOutcomes
        });
    }

    /// <summary>HCM-028 (H2): assign or reassign the independent reviewer (employee picker).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AssignReviewer(Guid id, Guid? reviewerEmployeeId, CancellationToken ct)
    {
        if (reviewerEmployeeId is not Guid reviewer)
        {
            TempData["Error"] = "يجب اختيار المراجع المستقل.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var (success, error) = await appealService.AssignReviewerAsync(
            id, reviewer, WorkflowPanelBuilder.ParseUserId(User), HrOrgScopeVisibility.GetEmployeeId(User), User.Identity?.Name, ct);
        TempData[success ? "Success" : "Error"] = success ? "تم تعيين المراجع المستقل وبدء مسار المراجعة." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>HCM-028 (H3): the assigned reviewer's explicit outcome.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(
        Guid id,
        RecruitmentAppealOutcome outcome,
        string? rationale,
        int? correctedRank,
        CancellationToken ct)
    {
        var (success, error) = await appealService.DecideAsync(id, outcome, rationale, correctedRank, CreateActor(User), ct);
        TempData[success ? "Success" : "Error"] = success ? "تم البت في الاستئناف." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>HCM-028 (H3): the reviewer asks HR for more information (the review step is returned).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestInfo(Guid id, string? infoRequest, CancellationToken ct)
    {
        var (success, error) = await appealService.RequestInfoAsync(id, infoRequest, CreateActor(User), ct);
        TempData[success ? "Success" : "Error"] = success ? "تم طلب معلومات إضافية." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>HCM-028 (H3): HR supplies the requested information; the review restarts with the same reviewer.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Resubmit(Guid id, string? additionalInfo, CancellationToken ct)
    {
        var (success, error) = await appealService.ResubmitAsync(
            id, additionalInfo, WorkflowPanelBuilder.ParseUserId(User), User.Identity?.Name, ct);
        TempData[success ? "Success" : "Error"] = success ? "تم استيفاء المعلومات وإعادة الاستئناف للمراجع." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanViewAsync(RecruitmentAppeal appeal, CancellationToken ct)
    {
        if (RecruitmentAppealAccess.IsComplianceOnly(User))
            return RecruitmentAppealAccess.IsAssignedTo(appeal, User);
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        return InScope(scope, appeal);
    }

    private static bool InScope(OrgAccessScope scope, RecruitmentAppeal appeal) =>
        WorkflowInboxOrgScope.Allows(scope, appeal.RecruitmentApplication?.Requisition?.FundedPosition?.OrgUnitId);

    private async Task<Dictionary<Guid, string>> ReviewerNamesAsync(IEnumerable<RecruitmentAppeal> appeals, CancellationToken ct)
    {
        var ids = appeals.Where(a => a.IndependentReviewerEmployeeId.HasValue)
            .Select(a => a.IndependentReviewerEmployeeId!.Value)
            .Distinct()
            .ToList();
        return ids.Count == 0
            ? []
            : await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
    }

    private static AppealReviewActor CreateActor(ClaimsPrincipal user)
    {
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        var userName = user.FindFirstValue("preferred_username") ?? user.Identity?.Name;
        return new AppealReviewActor(
            new WorkflowActionActor(userId, userName, HrManagementRoles.Compliance),
            HrOrgScopeVisibility.GetEmployeeId(user),
            RecruitmentAppealAccess.HasReviewerRole(user));
    }
}
