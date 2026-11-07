using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-45 (G-WF-02): the approval page of the types started from outside HR.Core, for the people who hold a step of them. It decides
/// through <see cref="IWorkflowInstanceActionService"/>, so nothing here is wider than the API: structure, step role, the separation
/// rules and <c>ExternalApprovalGate</c>. Any other type answers 404. The gateway applies the same two policies.
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreWorkInbox)]
public sealed class WorkflowApprovalsController(
    HRCoreDbContext db,
    IWorkflowInstanceActionService actions,
    WorkflowApprovalEvaluator evaluator,
    WorkflowGateOrgScope scope) : Controller
{
    public const string RejectReasonMessage = "سبب الرفض مطلوب.";

    private const int MaxCommentLength = 500;

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var (instance, definition, refusal) = await LoadAsync(id, ct);
        if (refusal is not null)
            return refusal;

        var panel = await WorkflowPanelBuilder.BuildAsync(
            instance,
            definition,
            User,
            new WorkflowPanelBuildOptions { IsAwaitingApproval = instance!.Status == WorkflowStatus.InProgress },
            evaluator,
            ct);

        return View(new WorkflowApprovalDetailsViewModel
        {
            InstanceId = instance.Id,
            Title = instance.DisplayTitle ?? definition!.Name,
            WorkflowName = definition!.Name,
            Status = instance.Status,
            RequesterName = instance.RequestedByName,
            SubjectName = await SubjectNameAsync(instance, ct),
            StartedAt = instance.StartedAt,
            CompletedAt = instance.CompletedAt,
            DetailsUrl = SafeDetailsUrl(instance),
            Facts = ReadFacts(instance.ContextJson),
            History = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [],
            Panel = panel
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.WorkflowStepAction)]
    public async Task<IActionResult> ApproveWorkflow(Guid id, string? comments, int? targetStepLevel, CancellationToken ct)
    {
        var (_, _, refusal) = await LoadAsync(id, ct);
        if (refusal is not null)
            return refusal;

        var result = await actions.ApproveAsync(id, WorkflowActionActor.FromPrincipal(User), Bounded(comments), targetStepLevel, ct);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "تم اعتماد الخطوة" : result.Error ?? "تعذّر الاعتماد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.WorkflowStepAction)]
    public async Task<IActionResult> RejectWorkflow(Guid id, string? comments, int? targetStepLevel, CancellationToken ct)
    {
        var (_, _, refusal) = await LoadAsync(id, ct);
        if (refusal is not null)
            return refusal;

        if (string.IsNullOrWhiteSpace(comments))
        {
            TempData["Error"] = RejectReasonMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await actions.RejectAsync(id, WorkflowActionActor.FromPrincipal(User), Bounded(comments), ct, targetStepLevel);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "تم رفض الطلب" : result.Error ?? "تعذّر الرفض.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<(WorkflowInstance? Instance, WorkflowDefinition? Definition, IActionResult? Refusal)> LoadAsync(Guid id, CancellationToken ct)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, id, ct);
        if (instance is null || definition is null || !ExternalApprovalTypes.IsRegistered(instance.EntityType))
            return (null, null, NotFound());

        var userId = WorkflowPanelBuilder.ParseUserId(User);
        if (!WorkInboxAudience.CanRead(User, userId, instance, definition))
            return (null, null, Forbid());

        // A branch-level holder of a step role reads only inside their unit, as they act only there. The requester reads their own.
        if (userId != instance.RequestedByUserId
            && await scope.GetBlockReasonAsync(WorkflowActionActor.FromPrincipal(User), instance.ScopeOrgUnitId, ct) is not null)
            return (null, null, Forbid());

        return (instance, definition, null);
    }

    private async Task<string?> SubjectNameAsync(WorkflowInstance instance, CancellationToken ct) =>
        instance.SubjectEmployeeId is Guid subject
            ? await db.Employees.AsNoTracking().Where(e => e.Id == subject).Select(e => e.FullName).FirstOrDefaultAsync(ct)
            : null;

    /// <summary>Checked at the start too. A link that fails here is not shown, whatever the row holds.</summary>
    private static string? SafeDetailsUrl(WorkflowInstance instance) =>
        ExternalApprovalTypes.Find(instance.EntityType) is { } type && ExternalApprovalService.IsDetailsUrl(instance.DetailsUrl, type.DetailsUrlPrefix)
            ? instance.DetailsUrl
            : null;

    private static IReadOnlyDictionary<string, string> ReadFacts(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(contextJson) ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string? Bounded(string? comments)
    {
        var trimmed = comments?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > MaxCommentLength ? trimmed[..MaxCommentLength] : trimmed;
    }
}
