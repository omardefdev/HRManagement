using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Abstractions;

/// <summary>
/// PLAN-58 (§2, P-6): writes the bank file of one sending, inside the sending's transaction: built from the orders submitted in it, with
/// its control count and total, stored and never regenerated. Called by <c>process</c> and <c>submit-held</c> (and the four-eyes reissue).
/// Returns the stored file's id. The caller saves.
/// </summary>
public interface IPaymentBatchFileWriter
{
    Task<Guid> WriteAsync(
        PaymentBatch batch,
        IReadOnlyList<PaymentOrder> sent,
        PaymentBatchFileKind kind,
        Guid generatedBy,
        CancellationToken cancellationToken);
}
