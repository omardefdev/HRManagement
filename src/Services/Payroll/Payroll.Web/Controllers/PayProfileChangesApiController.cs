using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-53 (§2): the salary change transaction over the API. A processor submits, a different approver approves or rejects, the
/// requester or an approver cancels. Every write names its policy; the branch scope is the filter's (a foreign request or employee is
/// 404) and the handler's. The answers: 400 a broken rule, 409 a state conflict, 403 one's own request, 404 outside the scope.
/// </summary>
[ApiController]
[Route("api/payroll/pay-profile-changes")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScoped(typeof(PayProfileChangeRequest))]
public sealed class PayProfileChangesApiController(PayrollDbContext db, IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PayProfileChangeState? state, [FromQuery] Guid? employeeId, CancellationToken ct)
    {
        var query = await db.PayProfileChangeRequests.AsNoTracking().InOrgScopeAsync(HttpContext, db);
        if (state.HasValue) query = query.Where(r => r.State == state.Value);
        if (employeeId.HasValue) query = query.Where(r => r.EmployeeId == employeeId.Value);
        var items = await query.OrderByDescending(r => r.RequestedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync(ct);
        return Ok(items.Select(PayProfileChangeDto.From));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var request = await db.PayProfileChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return request is null ? NotFound() : Ok(PayProfileChangeDto.From(request));
    }

    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Submit([FromBody] SubmitPayProfileChangeApiRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new SubmitPayProfileChangeCommand(
            request.EmployeeId, request.ProposedBaseSalary, request.ProposedPayGradeId, request.EffectiveFrom, request.Reason,
            request.LegalReference, PayProfileChangeSource.Api, request.SourceReference,
            PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User), await HttpContext.GetOrgScopeAsync()), ct);
        return result.Success
            ? CreatedAtAction(nameof(Get), new { id = result.RequestId }, PayProfileChangeHttp.Body(result))
            : PayProfileChangeHttp.Refusal(result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Approve(Guid id, [FromBody] PayProfileChangeDecisionApiRequest? request, CancellationToken ct) =>
        DecideAsync(id, PayProfileChangeDecision.Approved, request?.Note, ct);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public Task<IActionResult> Reject(Guid id, [FromBody] PayProfileChangeDecisionApiRequest? request, CancellationToken ct) =>
        DecideAsync(id, PayProfileChangeDecision.Rejected, request?.Note, ct);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = HrManagementPolicies.PayrollChangeCancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] PayProfileChangeCancelApiRequest? request, CancellationToken ct)
    {
        var result = await mediator.Send(new CancelPayProfileChangeCommand(
            id, request?.Reason, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User),
            PayrollPolicyRules.CanApprove(User), await HttpContext.GetOrgScopeAsync()), ct);
        return result.Success ? Ok(PayProfileChangeHttp.Body(result)) : PayProfileChangeHttp.Refusal(result);
    }

    /// <summary>Applies now the scheduled requests of the caller's agency that are due: the code the activation job runs.</summary>
    [HttpPost("activate-due")]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    [OrgScopeCentralOnly("applies every due request of the agency, whatever its branch")]
    public async Task<IActionResult> ActivateDue(
        [FromServices] PayProfileChangeActivator activator, [FromServices] ITenantContext tenant, CancellationToken ct)
    {
        var summary = await activator.ActivateDueAsync(ct);
        if (tenant.TenantId is Guid tenantId)
            PayProfileChangeMetrics.SetBlocked(tenantId, summary.BlockedTotal);
        return Ok(new { due = summary.Due, applied = summary.Applied, blocked = summary.Blocked, blockedTotal = summary.BlockedTotal });
    }

    private async Task<IActionResult> DecideAsync(Guid id, PayProfileChangeDecision decision, string? note, CancellationToken ct)
    {
        var result = await mediator.Send(new DecidePayProfileChangeCommand(
            id, decision, note, PayProfileChangeHttp.RequireUserId(User), PayProfileChangeHttp.UserName(User),
            await HttpContext.GetOrgScopeAsync()), ct);
        return result.Success ? Ok(PayProfileChangeHttp.Body(result)) : PayProfileChangeHttp.Refusal(result);
    }
}

/// <summary>The body of <c>POST api/payroll/pay-profile-changes</c>: what a caller may propose, and nothing of the request's own fields.</summary>
public sealed record SubmitPayProfileChangeApiRequest(
    Guid EmployeeId,
    decimal? ProposedBaseSalary,
    Guid? ProposedPayGradeId,
    DateOnly EffectiveFrom,
    string? Reason,
    string? LegalReference,
    string? SourceReference = null);

public sealed record PayProfileChangeDecisionApiRequest(string? Note);

public sealed record PayProfileChangeCancelApiRequest(string? Reason);

/// <summary>A request as the API returns it.</summary>
public sealed record PayProfileChangeDto(
    Guid Id,
    Guid EmployeeId,
    Guid? OrgUnitId,
    string ChangeKind,
    string Source,
    string? SourceReference,
    Guid? CurrentProfileId,
    decimal? CurrentBaseSalary,
    Guid? CurrentPayGradeId,
    decimal? ProposedBaseSalary,
    Guid? ProposedPayGradeId,
    DateOnly EffectiveFrom,
    string Reason,
    string? LegalReference,
    string State,
    Guid RequestedBy,
    DateTime RequestedAt,
    Guid? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNote,
    Guid? CancelledBy,
    DateTime? CancelledAt,
    string? CancelReason,
    DateTime? AppliedAt,
    Guid? AppliedProfileId,
    string? LastApplyError,
    Guid? SupersededByRequestId)
{
    public static PayProfileChangeDto From(PayProfileChangeRequest r) => new(
        r.Id, r.EmployeeId, r.OrgUnitId, r.ChangeKind.ToString(), r.Source.ToString(), r.SourceReference, r.CurrentProfileId,
        r.CurrentBaseSalary, r.CurrentPayGradeId, r.ProposedBaseSalary, r.ProposedPayGradeId, r.EffectiveFrom, r.Reason, r.LegalReference,
        r.State.ToString(), r.RequestedBy, r.RequestedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote, r.CancelledBy, r.CancelledAt,
        r.CancelReason, r.AppliedAt, r.AppliedProfileId, r.LastApplyError, r.SupersededByRequestId);
}
