using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
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

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
[RecruitmentOrgScope(RecruitmentScopeKind.CommitteeSession)]
public sealed class SelectionCommitteesController(
    HRCoreDbContext db,
    SelectionCommitteeService committeeService,
    SelectionCommitteeLifecycleService lifecycleService,
    SelectionCommitteeWorkflowUiService workflowUiService,
    SelectionCommitteeMinutesService minutesService,
    EstablishmentScopeService establishmentScope) : Controller
{
    public async Task<IActionResult> Index(Guid requisitionId)
    {
        var requisition = await db.RecruitmentRequisitions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (requisition is null) return NotFound();

        var session = await committeeService.GetLatestForRequisitionAsync(requisitionId);
        ViewBag.Requisition = requisition;
        return View(session);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var session = await committeeService.GetSessionAsync(id);
        if (session is null) return NotFound();

        var vm = await workflowUiService.BuildDetailsAsync(session, User);
        vm.Workspace = await committeeService.GetWorkspaceAsync(id);
        ViewData["SessionId"] = session.Id;
        ViewBag.Requisition = session.Requisition;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CreateSession(
        Guid requisitionId,
        DateTime committeeDate,
        string? findings,
        string? recommendation,
        CommitteePurpose purpose = CommitteePurpose.Ranking)
    {
        var (session, error) = await committeeService.CreateSessionAsync(
            requisitionId, committeeDate, findings, recommendation, purpose);
        TempData[session is null ? "Error" : "Success"] = error ?? "تم إنشاء جلسة اللجنة";
        if (session is not null)
            return RedirectToAction(nameof(Details), new { id = session.Id });
        return RedirectToAction(nameof(Index), new { requisitionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AddMember(
        Guid sessionId,
        Guid employeeId,
        string? role,
        bool hasConflictOfInterest)
    {
        var (success, error) = await committeeService.AddMemberAsync(
            sessionId, employeeId, role, hasConflictOfInterest, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تمت إضافة العضو" : error;
        return RedirectToDetails(sessionId, "members");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RemoveMember(Guid sessionId, Guid memberId)
    {
        var (success, error) = await committeeService.RemoveMemberAsync(sessionId, memberId, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تمت إزالة العضو" : error;
        return RedirectToDetails(sessionId, "members");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SetChair(Guid sessionId, Guid memberId)
    {
        var (success, error) = await committeeService.SetChairAsync(sessionId, memberId, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم تعيين رئيس اللجنة" : error;
        return RedirectToDetails(sessionId, "members");
    }

    /// <summary>HCM-028: per-candidate conflict of a member, recorded by the HR secretary.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RecordCoi(Guid sessionId, Guid memberId, Guid applicationId, bool hasConflict)
    {
        var (success, error) = await committeeService.RecordMemberCoiAsync(
            sessionId, memberId, applicationId, hasConflict, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success
            ? (hasConflict ? "تم تسجيل تعارض المصالح وحجب صوت العضو على هذا المرشح" : "تم إلغاء تعارض المصالح")
            : error;
        return RedirectToDetails(sessionId, "votes");
    }

    /// <summary>HCM-028: vote recorded on behalf of the member by the HR secretary.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RecordVote(
        Guid sessionId,
        Guid memberId,
        Guid applicationId,
        CommitteeVoteValue vote,
        string? comment)
    {
        var (success, error) = await committeeService.RecordVoteAsync(
            sessionId, memberId, applicationId, vote, comment, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل التصويت" : error;
        return RedirectToDetails(sessionId, "votes");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> UpsertDecision(
        Guid sessionId,
        Guid applicationId,
        CommitteeRecommendation recommendation,
        int? proposedRank,
        string? rationale)
    {
        // A rejection never carries a rank; the form keeps the pre-filled rank field, so drop it here.
        var rank = recommendation == CommitteeRecommendation.Reject ? null : proposedRank;
        var (success, error) = await committeeService.UpsertDecisionAsync(
            sessionId, applicationId, recommendation, rank, rationale, ParseUserId(User));
        TempData[success ? "Success" : "Error"] = success ? "تم حفظ توصية اللجنة" : error;
        return RedirectToDetails(sessionId, "decisions");
    }

    /// <summary>HCM-028 (D): committee minutes PDF, generated from the locked session data.</summary>
    [HttpGet]
    public async Task<IActionResult> Minutes(Guid id, CancellationToken ct)
    {
        var (minutes, error) = await minutesService.GetMinutesAsync(id, ct);
        if (minutes is null)
        {
            if (error == SelectionCommitteeService.SessionNotFoundMessage) return NotFound();
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "SelectionCommittees/Minutes");
        // PLAN-29: archive minutes via File.Storage
        var pdf = SelectionCommitteeMinutesDocument.Generate(minutes);
        return File(pdf, "application/pdf", $"committee-minutes-{minutes.RequisitionNumber}-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    /// <summary>PLAN-25 (G-02): gives a session submitted without a workflow its approval workflow.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RestartApproval(Guid id)
    {
        var (success, error) = await lifecycleService.RestartApprovalAsync(id, ParseUserId(User), User.Identity?.Name);
        TempData[success ? "Success" : "Error"] = success ? "تمت إعادة بدء مسار اعتماد اللجنة" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Submit(Guid id)
    {
        var (success, error) = await lifecycleService.SubmitForApprovalAsync(
            id, ParseUserId(User), User.Identity?.Name);
        TempData[success ? "Success" : "Error"] = success ? "تم تقديم اللجنة للاعتماد" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveWorkflow(Guid id, SelectionCommitteeWorkflowActionViewModel vm)
    {
        if (id != vm.SessionId) return BadRequest();

        var session = await db.SelectionCommitteeSessions
            .Include(s => s.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (session is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            session.Requisition?.FundedPosition?.OrgUnitId);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        // HCM-028 (B): quorum and member/creator separation are enforced server-side by the lifecycle service.
        var (success, error) = await lifecycleService.ApproveStepAsync(
            id,
            CreateWorkflowActor(User),
            HrOrgScopeVisibility.GetEmployeeId(User),
            vm.Comments);

        TempData[success ? "Success" : "Error"] = success ? "تم اعتماد خطوة مسار اللجنة" : error;

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectWorkflow(Guid id, SelectionCommitteeWorkflowActionViewModel vm)
    {
        if (id != vm.SessionId) return BadRequest();

        var session = await db.SelectionCommitteeSessions
            .Include(s => s.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (session is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            session.Requisition?.FundedPosition?.OrgUnitId);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        var (success, error) = await lifecycleService.RejectStepAsync(
            id,
            CreateWorkflowActor(User),
            HrOrgScopeVisibility.GetEmployeeId(User),
            vm.Comments);

        TempData[success ? "Success" : "Error"] = success ? "تم رفض مسار اللجنة وإعادة الجلسة للتعديل" : error;

        return RedirectToAction(nameof(Details), new { id });
    }

    private RedirectToActionResult RedirectToDetails(Guid sessionId, string fragment) =>
        RedirectToAction(nameof(Details), null, new { id = sessionId }, fragment);

    /// <summary>PLAN-25 (G-04): every role claim, the employee id and the branch scope claim.</summary>
    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user) => WorkflowActionActor.FromPrincipal(user);

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
