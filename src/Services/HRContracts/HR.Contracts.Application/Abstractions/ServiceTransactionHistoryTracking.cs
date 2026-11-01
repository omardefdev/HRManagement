using HR.Contracts.Domain.Entities;

namespace HR.Contracts.Application.Abstractions;

/// <summary>
/// PLAN-38 (P6, PLAN-37 defect 1): <see cref="ServiceTransaction.MoveTo"/> appends a history row whose Guid key the
/// entity sets itself. EF finds that row through the tracked transaction's navigation and, because the key is set, takes
/// it for an existing row: it issues an UPDATE that matches nothing (DbUpdateConcurrencyException, HTTP 500). The
/// handlers register the rows appended since <paramref name="countBefore"/> as new, so they are inserted.
/// </summary>
public static class ServiceTransactionHistoryTracking
{
    public static async Task TrackNewHistoryAsync(
        this IContractsRepository repository,
        ServiceTransaction transaction,
        int countBefore,
        CancellationToken ct)
    {
        foreach (var row in transaction.StateHistory.Skip(countBefore).ToList())
            await repository.AddAsync(row, ct);
    }
}
