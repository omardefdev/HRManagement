using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-43 (G-WF-04): definitions are written and published by hr-admin or workflow-admin only, through
/// <see cref="WorkflowDefinitionLifecycleService"/>; the class policy is for readers, and <c>Wf043DefinitionControllerGuardTests</c> requires the
/// admin policy on every action that is not a read. Every write needs an antiforgery token from <c>GET antiforgery</c>: a signed-in
/// browser reaches this API through the gateway's session.
/// </summary>
[Route("api/workflow-definitions")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.WorkflowDefinitionRead)]
public class WorkflowDefinitionsController(
    HRCoreDbContext context,
    WorkflowDefinitionLifecycleService lifecycle,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("antiforgery")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? entityType, [FromQuery] bool? publishedOnly)
    {
        // PLAN-48: the national definitions every agency runs, and the session's own (read-only outside the national tenant).
        var query = context.WorkflowDefinitions.OwnOrNational(context).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(d => d.DomainEntityType == entityType);

        if (publishedOnly == true)
            query = query.Where(d => d.IsPublished);

        var items = await query.OrderBy(d => d.Code).ThenByDescending(d => d.Version).ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var def = await context.WorkflowDefinitions.OwnOrNational(context).AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);
        if (def is null) return NotFound();
        return Ok(def);
    }

    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateWorkflowDefinitionRequest request)
    {
        var result = await lifecycle.CreateDraftAsync(request.ToChange(), Actor(), HttpContext.RequestAborted);
        return result.Success
            ? CreatedAtAction(nameof(Get), new { id = result.Definition!.Id }, result.Definition)
            : ToError(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkflowDefinitionRequest request) =>
        ToResponse(await lifecycle.UpdateDraftAsync(id, request.ToChange(), Actor(), HttpContext.RequestAborted));

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id) =>
        ToResponse(await lifecycle.SubmitForPublishAsync(id, Actor(), HttpContext.RequestAborted));

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id) =>
        ToResponse(await lifecycle.PublishAsync(id, Actor(), HttpContext.RequestAborted));

    [HttpPost("{id:guid}/return")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(Guid id, [FromBody] WorkflowDefinitionReasonRequest? request) =>
        ToResponse(await lifecycle.ReturnToDraftAsync(id, request?.Reason, Actor(), HttpContext.RequestAborted));

    [HttpPost("{id:guid}/retire")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retire(Guid id, [FromBody] WorkflowDefinitionReasonRequest? request) =>
        ToResponse(await lifecycle.RetireAsync(id, request?.Reason, Actor(), HttpContext.RequestAborted));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.WorkflowDefinitionAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await lifecycle.DeleteDraftAsync(id, Actor(), HttpContext.RequestAborted);
        return result.Success ? NoContent() : ToError(result);
    }

    private WorkflowDefinitionActor Actor()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return new WorkflowDefinitionActor(Guid.TryParse(sub, out var userId) ? userId : null, tenantContext.TenantId);
    }

    private IActionResult ToResponse(WorkflowDefinitionResult result) =>
        result.Success ? Ok(new WorkflowDefinitionResponse(result.Definition!, result.Warnings ?? [])) : ToError(result);

    private IActionResult ToError(WorkflowDefinitionResult result)
    {
        var body = new { errors = result.Errors ?? [] };
        return result.Failure switch
        {
            WorkflowDefinitionFailure.NotFound => NotFound(),
            WorkflowDefinitionFailure.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            _ => BadRequest(body)
        };
    }
}

public sealed record WorkflowDefinitionResponse(HR.Core.Domain.Entities.Workflow.WorkflowDefinition Definition, IReadOnlyList<string> Warnings);

public record WorkflowDefinitionReasonRequest
{
    public string? Reason { get; init; }
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
    public bool? RequireDistinctApprovers { get; init; }
    public string? ChangeReason { get; init; }

    public WorkflowDefinitionChange ToChange() => new()
    {
        Code = Code,
        Name = Name,
        DomainEntityType = DomainEntityType,
        Description = Description,
        Steps = Steps,
        GuardrailRoles = GuardrailRoles,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
        RequireDistinctApprovers = RequireDistinctApprovers,
        ChangeReason = ChangeReason
    };
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
    public bool? RequireDistinctApprovers { get; init; }
    public string? ChangeReason { get; init; }

    public WorkflowDefinitionChange ToChange() => new()
    {
        Name = Name,
        DomainEntityType = DomainEntityType,
        Description = Description,
        Steps = Steps,
        GuardrailRoles = GuardrailRoles,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
        RequireDistinctApprovers = RequireDistinctApprovers,
        ChangeReason = ChangeReason
    };
}
