using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-43 (G-WF-05): the start and the withdrawal of an approval, for the service that owns the type. Who may call is the policy;
/// which type it owns is <see cref="ExternalApprovalService"/>.
/// <para>
/// The class demands an antiforgery token, so a POST added here later is protected by default. Both actions opt out: the policy
/// needs a client_credentials token, which no browser holds, so there is no ambient credential a forged request could ride on.
/// </para>
/// </summary>
[ApiController]
[Route("api/workflow-instances")]
[Authorize(Policy = HrManagementPolicies.WorkflowExternalStart)]
[ValidateAntiForgeryToken]
// PLAN-51 (inventory 0.5): the module filter applies the class policy here, so the owning services no longer lean on Module.HrCore's
// hr-core.workflow branch.
[ModulePolicyOverride(HrManagementPolicies.WorkflowExternalStart)]
public sealed class WorkflowExternalInstancesController(ExternalApprovalService approvals) : ControllerBase
{
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Start([FromBody] WorkflowApprovalStartRequest request, CancellationToken ct)
    {
        var result = await approvals.StartAsync(request, Caller(), ct);
        return result.Outcome switch
        {
            ExternalApprovalOutcome.Started => Created($"/api/workflow-instances/{result.Instance!.Id}", ToResponse(result.Instance)),
            ExternalApprovalOutcome.Existing => Ok(ToResponse(result.Instance!)),
            _ => ToError(result)
        };
    }

    [HttpPost("{id:guid}/cancel")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] WorkflowApprovalCancelRequest request, CancellationToken ct)
    {
        var result = await approvals.CancelAsync(id, Caller(), request.Reason, ct);
        return result.Outcome == ExternalApprovalOutcome.Withdrawn ? Ok(ToResponse(result.Instance!)) : ToError(result);
    }

    /// <summary>PLAN-46: for the service that owns the type and started the instance; anyone else gets the 404 of an unknown id.</summary>
    [HttpGet("{id:guid}/external-status")]
    public async Task<IActionResult> ExternalStatus(Guid id, CancellationToken ct) =>
        await approvals.GetStatusAsync(id, Caller(), ct) is { } status ? Ok(status) : NotFound();

    private ExternalApprovalCaller Caller() => new(
        ServiceCallerPrincipal.IsServiceAccount(User),
        ServiceCallerPrincipal.GetClientId(User),
        Request.Headers[ForwardActingUserHandler.HeaderName].ToString(),
        ForwardActingUserHandler.DecodeName(Request.Headers[ForwardActingUserHandler.NameHeaderName].ToString()));

    private static WorkflowApprovalStartResponse ToResponse(WorkflowInstance instance) =>
        new(instance.Id, instance.Status.ToString(), instance.Status == WorkflowStatus.InProgress);

    private IActionResult ToError(ExternalApprovalResult result)
    {
        var body = new { error = result.Error, instanceId = result.Instance?.Id, status = result.Instance?.Status.ToString() };
        return result.Outcome switch
        {
            ExternalApprovalOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            ExternalApprovalOutcome.Conflict => Conflict(body),
            ExternalApprovalOutcome.DefinitionMissing => UnprocessableEntity(body),
            ExternalApprovalOutcome.NotFound => NotFound(),
            _ => BadRequest(body)
        };
    }
}
