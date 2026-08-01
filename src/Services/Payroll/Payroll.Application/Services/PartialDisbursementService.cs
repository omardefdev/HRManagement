using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>Employee entitlement line used for priority-based partial disbursement.</summary>
public sealed record EmployeeDisbursementClaim(
    Guid EmployeeId,
    decimal EntitledAmount,
    string Category);

public sealed record EmployeeDisbursementAllocation(
    Guid EmployeeId,
    string Category,
    decimal EntitledAmount,
    decimal DisbursedAmount,
    decimal ArrearsAmount);

public sealed class PartialDisbursementResult
{
    public required PaymentBatch Batch { get; init; }
    public required IReadOnlyList<ArrearsLedgerEntry> ArrearsEntries { get; init; }
    public required IReadOnlyList<EmployeeDisbursementAllocation> Allocations { get; init; }
    public decimal TotalEntitled { get; init; }
    public decimal TotalDisbursed { get; init; }
    public decimal TotalArrears { get; init; }
}

/// <summary>PAY-004: allocates constrained treasury funding by legal priority rules.</summary>
public class PartialDisbursementService
{
    /// <summary>
    /// Allocates <paramref name="decision"/>.ApprovedAmount across claims ordered by priority rules
    /// (Priority ascending). Creates a PaymentBatch and arrears entries for unpaid remainders.
    /// Invariant: sum(disbursed) + sum(arrears) == sum(entitled).
    /// </summary>
    public PartialDisbursementResult Allocate(
        PayrollRun run,
        FundingDecision decision,
        IEnumerable<EmployeeDisbursementClaim> claims,
        IEnumerable<DisbursementPriorityRule> rules,
        Guid? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.PayrollRunId != run.Id && decision.PayrollRunId != Guid.Empty)
            throw new InvalidOperationException("FundingDecision.PayrollRunId must match the payroll run.");

        if (decision.RequestedAmount < 0 || decision.ApprovedAmount < 0)
            throw new InvalidOperationException("Funding amounts cannot be negative.");

        if (decision.FundingRatio is < 0 or > 1)
            throw new InvalidOperationException("FundingRatio must be between 0 and 1.");

        var claimList = claims.ToList();
        var activeRules = rules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var priorityByCategory = activeRules
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Priority, StringComparer.OrdinalIgnoreCase);

        var minimumByCategory = activeRules
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MinimumAmount, StringComparer.OrdinalIgnoreCase);

        var ordered = claimList
            .Select((c, index) => new { Claim = c, Index = index })
            .OrderBy(x => priorityByCategory.TryGetValue(x.Claim.Category, out var p) ? p : int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Claim)
            .ToList();

        var available = decision.ApprovedAmount;
        if (available <= 0 && decision.FundingRatio > 0 && decision.RequestedAmount > 0)
            available = Math.Round(decision.RequestedAmount * decision.FundingRatio, 2, MidpointRounding.AwayFromZero);

        var allocations = new List<EmployeeDisbursementAllocation>();
        var remaining = available;

        foreach (var claim in ordered)
        {
            if (claim.EntitledAmount < 0)
                throw new InvalidOperationException("Entitled amounts cannot be negative.");

            var minFloor = minimumByCategory.TryGetValue(claim.Category, out var min)
                ? Math.Min(min, claim.EntitledAmount)
                : 0m;

            decimal pay;
            if (remaining <= 0)
            {
                pay = 0m;
            }
            else if (remaining >= claim.EntitledAmount)
            {
                pay = claim.EntitledAmount;
            }
            else
            {
                // Prefer meeting the legal minimum for this category when funds allow.
                pay = remaining >= minFloor
                    ? Math.Min(remaining, claim.EntitledAmount)
                    : remaining;
            }

            pay = Math.Round(pay, 2, MidpointRounding.AwayFromZero);
            remaining = Math.Round(remaining - pay, 2, MidpointRounding.AwayFromZero);
            var arrears = Math.Round(claim.EntitledAmount - pay, 2, MidpointRounding.AwayFromZero);

            allocations.Add(new EmployeeDisbursementAllocation(
                claim.EmployeeId,
                claim.Category,
                claim.EntitledAmount,
                pay,
                arrears));
        }

        var totalEntitled = allocations.Sum(a => a.EntitledAmount);
        var totalDisbursed = allocations.Sum(a => a.DisbursedAmount);
        var totalArrears = allocations.Sum(a => a.ArrearsAmount);

        if (totalDisbursed + totalArrears != totalEntitled)
            throw new InvalidOperationException(
                $"Disbursement invariant violated: disbursed ({totalDisbursed}) + arrears ({totalArrears}) != entitled ({totalEntitled}).");

        var isPartial = decision.FundingRatio < 1m;
        var tid = tenantId ?? run.TenantId;

        var batch = new PaymentBatch
        {
            TenantId = tid,
            PayrollRunId = run.Id,
            FundingDecisionId = decision.Id == Guid.Empty ? null : decision.Id,
            TotalAmount = totalEntitled,
            DisbursedAmount = totalDisbursed,
            IsPartial = isPartial,
            Status = PaymentBatchStatus.Pending,
            BatchKind = PaymentBatchKind.Disbursement,
            ProcessedAt = DateTime.UtcNow
        };

        var arrearsEntries = allocations
            .Where(a => a.ArrearsAmount > 0)
            .Select(a => new ArrearsLedgerEntry
            {
                TenantId = tid,
                EmployeeId = a.EmployeeId,
                PayrollRunId = run.Id,
                OriginalAmount = a.EntitledAmount,
                PaidAmount = a.DisbursedAmount,
                ArrearsAmount = a.ArrearsAmount,
                Reason = "PartialDisbursement",
                CurrencyEra = "SYP-new",
                Status = a.DisbursedAmount > 0
                    ? ArrearsLedgerStatus.PartiallyPaid
                    : ArrearsLedgerStatus.Pending
            })
            .ToList();

        return new PartialDisbursementResult
        {
            Batch = batch,
            ArrearsEntries = arrearsEntries,
            Allocations = allocations,
            TotalEntitled = totalEntitled,
            TotalDisbursed = totalDisbursed,
            TotalArrears = totalArrears
        };
    }

    /// <summary>Builds claims from salary lines using a single category (e.g. RegularPayroll).</summary>
    public static IReadOnlyList<EmployeeDisbursementClaim> ClaimsFromSalaryLines(
        IEnumerable<SalaryLine> lines,
        string category = "RegularPayroll") =>
        lines.Select(l => new EmployeeDisbursementClaim(l.EmployeeId, l.NetPay, category)).ToList();
}
