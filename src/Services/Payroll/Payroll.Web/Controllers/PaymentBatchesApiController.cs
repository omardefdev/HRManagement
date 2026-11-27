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
            files = await FilesOf(id).ToListAsync(ct),
        });
    }

    /// <summary>PLAN-59 (P-9): how many orders one page of <see cref="Orders"/> answers.</summary>
    public const int OrdersPageSize = 500;

    /// <summary>PLAN-59 (review): the last page the orders read answers (500,000 orders; far above any agency's batch).</summary>
    public const int MaxOrdersPage = 1_000;

    /// <summary>
    /// PLAN-59 (P-9): the batch's orders by employee, in the order of their employee ids, a page at a time: each order's id, employee and
    /// salary line, state, amount, hold reason, whether a destination is pinned on it, and the sequence of the bank file that carried it
    /// (null until it is sent). Never an account number, and no field that names one. Another agency's batch is 404.
    /// </summary>
    [HttpGet("{id:guid}/orders")]
    public async Task<IActionResult> Orders(Guid id, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!await db.PaymentBatches.AsNoTracking().AnyAsync(b => b.Id == id, ct))
            return NotFound(new { error = PaymentBatchMessages.BatchNotFound });
        // A page past any batch's size answers an empty page (never an overflow of the skip).
        page = Math.Clamp(page, 1, MaxOrdersPage);
        var orders = db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == id);
        var total = await orders.CountAsync(ct);
        var sequences = await db.PaymentBatchFiles.AsNoTracking()
            .Where(f => f.PaymentBatchId == id)
            .ToDictionaryAsync(f => f.Id, f => f.Sequence, ct);
        var rows = await orders
            .OrderBy(o => o.EmployeeId).ThenBy(o => o.Id)
            .Skip((page - 1) * OrdersPageSize).Take(OrdersPageSize)
            .Select(o => new
            {
                o.Id,
                o.EmployeeId,
                o.SalaryLineId,
                o.Status,
                o.Amount,
                o.HoldReason,
                DestinationPinned = o.BeneficiaryIban != null,
                o.PaymentBatchFileId,
            })
            .ToListAsync(ct);
        return Ok(new
        {
            batchId = id,
            total,
            page,
            pageSize = OrdersPageSize,
            orders = rows.Select(o => new
            {
                id = o.Id,
                employeeId = o.EmployeeId,
                salaryLineId = o.SalaryLineId,
                status = o.Status.ToString(),
                amount = o.Amount,
                holdReason = o.HoldReason,
                destinationPinned = o.DestinationPinned,
                fileSequence = o.PaymentBatchFileId is Guid file && sequences.TryGetValue(file, out var sequence) ? sequence : (int?)null,
            }),
        });
    }

    /// <summary>§7: the channel acknowledged the sent orders — one act by the poster's role, with the channel's reference (400 without).</summary>
    [HttpPost("{id:guid}/ack")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> Ack(Guid id, [FromBody] AckPaymentBatchApiRequest? request, CancellationToken ct) =>
        PayrollRunActHttp.Answer(await mediator.Send(new AckPaymentBatchCommand(id, request?.ChannelReference), ct));

    /// <summary>
    /// §7: asks to record the settlement the bank statement confirms (its reference and the settled amount, which must be the sum of the
    /// acknowledged orders: 400 otherwise, nothing changed). An approver decides it; the decision settles the batch and its orders.
    /// </summary>
    [HttpPost("{id:guid}/settlement-requests")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> RequestSettlement(
        Guid id, [FromBody] SettlementRequestApiRequest? request, [FromServices] Payroll.Application.Services.PayrollDualControlService dualControl, CancellationToken ct) =>
        DualControlApiController.Asked(await dualControl.RequestAsync(PayrollDualControlKind.BatchSettle, id,
            new Payroll.Application.Services.PayrollDualControlPayload(
                request?.Reason ?? "تسوية الدفعة بكشف المصرف", Amount: request?.SettledAmount, Reference: request?.StatementReference),
            PayProfileChangeHttp.RequireUserId(User), ct));

    /// <summary>§7: asks to record the channel's rejection of the sent orders, with its reason; an approver decides it.</summary>
    [HttpPost("{id:guid}/rejection-requests")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> RequestRejection(
        Guid id, [FromBody] DualControlReasonApiRequest? request, [FromServices] Payroll.Application.Services.PayrollDualControlService dualControl, CancellationToken ct) =>
        DualControlApiController.Asked(await dualControl.RequestAsync(PayrollDualControlKind.BatchReject, id,
            new Payroll.Application.Services.PayrollDualControlPayload(request?.Reason ?? string.Empty), PayProfileChangeHttp.RequireUserId(User), ct));

    /// <summary>D-PAY-19: asks to reissue a bank file, with a reason; an approver who is not the requester decides it.</summary>
    [HttpPost("{id:guid}/files/{fileId:guid}/reissue-requests")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> RequestReissue(
        Guid id, Guid fileId, [FromBody] DualControlReasonApiRequest? request, [FromServices] Payroll.Application.Services.PayrollDualControlService dualControl, CancellationToken ct)
    {
        if (!await db.PaymentBatchFiles.AsNoTracking().AnyAsync(f => f.Id == fileId && f.PaymentBatchId == id, ct))
            return NotFound(new { error = Payroll.Application.Services.PayrollDualControlMessages.FileNotFound });
        return DualControlApiController.Asked(await dualControl.RequestAsync(PayrollDualControlKind.BankFileReissue, fileId,
            new Payroll.Application.Services.PayrollDualControlPayload(request?.Reason ?? string.Empty), PayProfileChangeHttp.RequireUserId(User), ct));
    }

    /// <summary>
    /// §6: the held report — the employees whose order is held out of the sending, by name, with the reason, the record that holds them
    /// and the release request that waits on it, if any. (Never an account number.)
    /// </summary>
    [HttpGet("{id:guid}/held")]
    public async Task<IActionResult> Held(Guid id, [FromServices] Payroll.Application.Abstractions.IEmployeeLookup employeeLookup, CancellationToken ct)
    {
        var batch = await db.PaymentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return NotFound(new { error = PaymentBatchMessages.BatchNotFound });
        var held = await PaymentBatchHeldReport.BuildAsync(db, batch, employeeLookup, ct);
        return Ok(held.Select(h => new
        {
            h.EmployeeId,
            h.EmployeeName,
            reason = h.Reason,
            reasonText = Payroll.Domain.Entities.PaymentOrderHoldReasons.Describe(h.Reason),
            h.Amount,
            h.HoldId,
            h.PendingRequestId,
        }));
    }

    /// <summary>PLAN-58 (§2): the batch's bank files, one per sending (and each reissue): their controls and fingerprints, not their content.</summary>
    [HttpGet("{id:guid}/files")]
    public async Task<IActionResult> Files(Guid id, CancellationToken ct) =>
        await db.PaymentBatches.AsNoTracking().AnyAsync(b => b.Id == id, ct)
            ? Ok(await FilesOf(id).ToListAsync(ct))
            : NotFound(new { error = PaymentBatchMessages.BatchNotFound });

    /// <summary>
    /// A stored bank file, as it was handed over: read, never built again. A holder of <c>Payroll.Post</c> or <c>Payroll.Approve</c>
    /// (403 otherwise); audited as a bulk export (the file's id and controls; never an account).
    /// </summary>
    [HttpGet("{id:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> DownloadFile(Guid id, Guid fileId, [FromServices] IAuthorizationService authorization, CancellationToken ct)
    {
        if (!(await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollPost)).Succeeded
            && !(await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollApprove)).Succeeded)
            return Forbid();
        var file = await db.PaymentBatchFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && f.PaymentBatchId == id, ct);
        if (file is null)
            return NotFound(new { error = Payroll.Infrastructure.Services.PaymentBatchFileWriter.FileNotFound });
        PaymentBatchFileAudit.Record(HttpContext, file);
        return File(file.Content, "text/csv", file.FileName);
    }

    private IQueryable<object> FilesOf(Guid batchId) =>
        db.PaymentBatchFiles.AsNoTracking()
            .Where(f => f.PaymentBatchId == batchId)
            .OrderBy(f => f.Sequence)
            .Select(f => new
            {
                f.Id,
                f.Sequence,
                kind = f.Kind.ToString(),
                f.ControlCount,
                f.ControlTotal,
                f.Sha256,
                f.GeneratedBy,
                f.GeneratedAt,
                f.SupersededById,
                f.ReissueReason,
                f.ReissueApprovedBy,
            });

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

/// <summary>PLAN-58 (§7): the channel's acknowledgement reference.</summary>
public sealed record AckPaymentBatchApiRequest(string? ChannelReference);

/// <summary>PLAN-58 (§7): the bank statement's reference and the settled amount (the sum of the acknowledged orders).</summary>
public sealed record SettlementRequestApiRequest(string? StatementReference, decimal? SettledAmount, string? Reason = null);
