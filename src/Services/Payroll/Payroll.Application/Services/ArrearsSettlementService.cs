using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

public sealed class ArrearsSettlementResult
{
    public required PaymentBatch SettlementBatch { get; init; }
    public required IReadOnlyList<ArrearsLedgerEntry> SettledEntries { get; init; }
    public decimal AmountApplied { get; init; }
    public decimal AmountRemaining { get; init; }
    public bool WasIdempotentReplay { get; init; }
}

public sealed record EmployeeArrearsBalance(
    Guid EmployeeId,
    decimal EntitledTotal,
    decimal PaidTotal,
    decimal ArrearsTotal,
    int OpenEntryCount);

/// <summary>PAY-004: settles oldest arrears first; idempotent by settlement key / batch.</summary>
public class ArrearsSettlementService
{
    private readonly Dictionary<string, Guid> _settledKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Applies <paramref name="availableAmount"/> to open arrears for an employee,
    /// oldest first (CreatedAt, then Id). Same <paramref name="settlementKey"/> is a no-op replay.
    /// </summary>
    public ArrearsSettlementResult Settle(
        Guid employeeId,
        decimal availableAmount,
        IList<ArrearsLedgerEntry> openEntries,
        string settlementKey,
        Guid? tenantId = null,
        Guid? settlementBatchId = null)
    {
        ArgumentNullException.ThrowIfNull(openEntries);
        if (string.IsNullOrWhiteSpace(settlementKey))
            throw new ArgumentException("Settlement key is required for idempotency.", nameof(settlementKey));

        if (availableAmount < 0)
            throw new InvalidOperationException("Settlement amount cannot be negative.");

        var key = NormalizeKey(settlementKey);

        if (_settledKeys.TryGetValue(key, out var priorBatchId)
            || (settlementBatchId is { } explicitBatch
                && openEntries.Any(e => e.SettledByBatchId == explicitBatch)))
        {
            var replayBatchId = priorBatchId != Guid.Empty
                ? priorBatchId
                : settlementBatchId ?? Guid.Empty;
            var already = openEntries
                .Where(e => e.EmployeeId == employeeId && e.SettledByBatchId == replayBatchId)
                .ToList();
            return new ArrearsSettlementResult
            {
                SettlementBatch = new PaymentBatch
                {
                    Id = replayBatchId,
                    TenantId = tenantId ?? already.FirstOrDefault()?.TenantId ?? Guid.Empty,
                    PayrollRunId = already.FirstOrDefault()?.PayrollRunId ?? Guid.Empty,
                    TotalAmount = 0,
                    DisbursedAmount = 0,
                    IsPartial = false,
                    Status = PaymentBatchStatus.Processed,
                    BatchKind = PaymentBatchKind.Disbursement,
                    BankTransferReference = settlementKey,
                    ProcessedAt = DateTime.UtcNow
                },
                SettledEntries = already,
                AmountApplied = 0,
                AmountRemaining = availableAmount,
                WasIdempotentReplay = true
            };
        }

        var batchId = settlementBatchId ?? Guid.NewGuid();

        var ordered = openEntries
            .Where(e => e.EmployeeId == employeeId
                        && e.ArrearsAmount > 0
                        && e.Status != ArrearsLedgerStatus.FullyPaid)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();

        var remaining = availableAmount;
        var touched = new List<ArrearsLedgerEntry>();
        decimal applied = 0;

        foreach (var entry in ordered)
        {
            if (remaining <= 0)
                break;

            var pay = Math.Min(remaining, entry.ArrearsAmount);
            pay = Math.Round(pay, 2, MidpointRounding.AwayFromZero);
            if (pay <= 0)
                continue;

            if (!entry.ApplyPayment(pay))
                throw new InvalidOperationException($"Failed to apply settlement to arrears entry {entry.Id}.");

            if (entry.Status == ArrearsLedgerStatus.FullyPaid)
                entry.SettledByBatchId = batchId;

            remaining = Math.Round(remaining - pay, 2, MidpointRounding.AwayFromZero);
            applied = Math.Round(applied + pay, 2, MidpointRounding.AwayFromZero);
            touched.Add(entry);
        }

        _settledKeys[key] = batchId;

        var batch = new PaymentBatch
        {
            Id = batchId,
            TenantId = tenantId ?? ordered.FirstOrDefault()?.TenantId ?? Guid.Empty,
            PayrollRunId = ordered.FirstOrDefault()?.PayrollRunId ?? Guid.Empty,
            TotalAmount = applied,
            DisbursedAmount = applied,
            IsPartial = false,
            Status = PaymentBatchStatus.Processed,
            BatchKind = PaymentBatchKind.Disbursement,
            BankTransferReference = settlementKey,
            ProcessedAt = DateTime.UtcNow
        };

        return new ArrearsSettlementResult
        {
            SettlementBatch = batch,
            SettledEntries = touched,
            AmountApplied = applied,
            AmountRemaining = remaining,
            WasIdempotentReplay = false
        };
    }

    /// <summary>Marks a settlement key as already processed (e.g. loaded from persistence).</summary>
    public void RememberSettlementKey(string settlementKey, Guid batchId) =>
        _settledKeys[NormalizeKey(settlementKey)] = batchId;

    public static EmployeeArrearsBalance GetBalance(Guid employeeId, IEnumerable<ArrearsLedgerEntry> entries)
    {
        var list = entries.Where(e => e.EmployeeId == employeeId).ToList();
        return new EmployeeArrearsBalance(
            employeeId,
            list.Sum(e => e.OriginalAmount),
            list.Sum(e => e.PaidAmount),
            list.Sum(e => e.ArrearsAmount),
            list.Count(e => e.Status != ArrearsLedgerStatus.FullyPaid));
    }

    private static string NormalizeKey(string settlementKey) =>
        settlementKey.Trim();
}
