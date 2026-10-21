using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
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
    IWorkflowEngine engine,
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

    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Start([FromBody] StartWorkflowRequest request)
    {
        var tenantId = context.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var definition = await context.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .Where(d => d.TenantId == tenantId || d.TenantId == TenantDefaults.DefaultTenantId)
            .Where(d => d.DomainEntityType == request.EntityType && d.IsPublished)
            .Where(d => d.ValidFrom == null || d.ValidFrom <= DateTime.UtcNow)
            .Where(d => d.ValidTo == null || d.ValidTo >= DateTime.UtcNow)
            .OrderByDescending(d => d.TenantId == tenantId)
            .ThenByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (definition is null)
            return BadRequest($"No published workflow definition found for entity type '{request.EntityType}'.");

        var snapshot = WorkflowDefinitionMapper.ToSnapshot(definition);
        var userId = ParseUserId(User);
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name;

        var result = engine.Start(snapshot, request.EntityType, request.EntityId, userId, userName, request.ContextJson);

        if (!result.Success)
            return BadRequest(result.Error);

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            CurrentStepLevel = result.NewStepLevel,
            Status = result.NewStatus,
            ContextJson = request.ContextJson,
            InitiatedBy = userId,
            InitiatedByName = userName,
            StartedAt = DateTime.UtcNow
        };

        if (result.NewStatus == WorkflowStatus.Completed)
            instance.CompletedAt = DateTime.UtcNow;

        context.WorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = instance.Id }, instance);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
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
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
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

    private static WorkflowActionActor CreateActor(ClaimsPrincipal user)
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

public record StartWorkflowRequest
{
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string? ContextJson { get; init; }
}

public record WorkflowActionRequest
{
    public string? Comments { get; init; }

    /// <summary>When set, must equal the instance current step (stage-skip guard).</summary>
    public int? TargetStepLevel { get; init; }
}
