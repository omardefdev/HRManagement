using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58 (§1, §2, §6, §7): a payment batch over the API — the one path money leaves by after the posting. A funding decision
/// (<c>Payroll.Approve</c>) on a <c>Pending</c> batch; <c>process</c> and <c>submit-held</c> by the poster's role (<c>Payroll.Post</c>),
/// each with the fraud scan, the gate per employee and the bank file of its sending. Every write names its policy and validates an
/// antiforgery token (<c>GET api/payroll/payment-batches/antiforgery</c>). The answers follow PLAN-56's rule (<see cref="PayrollRunActHttp"/>):
/// 400 a broken rule or a state the act cannot leave, 403 a policy refusal, 404 another agency's batch, 409 a request that lost to another
/// (a second sending of the same batch makes no second file). A batch belongs to the whole agency.
/// </summary>
[ApiController]
[Route("api/payroll/payment-batches")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeCentralOnly("a payment batch pays a whole agency's run, not a unit")]
public sealed class PaymentBatchesApiController(IMediator mediator, PayrollDbContext db) : ControllerBase
{
    /// <summary>The antiforgery token every POST of this controller must send (the header's name and its value); the cookie is set here.</summary>
    [HttpGet("antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    /// <summary>The batch: its state, its totals, and its orders counted by state (no account number).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var batch = await db.PaymentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return NotFound(new { error = PaymentBatchMessages.BatchNotFound });
        var orders = await db.PaymentOrders.AsNoTracking()
            .Where(o => o.PaymentBatchId == id)
            .GroupBy(o => o.Status)
            .Select(g => new { status = g.Key, count = g.Count(), amount = g.Sum(o => o.Amount) })
            .ToListAsync(ct);
        return Ok(new
        {
            batch.Id,
            batch.PayrollRunId,
            status = batch.Status.ToString(),
            source = batch.Source.ToString(),
            batch.TotalAmount,
            batch.ArrearsAmount,
            batch.DisbursedAmount,
            batch.IsPartial,
            batch.FundingDecisionId,
            batch.SubmittedAt,
            batch.AckedAt,
            batch.SettledAt,
            batch.SettlementReference,
            orders = orders.Select(o => new { status = o.status.ToString(), o.count, o.amount }),
        });
    }

    /// <summary>
    /// D-PAY-2: the treasury's funding of the batch, by a holder of <c>Payroll.Approve</c>, on a <c>Pending</c> batch of a posted run,
    /// once (409 for a second). The approved amount is spread over the orders by the pinned framework's priority rules; the rest is arrears.
    /// </summary>
    [HttpPost("{id:guid}/funding-decisions")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> CreateFundingDecision(Guid id, [FromBody] CreateFundingDecisionApiRequest request, CancellationToken ct) =>
        PayrollRunActHttp.Answer(await mediator.Send(new CreateFundingDecisionCommand(
            id, request.ApprovedAmount, request.FundingRatio, request.PriorityRuleSetId, PayProfileChangeHttp.RequireUserId(User), request.Notes), ct));

    /// <summary>
    /// Sends the batch: the fraud scan, the gate per employee (held employees' orders stay out), the bank file of this sending. The poster's
    /// role; the caller is not the run's creator, an approver or the funding decider (400). A second request: 409, and no second file.
    /// </summary>
    [HttpPost("{id:guid}/process")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> Process(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new ProcessPaymentBatchCommand(id, PayProfileChangeHttp.RequireUserId(User)), ct);
        return PayrollRunActHttp.Answer(result, result.Success ? new { batchId = id, fileId = result.Id } : null);
    }

    /// <summary>Sends, with a supplementary bank file, the orders of a sent batch whose hold was lifted since. 400 when none was.</summary>
    [HttpPost("{id:guid}/submit-held")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> SubmitHeld(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new SubmitHeldPaymentOrdersCommand(id, PayProfileChangeHttp.RequireUserId(User)), ct);
        return PayrollRunActHttp.Answer(result, result.Success ? new { batchId = id, fileId = result.Id } : null);
    }
}
