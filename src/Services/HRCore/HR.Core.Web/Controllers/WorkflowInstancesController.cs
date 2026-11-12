using System.Security.Claims;
using System.Text.Json;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Route("api/workflow-instances")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
[OrgScoped(typeof(HR.Core.Domain.Entities.Workflow.WorkflowInstance))]
public class WorkflowInstancesController(
    HRCoreDbContext context,
    IWorkflowInstanceActionService workflowActions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? entityType, [FromQuery] string? status)
    {
        // PLAN-52 (P-5, AC-IAM-52d): a restricted user lists his scope's instances only (an instance with no unit is central).
        var query = await context.WorkflowInstances.AsNoTracking().AsQueryable().InOrgScopeAsync(HttpContext, context);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(i => i.EntityType == entityType);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<WorkflowStatus>(status, true, out var s))
            query = query.Where(i => i.Status == s);

        var items = await query.OrderByDescending(i => i.StartedAt).Take(100).ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var instance = await context.WorkflowInstances.FindAsync(id);
        if (instance is null) return NotFound();
        return Ok(instance);
    }

    // PLAN-52: a step decision keeps the workflow gates' answer (WorkflowGateOrgScope refuses, in-app, an instance outside the actor's
    // scope or with no unit, and the requester rule comes first: AC-WF-43e pins 400); List, Get and History take the 404 filter (P-5).
    [OrgScopeNotApplicable("a step decision: the workflow gates apply the actor's scope and the principal's (WorkflowGateOrgScope, PLAN-43/50)")]
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = HrManagementPolicies.WorkflowStepAction)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] WorkflowActionRequest request)
    {
        var result = await workflowActions.ApproveAsync(
            id,
            CreateActor(User),
            request.Comments,
            request.TargetStepLevel);

        if (!result.Success)
            return Refused(result);

        return Ok(result.Instance);
    }

    [OrgScopeNotApplicable("a step decision: the workflow gates apply the actor's scope and the principal's (WorkflowGateOrgScope, PLAN-43/50)")]
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = HrManagementPolicies.WorkflowStepAction)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] WorkflowActionRequest request)
    {
        var result = await workflowActions.RejectAsync(
            id,
            CreateActor(User),
            request.Comments,
            targetStepLevel: request.TargetStepLevel);

        if (!result.Success)
            return Refused(result);

        return Ok(result.Instance);
    }

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id)
    {
        var instance = await context.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (instance is null) return NotFound();

        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
        return Ok(history);
    }

    /// <summary>PLAN-44: a decision that lost a race is a 409, every other refusal a 400.</summary>
    private IActionResult Refused(WorkflowActionResult result) =>
        result.IsConflict ? Conflict(result.Error) : BadRequest(result.Error);

    /// <summary>PLAN-25 (G-04): every role claim, the employee id and the branch scope claim.</summary>
    private static WorkflowActionActor CreateActor(ClaimsPrincipal user) => WorkflowActionActor.FromPrincipal(user);
}

public record WorkflowActionRequest
{
    public string? Comments { get; init; }

    /// <summary>When set, must equal the instance current step (stage-skip guard).</summary>
    public int? TargetStepLevel { get; init; }
}
