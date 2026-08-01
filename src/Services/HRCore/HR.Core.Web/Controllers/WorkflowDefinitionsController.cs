using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Web.Controllers;

[Route("api/workflow-definitions")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
public class WorkflowDefinitionsController : ControllerBase
{
    private readonly HRCoreDbContext _context;

    public WorkflowDefinitionsController(HRCoreDbContext context) => _context = context;

    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
    public async Task<IActionResult> List([FromQuery] string? entityType, [FromQuery] bool? publishedOnly)
    {
        var query = _context.WorkflowDefinitions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(d => d.DomainEntityType == entityType);

        if (publishedOnly == true)
            query = query.Where(d => d.IsPublished);

        var items = await query.OrderBy(d => d.Code).ThenByDescending(d => d.Version).ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
    public async Task<IActionResult> Get(Guid id)
    {
        var def = await _context.WorkflowDefinitions.FindAsync(id);
        if (def is null) return NotFound();
        return Ok(def);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkflowDefinitionRequest request)
    {
        var existingMax = await _context.WorkflowDefinitions
            .Where(d => d.Code == request.Code)
            .MaxAsync(d => (int?)d.Version) ?? 0;

        var def = new WorkflowDefinition
        {
            Code = request.Code,
            Name = request.Name,
            Version = existingMax + 1,
            DomainEntityType = request.DomainEntityType,
            Description = request.Description,
            StepsJson = JsonSerializer.Serialize(request.Steps ?? []),
            GuardrailRolesJson = JsonSerializer.Serialize(request.GuardrailRoles ?? []),
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo
        };

        _context.WorkflowDefinitions.Add(def);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = def.Id }, def);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkflowDefinitionRequest request)
    {
        var def = await _context.WorkflowDefinitions.FindAsync(id);
        if (def is null) return NotFound();

        if (def.IsPublished)
            return BadRequest("Cannot edit a published definition. Create a new version instead.");

        def.Name = request.Name ?? def.Name;
        def.Description = request.Description ?? def.Description;
        def.DomainEntityType = request.DomainEntityType ?? def.DomainEntityType;
        def.ValidFrom = request.ValidFrom ?? def.ValidFrom;
        def.ValidTo = request.ValidTo ?? def.ValidTo;

        if (request.Steps is not null)
            def.StepsJson = JsonSerializer.Serialize(request.Steps);

        if (request.GuardrailRoles is not null)
            def.GuardrailRolesJson = JsonSerializer.Serialize(request.GuardrailRoles);

        await _context.SaveChangesAsync();
        return Ok(def);
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var def = await _context.WorkflowDefinitions.FindAsync(id);
        if (def is null) return NotFound();

        if (def.IsPublished)
            return BadRequest("Already published.");

        var steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(def.StepsJson);
        if (steps is null || steps.Count == 0)
            return BadRequest("Cannot publish a definition with no steps.");

        def.IsPublished = true;
        await _context.SaveChangesAsync();

        return Ok(def);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var def = await _context.WorkflowDefinitions.FindAsync(id);
        if (def is null) return NotFound();

        if (def.IsPublished)
            return BadRequest("Cannot delete a published definition.");

        _context.WorkflowDefinitions.Remove(def);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateWorkflowDefinitionRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DomainEntityType { get; init; } = string.Empty;
    public string? Description { get; init; }
    public List<WorkflowStepSnapshot>? Steps { get; init; }
    public List<string>? GuardrailRoles { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
}

public record UpdateWorkflowDefinitionRequest
{
    public string? Name { get; init; }
    public string? DomainEntityType { get; init; }
    public string? Description { get; init; }
    public List<WorkflowStepSnapshot>? Steps { get; init; }
    public List<string>? GuardrailRoles { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
}
