namespace Payroll.Application.Abstractions;

public interface IBankReconciliationCoordinator
{
    Task<int> ReconcileBatchAsync(Guid paymentBatchId, CancellationToken cancellationToken = default);
}
