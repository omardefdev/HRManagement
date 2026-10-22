using System.Security.Claims;
using HR.Core.Domain.Entities;
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
public sealed class SelectionCommitteesController(
    HRCoreDbContext db,
    SelectionCommitteeService committeeService,
    SelectionCommitteeLifecycleService lifecycleService,
    SelectionCommitteeWorkflowUiService workflowUiService,
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
        string? recommendation)
    {
        var (session, error) = await committeeService.CreateSessionAsync(
            requisitionId, committeeDate, findings, recommendation);
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
        Guid requisitionId,
        Guid employeeId,
        string role,
        bool hasConflictOfInterest)
    {
        var (success, error) = await committeeService.AddMemberAsync(
            sessionId, employeeId, role, hasConflictOfInterest);
        TempData[success ? "Success" : "Error"] = success ? "تمت إضافة العضو" : error;
        return RedirectToAction(nameof(Details), new { id = sessionId });
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
