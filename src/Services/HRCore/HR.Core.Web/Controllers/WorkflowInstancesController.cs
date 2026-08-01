using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Web.Controllers;

[Route("api/workflow-instances")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class WorkflowInstancesController : ControllerBase
{
    private readonly HRCoreDbContext _context;
    private readonly IWorkflowEngine _engine;

    public WorkflowInstancesController(HRCoreDbContext context, IWorkflowEngine engine)
    {
        _context = context;
        _engine = engine;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? entityType, [FromQuery] string? status)
    {
        var query = _context.WorkflowInstances.AsNoTracking().AsQueryable();

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
        var instance = await _context.WorkflowInstances.FindAsync(id);
        if (instance is null) return NotFound();
        return Ok(instance);
    }

    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Start([FromBody] StartWorkflowRequest request)
    {
        var definition = await _context.WorkflowDefinitions
            .Where(d => d.DomainEntityType == request.EntityType && d.IsPublished)
            .Where(d => d.ValidFrom == null || d.ValidFrom <= DateTime.UtcNow)
            .Where(d => d.ValidTo == null || d.ValidTo >= DateTime.UtcNow)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (definition is null)
            return BadRequest($"No published workflow definition found for entity type '{request.EntityType}'.");

        var snapshot = ToSnapshot(definition);
        var userId = ParseUserId(User);
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name;

        var result = _engine.Start(snapshot, request.EntityType, request.EntityId, userId, userName, request.ContextJson);

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

        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = instance.Id }, instance);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] WorkflowActionRequest request)
    {
        var instance = await _context.WorkflowInstances.FindAsync(id);
        if (instance is null) return NotFound();

        var definition = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Id == instance.DefinitionId && d.Version == instance.DefinitionVersion);

        if (definition is null)
            return BadRequest("Workflow definition not found for this instance version.");

        var snapshot = ToSnapshot(definition);
        var state = ToState(instance);
        var userId = ParseUserId(User);
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name;

        var result = _engine.ApproveStep(state, snapshot, userId, userName, request.Comments);

        if (!result.Success)
            return BadRequest(result.Error);

        instance.CurrentStepLevel = result.NewStepLevel;
        instance.Status = result.NewStatus;

        if (result.HistoryEntry is not null)
        {
            var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
            history.Add(result.HistoryEntry);
            instance.StepHistoryJson = JsonSerializer.Serialize(history);
        }

        if (result.NewStatus == WorkflowStatus.Completed)
            instance.CompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(instance);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] WorkflowActionRequest request)
    {
        var instance = await _context.WorkflowInstances.FindAsync(id);
        if (instance is null) return NotFound();

        var state = ToState(instance);
        var userId = ParseUserId(User);
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name;

        var result = _engine.Reject(state, userId, userName, request.Comments);

        if (!result.Success)
            return BadRequest(result.Error);

        instance.Status = result.NewStatus;

        if (result.HistoryEntry is not null)
        {
            var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
            history.Add(result.HistoryEntry);
            instance.StepHistoryJson = JsonSerializer.Serialize(history);
        }

        instance.CompletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(instance);
    }

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id)
    {
        var instance = await _context.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (instance is null) return NotFound();

        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
        return Ok(history);
    }

    private static WorkflowDefinitionSnapshot ToSnapshot(WorkflowDefinition def) => new()
    {
        Id = def.Id,
        Code = def.Code,
        Name = def.Name,
        Version = def.Version,
        DomainEntityType = def.DomainEntityType,
        Steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(def.StepsJson) ?? [],
        GuardrailRoles = JsonSerializer.Deserialize<List<string>>(def.GuardrailRolesJson) ?? []
    };

    private static WorkflowInstanceState ToState(WorkflowInstance instance) => new()
    {
        Id = instance.Id,
        CurrentStepLevel = instance.CurrentStepLevel,
        Status = instance.Status,
        StepHistory = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [],
        ContextJson = instance.ContextJson
    };

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
}
