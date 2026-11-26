using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PartialDisbursement;

/// <summary>PLAN-58 (D-PAY-2): the messages of a funding decision.</summary>
public static class FundingDecisionMessages
{
    public const string AfterPostingOnly =
        "قرار التمويل يُتخذ بعد الترحيل فقط، على دفعة الصرف «قيد الانتظار»: هذا التشغيل لم يُرحَّل بعد.";
    public const string BatchNotPending = "قرار التمويل يُتخذ على دفعة «قيد الانتظار» فقط، قبل إرسالها.";
    public const string AlreadyFunded = "لهذه الدفعة قرار تمويل من قبل؛ لا يُتخذ قرار ثانٍ.";
    public const string RatioOutOfRange = "نسبة التمويل يجب أن تكون بين 0 و 1.";
    public const string AmountOutOfRange = "المبلغ المعتمد يجب أن يكون بين صفر وصافي التشغيل المستحق.";
    public const string NoRules = "لا توجد قواعد أولوية للصرف الجزئي.";
    public const string RuleSetNotThePinnedFramework = "قرار التمويل يسمّي مجموعة قواعد غير قواعد الإطار القانوني المثبت على التشغيل.";
    public const string OneOrderPerEmployee = "في الدفعة أكثر من أمر لموظف واحد؛ لا يُوزَّع التمويل عليها.";
    public const string NoFundingForOrder = "لا تمويل لهذا الأمر في قرار التمويل: صافيه كله متأخرات.";
}

/// <summary>
/// PLAN-58 (D-PAY-2, §1): the treasury's funding of a posted run's batch, decided by a holder of <c>Payroll.Approve</c> (the decider from
/// the token). On a <c>Pending</c> batch of a <c>Posted</c> run only, once. The approved amount is spread over the batch's orders by the
/// priority rules of the run's pinned framework version (PLAN-54); what an employee is not paid now is an arrears row, as before. A
/// partial disbursement is this decision, then <c>process</c> by the poster — who is not the decider.
/// </summary>
public record CreateFundingDecisionCommand(
    Guid PaymentBatchId,
    decimal? ApprovedAmount,
    decimal FundingRatio,
    Guid? PriorityRuleSetId,
    Guid DecidedBy,
    string? Notes = null) : IRequest<RunActResult>;

/// <summary>The run route kept for its clients: it answers 400 for a run that is not posted, and otherwise acts on the posting's batch.</summary>
public record CreateRunFundingDecisionCommand(
    Guid PayrollRunId,
    decimal? ApprovedAmount,
    decimal FundingRatio,
    Guid? PriorityRuleSetId,
    Guid DecidedBy,
    string? Notes = null) : IRequest<RunActResult>;

public class CreateRunFundingDecisionHandler(IPayrollRepository repository, IMediator mediator)
    : IRequestHandler<CreateRunFundingDecisionCommand, RunActResult>
{
    public async Task<RunActResult> Handle(CreateRunFundingDecisionCommand request, CancellationToken cancellationToken)
    {
        var run = await repository.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.PayrollRunId, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (run.Status != PayrollRunStatus.Posted)
            return RunActResult.Invalid(FundingDecisionMessages.AfterPostingOnly);

        var batchId = await repository.PaymentBatches.AsNoTracking()
            .Where(b => b.PayrollRunId == run.Id && b.BatchKind == PaymentBatchKind.Disbursement && b.Status == PaymentBatchStatus.Pending)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (batchId is null)
            return RunActResult.Invalid(FundingDecisionMessages.BatchNotPending);

        return await mediator.Send(new CreateFundingDecisionCommand(
            batchId.Value, request.ApprovedAmount, request.FundingRatio, request.PriorityRuleSetId, request.DecidedBy, request.Notes),
            cancellationToken);
    }
}

public class CreateFundingDecisionHandler(IPayrollRepository repository, PartialDisbursementService allocator)
    : IRequestHandler<CreateFundingDecisionCommand, RunActResult>
{
    public async Task<RunActResult> Handle(CreateFundingDecisionCommand request, CancellationToken cancellationToken)
    {
        if (request.FundingRatio is < 0 or > 1)
            return RunActResult.Invalid(FundingDecisionMessages.RatioOutOfRange);

        repository.ClearChangeTracker();
        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null)
            return RunActResult.NotFound(PaymentBatchMessages.BatchNotFound);
        if (batch.BatchKind != PaymentBatchKind.Disbursement)
            return RunActResult.Invalid(PaymentBatchMessages.NotADisbursement);

        var run = await repository.GetPayrollRunAsync(batch.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (run.Status != PayrollRunStatus.Posted)
            return RunActResult.Invalid(FundingDecisionMessages.AfterPostingOnly);
        if (batch.Status != PaymentBatchStatus.Pending)
            return RunActResult.Invalid(FundingDecisionMessages.BatchNotPending);
        if (batch.FundingDecisionId is not null)
            return RunActResult.Conflict(FundingDecisionMessages.AlreadyFunded);

        // In a fixed order (the employee, then the order): the allocation pays the claims of a priority in list order, so the same batch
        // and decision always fund the same employees.
        var orders = await repository.PaymentOrders
            .Where(o => o.PaymentBatchId == batch.Id && o.Status != PaymentOrderStatus.Cancelled)
            .OrderBy(o => o.EmployeeId)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);
        if (orders.Count == 0 && batch.Source != PaymentBatchSource.PayrollRunPosted)
        {
            // A batch posted before PLAN-58 has no orders yet: they are made now, by the issuer's rule, so that the decision spreads
            // over what will be sent.
            foreach (var order in await PaymentBatchIssuer.OrdersForAsync(repository.EmployeeBankAccounts, batch, run.SalaryLines.ToList(), cancellationToken))
            {
                await repository.AddAsync(order, cancellationToken);
                orders.Add(order);
            }
        }

        if (orders.GroupBy(o => o.EmployeeId).Any(g => g.Count() > 1))
            return RunActResult.Invalid(FundingDecisionMessages.OneOrderPerEmployee);

        var payable = PaymentBatchLedger.PayableOf(run.SalaryLines);
        if (request.ApprovedAmount is { } approvedAmount && (approvedAmount < 0 || approvedAmount > payable))
            return RunActResult.Invalid(FundingDecisionMessages.AmountOutOfRange);

        var (rules, rulesError) = await FundingRules.ForRunAsync(repository, run, request.PriorityRuleSetId, cancellationToken);
        if (rulesError is not null)
            return RunActResult.Invalid(rulesError);

        var decision = new FundingDecision
        {
            TenantId = run.TenantId,
            PayrollRunId = run.Id,
            RequestedAmount = payable,
            ApprovedAmount = request.ApprovedAmount ?? Math.Round(payable * request.FundingRatio, 2, MidpointRounding.AwayFromZero),
            FundingRatio = request.FundingRatio,
            PriorityRuleSetId = request.PriorityRuleSetId,
            ApprovedBy = request.DecidedBy,
            DecisionDate = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };

        var claims = orders.Select(o => new EmployeeDisbursementClaim(o.EmployeeId, o.Amount, "RegularPayroll")).ToList();
        var allocation = allocator.Allocate(run, decision, claims, rules!, run.TenantId);
        var paidBy = allocation.Allocations.ToDictionary(a => a.EmployeeId, a => a.DisbursedAmount);

        foreach (var order in orders)
        {
            order.Amount = paidBy[order.EmployeeId];
            order.UpdatedAt = DateTime.UtcNow;
            if (order.Amount == 0m)
            {
                order.Status = PaymentOrderStatus.Cancelled;
                order.HoldReason = null;
                order.RejectionReason = FundingDecisionMessages.NoFundingForOrder;
            }
        }

        foreach (var entry in allocation.ArrearsEntries)
            await repository.AddAsync(entry, cancellationToken);
        await repository.AddAsync(decision, cancellationToken);

        batch.FundingDecisionId = decision.Id;
        batch.IsPartial = allocation.TotalArrears > 0;
        batch.DisbursedAmount = allocation.TotalDisbursed;
        batch.ArrearsAmount = allocation.TotalArrears;
        batch.UpdatedAt = DateTime.UtcNow;

        if (PaymentBatchLedger.Check(batch, orders, payable) is { } broken)
        {
            repository.ClearChangeTracker();
            return RunActResult.Invalid(broken);
        }

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PaymentBatchMessages.ChangedMeanwhile);
        }

        return RunActResult.Done(decision.Id);
    }
}

/// <summary>
/// PLAN-54 (§2): the priority rules a run's funding is spread by. A run pinned to a v2 version uses that version's own rules, inside its
/// fingerprint; a run of a version older than PLAN-54 (or pinned to none) reads, as before, the decision's rule set or the general active
/// rules («general» meaning rules that belong to no version). Untracked copies: nothing is written.
/// </summary>
internal static class FundingRules
{
    public static async Task<(List<DisbursementPriorityRule>? Rules, string? Error)> ForRunAsync(
        IPayrollRepository repository, PayrollRun run, Guid? ruleSetId, CancellationToken cancellationToken)
    {
        var pinnedVersion = run.PayrollConfigurationId is Guid pinnedId
            ? await repository.PayrollConfigurations.AsNoTracking()
                .Where(c => c.Id == pinnedId)
                .Select(c => new { c.Id, c.HashSchemaVersion })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        List<DisbursementPriorityRule> rules;
        if (pinnedVersion is { HashSchemaVersion: >= PayrollConfiguration.CurrentHashSchemaVersion })
        {
            if (ruleSetId.HasValue && ruleSetId.Value != pinnedVersion.Id)
                return (null, FundingDecisionMessages.RuleSetNotThePinnedFramework);
            rules = await repository.DisbursementPriorityRules.AsNoTracking()
                .Where(r => r.PayrollConfigurationId == pinnedVersion.Id)
                .ToListAsync(cancellationToken);
            // A version's rules are in force through the version the run is pinned to; their stored IsActive is off (see the pack
            // mapper), and the allocation takes them all.
            foreach (var rule in rules)
                rule.IsActive = true;
        }
        else
        {
            rules = ruleSetId.HasValue
                ? await repository.DisbursementPriorityRules.AsNoTracking().Where(r => r.RuleSetId == ruleSetId.Value).ToListAsync(cancellationToken)
                : await repository.DisbursementPriorityRules.AsNoTracking()
                    .Where(r => r.IsActive && r.PayrollConfigurationId == null)
                    .ToListAsync(cancellationToken);
        }

        return rules.Count == 0 ? (null, FundingDecisionMessages.NoRules) : (rules, null);
    }
}
