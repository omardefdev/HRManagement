using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class EstablishmentChangesController(
    HRCoreDbContext db,
    EstablishmentScopeService establishmentScope,
    EstablishmentChangeLifecycleService lifecycleService,
    EstablishmentChangeWorkflowUiService workflowUiService,
    IWorkflowInstanceActionService workflowActions,
    ILegislationRegisterProvider legislationProvider,
    EstablishmentPayloadSummaryBuilder payloadSummaryBuilder) : Controller
{
    public async Task<IActionResult> Index(EstablishmentChangesIndexViewModel filter)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        filter.NormalizePaging();

        var query = db.EstablishmentChangeRequests.AsNoTracking().AsQueryable();

        if (filter.Status is EstablishmentChangeStatus status)
            query = query.Where(r => r.Status == status);

        if (filter.ChangeType is EstablishmentChangeType changeType)
            query = query.Where(r => r.ChangeType == changeType);

        if (filter.BudgetYear is int budgetYear)
        {
            var yearToken = $"\"BudgetYear\":{budgetYear}";
            query = query.Where(r => r.PayloadJson.Contains(yearToken));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(r =>
                r.RequestNumber.Contains(term) ||
                (r.DecisionReference != null && r.DecisionReference.Contains(term)));
        }

        query = query.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestNumber);

        filter.Items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return View(filter);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        var request = await db.EstablishmentChangeRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound();

        string? legalLabel = null;
        if (!string.IsNullOrWhiteSpace(request.LegalSourceId))
        {
            var legalSource = await legislationProvider.GetByIdAsync(request.LegalSourceId);
            legalLabel = legalSource?.DisplayLabel ?? request.LegalSourceId;
        }

        var changeLogs = await db.EstablishmentChangeLogs
            .AsNoTracking()
            .Where(l => l.ChangeRequestId == id)
            .OrderByDescending(l => l.OccurredAt)
            .ToListAsync();

        var vm = await workflowUiService.BuildDetailsAsync(request, User);
        vm.LegalSourceLabel = legalLabel;
        vm.ChangeLogs = changeLogs;
        vm.PayloadSummary = await payloadSummaryBuilder.BuildAsync(request);
        vm.Submit = new EstablishmentChangeSubmitViewModel
        {
            LegalSourceId = request.LegalSourceId ?? string.Empty,
            DecisionReference = request.DecisionReference,
            EffectiveFrom = request.EffectiveFrom
        };
        vm.CanMutate = true;

        await PopulateLegalSourcesAsync(vm.Submit.LegalSourceId);
        return View(vm);
    }

    /// <summary>PLAN-25 (G-02): gives a change request left PendingApproval without a workflow its approval workflow.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> RestartApproval(Guid id)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        var (success, error) = await lifecycleService.RestartApprovalAsync(id, ParseUserId(User), User.Identity?.Name);
        TempData[success ? "Success" : "Error"] = success ? "تمت إعادة بدء مسار اعتماد الطلب" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Submit(Guid id, EstablishmentChangeSubmitViewModel submit)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        submit.LegalSourceId = NormalizeLegalSourceId(submit.LegalSourceId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(submit.LegalSourceId))
            ModelState.AddModelError(nameof(submit.LegalSourceId), EstablishmentLegalSourceValidator.RequiredOnSubmitMessage);

        if (!ModelState.IsValid)
        {
            var request = await db.EstablishmentChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (request is null) return NotFound();

            var vm = await workflowUiService.BuildDetailsAsync(request, User);
            vm.Submit = submit;
            vm.PayloadSummary = await payloadSummaryBuilder.BuildAsync(request);
            vm.CanMutate = true;
            await PopulateLegalSourcesAsync(submit.LegalSourceId);
            return View("Details", vm);
        }

        var (success, error) = await lifecycleService.SubmitForApprovalAsync(
            id,
            submit.LegalSourceId,
            submit.DecisionReference,
            ParseUserId(User),
            User.Identity?.Name,
            submit.EffectiveFrom);

        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم تقديم طلب تغيير الملاك للاعتماد";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> ApproveWorkflow(Guid id, EstablishmentChangeWorkflowActionViewModel vm)
    {
        if (id != vm.ChangeRequestId) return BadRequest();

        var request = await db.EstablishmentChangeRequests.FindAsync(id);
        if (request is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            entityOrgUnitId: null);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (request.WorkflowInstanceId is not Guid workflowId)
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
            ? "تم اعتماد خطوة مسار تغيير الملاك"
            : result.Error ?? "تعذّر اعتماد الطلب.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> RejectWorkflow(Guid id, EstablishmentChangeWorkflowActionViewModel vm)
    {
        if (id != vm.ChangeRequestId) return BadRequest();

        var request = await db.EstablishmentChangeRequests.FindAsync(id);
        if (request is null) return NotFound();

        var orgBlock = WorkflowInboxOrgScope.GetBlockReason(
            await establishmentScope.GetScopeAsync(User),
            entityOrgUnitId: null);
        if (orgBlock is not null)
        {
            TempData["Error"] = orgBlock;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (request.WorkflowInstanceId is not Guid workflowId)
        {
            TempData["Error"] = "لا يوجد مسار اعتماد مرتبط بهذا الطلب.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await workflowActions.RejectAsync(
            workflowId,
            CreateWorkflowActor(User),
            vm.Comments);

        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تم رفض طلب تغيير الملاك وإرجاعه إلى المسودة"
            : result.Error ?? "تعذّر رفض الطلب.";

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User) is not null)
            return Forbid();

        var (success, error) = await lifecycleService.CancelAsync(id);
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = "تم إلغاء طلب التغيير";
        return RedirectToAction(nameof(Details), new { id });
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

    private static WorkflowActionActor CreateWorkflowActor(ClaimsPrincipal user)
    {
        var userId = ParseUserId(user);
        var userName = user.FindFirstValue("preferred_username") ?? user.Identity?.Name;
        var actorRole = user.FindFirstValue(ClaimTypes.Role) ?? user.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
        return new WorkflowActionActor(userId, userName, actorRole);
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
