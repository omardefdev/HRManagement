using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PartialDisbursement;

public record ExecutePartialDisbursementCommand(
    Guid PayrollRunId,
    Guid FundingDecisionId,
    bool Persist = true) : IRequest<ExecutePartialDisbursementResult>;

public record PartialDisbursementOrderDto(
    Guid OrderId,
    Guid EmployeeId,
    Guid SalaryLineId,
    decimal Amount);

public record ExecutePartialDisbursementResult(
    bool Success,
    string? Error,
    Guid? BatchId,
    decimal TotalDisbursed,
    decimal TotalArrears,
    bool InvariantOk,
    bool WasIdempotentReplay,
    IReadOnlyList<PartialDisbursementOrderDto> Orders);

public class ExecutePartialDisbursementHandler(
    IPayrollRepository repository,
    PartialDisbursementService disbursementService,
    IPaymentOrderCoordinator paymentOrderCoordinator)
    : IRequestHandler<ExecutePartialDisbursementCommand, ExecutePartialDisbursementResult>
{
    public async Task<ExecutePartialDisbursementResult> Handle(
        ExecutePartialDisbursementCommand request,
        CancellationToken cancellationToken)
    {
        var existingBatch = await repository.PaymentBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.FundingDecisionId == request.FundingDecisionId,
                cancellationToken);

        if (existingBatch is not null)
        {
            var existingOrders = await LoadOrderDtosAsync(existingBatch.Id, cancellationToken);
            return new ExecutePartialDisbursementResult(
                true,
                null,
                existingBatch.Id,
                existingBatch.DisbursedAmount,
                existingBatch.TotalAmount - existingBatch.DisbursedAmount,
                existingBatch.DisbursedAmount + (existingBatch.TotalAmount - existingBatch.DisbursedAmount) == existingBatch.TotalAmount,
                WasIdempotentReplay: true,
                existingOrders);
        }

        var decision = await repository.FundingDecisions
            .FirstOrDefaultAsync(d => d.Id == request.FundingDecisionId, cancellationToken);
        if (decision is null)
            return Empty(false, "قرار التمويل غير موجود.");

        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null)
            return Empty(false, "التشغيل غير موجود.");

        if (decision.PayrollRunId != run.Id)
            return Empty(false, "قرار التمويل لا يخص هذا التشغيل.");

        if (run.Status is not (PayrollRunStatus.Approved or PayrollRunStatus.Posted))
            return Empty(false, "يجب اعتماد أو ترحيل التشغيل قبل الصرف الجزئي.");

        var lines = run.SalaryLines.ToList();
        if (lines.Count == 0)
            return Empty(false, "لا توجد بنود رواتب للصرف.");

        // PLAN-54 (§2): a run pinned to a v2 version is disbursed by that version's own priority rules, inside its fingerprint. A run
        // of a version older than PLAN-54 (or pinned to none) reads, as before, the decision's rule set or the general active rules;
        // «general» now means rules that belong to no version, so a version's rules never leak into a legacy run.
        var ruleSetId = decision.PriorityRuleSetId;
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
                return Empty(false, "قرار التمويل يسمّي مجموعة قواعد غير قواعد الإطار القانوني المثبت على التشغيل.");
            rules = await repository.DisbursementPriorityRules
                .AsNoTracking()
                .Where(r => r.PayrollConfigurationId == pinnedVersion.Id)
                .ToListAsync(cancellationToken);
            // A version's rules are in force through the version the run is pinned to; their stored IsActive is off (see the pack
            // mapper), and the allocation takes them all. These are untracked copies: nothing is written.
            foreach (var rule in rules)
                rule.IsActive = true;
        }
        else
        {
            rules = ruleSetId.HasValue
                ? await repository.DisbursementPriorityRules
                    .AsNoTracking()
                    .Where(r => r.RuleSetId == ruleSetId.Value)
                    .ToListAsync(cancellationToken)
                : await repository.DisbursementPriorityRules
                    .AsNoTracking()
                    .Where(r => r.IsActive && r.PayrollConfigurationId == null)
                    .ToListAsync(cancellationToken);
        }

        if (rules.Count == 0)
            return Empty(false, "لا توجد قواعد أولوية للصرف الجزئي.");

        var claims = PartialDisbursementService.ClaimsFromSalaryLines(lines);
        var allocation = disbursementService.Allocate(run, decision, claims, rules, run.TenantId);

        if (!request.Persist)
        {
            var previewOverrides = BuildAmountOverrides(allocation.Allocations);
            return new ExecutePartialDisbursementResult(
                true,
                null,
                allocation.Batch.Id,
                allocation.TotalDisbursed,
                allocation.TotalArrears,
                allocation.TotalDisbursed + allocation.TotalArrears == allocation.TotalEntitled,
                false,
                PreviewOrders(lines, previewOverrides));
        }

        await repository.AddAsync(allocation.Batch, cancellationToken);
        foreach (var entry in allocation.ArrearsEntries)
            await repository.AddAsync(entry, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);

        var amountOverrides = BuildAmountOverrides(allocation.Allocations);
        await paymentOrderCoordinator.EnsureAndSubmitOrdersAsync(
            allocation.Batch.Id,
            amountOverrides,
            cancellationToken);

        var orders = await LoadOrderDtosAsync(allocation.Batch.Id, cancellationToken);
        return new ExecutePartialDisbursementResult(
            true,
            null,
            allocation.Batch.Id,
            allocation.TotalDisbursed,
            allocation.TotalArrears,
            allocation.TotalDisbursed + allocation.TotalArrears == allocation.TotalEntitled,
            false,
            orders);
    }

    private static Dictionary<Guid, decimal> BuildAmountOverrides(
        IReadOnlyList<EmployeeDisbursementAllocation> allocations) =>
        allocations
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.DisbursedAmount));

    private static IReadOnlyList<PartialDisbursementOrderDto> PreviewOrders(
        IReadOnlyList<SalaryLine> lines,
        IReadOnlyDictionary<Guid, decimal> overrides) =>
        lines
            .Where(l => overrides.TryGetValue(l.EmployeeId, out var amt) && amt > 0)
            .Select(l => new PartialDisbursementOrderDto(
                Guid.Empty,
                l.EmployeeId,
                l.Id,
                overrides[l.EmployeeId]))
            .ToList();

    private async Task<IReadOnlyList<PartialDisbursementOrderDto>> LoadOrderDtosAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var orders = await repository.PaymentOrders
            .AsNoTracking()
            .Where(o => o.PaymentBatchId == batchId)
            .Select(o => new PartialDisbursementOrderDto(o.Id, o.EmployeeId, o.SalaryLineId, o.Amount))
            .ToListAsync(cancellationToken);
        return orders;
    }

    private static ExecutePartialDisbursementResult Empty(bool success, string? error) =>
        new(success, error, null, 0, 0, false, false, []);
}
