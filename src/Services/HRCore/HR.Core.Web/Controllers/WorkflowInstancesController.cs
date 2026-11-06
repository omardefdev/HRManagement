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
public class WorkflowInstancesController(
    HRCoreDbContext context,
    IWorkflowInstanceActionService workflowActions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? entityType, [FromQuery] string? status)
    {
        var query = context.WorkflowInstances.AsNoTracking().AsQueryable();

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
            return BadRequest(result.Error);

        return Ok(result.Instance);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = HrManagementPolicies.WorkflowStepAction)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] WorkflowActionRequest request)
    {
        var result = await workflowActions.RejectAsync(
            id,
            CreateActor(User),
            request.Comments);

        if (!result.Success)
            return BadRequest(result.Error);

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

    /// <summary>PLAN-25 (G-04): every role claim, the employee id and the branch scope claim.</summary>
    private static WorkflowActionActor CreateActor(ClaimsPrincipal user) => WorkflowActionActor.FromPrincipal(user);
}

public record WorkflowActionRequest
{
    public string? Comments { get; init; }

    /// <summary>When set, must equal the instance current step (stage-skip guard).</summary>
    public int? TargetStepLevel { get; init; }
}
