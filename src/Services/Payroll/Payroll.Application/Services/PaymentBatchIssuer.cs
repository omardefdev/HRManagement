using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-58 (§1, P-5, G-PAY-08): the only maker of a <see cref="PaymentBatch"/> and its orders, with an explicit source. The posting calls
/// it in its own save: a <c>Pending</c> batch and one <c>Draft</c> order per line with a positive net, to the account the line pinned at
/// its run's snapshot (PLAN-55, D-PAY-15). A line that pinned no account gets a <c>Held</c> order with the reason <c>NO-ACCOUNT</c>
/// (PLAN-55's open question): verifying the employee's account lifts it, and <c>submit-held</c> sends it. Whatever its source, a batch
/// leaves only through <c>process</c> and <c>submit-held</c>, with the fraud scan, the gate and the bank file. PLAN-64 adds its final
/// settlement as a new <see cref="PaymentBatchSource"/> through <see cref="IssueAsync"/>.
/// <para><c>Pay058SinglePaymentPathArchitectureTests</c>: no <c>new PaymentBatch</c> outside this file (and the seed's named
/// construction), and no call of <see cref="IssueAsync"/> but from <c>PostPayrollRunHandler</c>.</para>
/// </summary>
public sealed class PaymentBatchIssuer(IPayrollRepository repository)
{
    public const string NotPosted = "لا تُنشأ دفعة صرف إلا لتشغيل مرحّل.";

    /// <summary>
    /// Adds the batch and its orders to the caller's unit of work (nothing is saved here: the posting saves the run, the batch, the
    /// orders and the outbox row together). <paramref name="lines"/> are the run's saved lines.
    /// </summary>
    public async Task<PaymentBatch> IssueAsync(
        PayrollRun run,
        IReadOnlyCollection<SalaryLine> lines,
        PaymentBatchSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(lines);
        if (source != PaymentBatchSource.PayrollRunPosted)
            throw new ArgumentOutOfRangeException(nameof(source), source, "Only a posted payroll run is a source of a batch today.");
        if (run.Status != PayrollRunStatus.Posted)
            throw new InvalidOperationException(NotPosted);

        var batch = new PaymentBatch
        {
            TenantId = run.TenantId,
            PayrollRunId = run.Id,
            TotalAmount = run.TotalNetPay,
            Status = PaymentBatchStatus.Pending,
            BatchKind = PaymentBatchKind.Disbursement,
            Source = source,
            BankTransferReference = $"PAY-{run.PeriodStart:yyyyMM}-{run.Id:N}"[..32],
        };
        await repository.AddAsync(batch, cancellationToken);

        var orders = await OrdersForAsync(repository.EmployeeBankAccounts, batch, lines, cancellationToken);
        if (PaymentBatchLedger.Check(batch, orders, PaymentBatchLedger.PayableOf(lines)) is { } broken)
            throw new InvalidOperationException(broken);
        foreach (var order in orders)
            await repository.AddAsync(order, cancellationToken);
        return batch;
    }

    /// <summary>
    /// The orders of a batch by the issuer's rule: one per line with a positive net, to the pinned account; <c>NO-ACCOUNT</c> held when
    /// the line pinned none. A line whose net is zero or negative (a correction's recovery) pays nothing and has no order. Used by
    /// <see cref="IssueAsync"/>, and for a batch older than PLAN-58 that never had its orders.
    /// </summary>
    public static async Task<IReadOnlyList<PaymentOrder>> OrdersForAsync(
        IQueryable<EmployeeBankAccount> accounts,
        PaymentBatch batch,
        IReadOnlyCollection<SalaryLine> lines,
        CancellationToken cancellationToken)
    {
        var pinned = await BankAccountPinning.PinnedAccountsAsync(accounts, lines, cancellationToken);
        var orders = new List<PaymentOrder>();
        foreach (var line in lines.Where(l => l.NetPay > 0).OrderBy(l => l.EmployeeId).ThenBy(l => l.Id))
        {
            var account = line.BankAccountId is Guid accountId && pinned.TryGetValue(accountId, out var found) ? found : null;
            orders.Add(new PaymentOrder
            {
                TenantId = batch.TenantId,
                IdempotencyKey = PaymentOrder.IdempotencyKeyFor(batch.PayrollRunId, line.Id),
                PayrollRunId = batch.PayrollRunId,
                PaymentBatchId = batch.Id,
                SalaryLineId = line.Id,
                EmployeeId = line.EmployeeId,
                Amount = line.NetPay,
                BeneficiaryIban = account?.AccountNumber,
                Status = account is null ? PaymentOrderStatus.Held : PaymentOrderStatus.Draft,
                HoldReason = account is null ? PaymentOrderHoldReasons.NoAccount : null,
            });
        }

        return orders;
    }
}

/// <summary>
/// PLAN-58 (§1 rule 2): the sum rule of a batch. Its orders that are not cancelled (held or not) plus the arrears its funding decision
/// left equal the run's payable net — the sum of the positive nets of its lines, which is the run's <c>TotalNetPay</c> whenever no line is
/// negative (every regular run; a correction's recovery line pays nothing). Checked at every transition of the batch.
/// </summary>
public static class PaymentBatchLedger
{
    public const string Broken =
        "مجموع أوامر الدفعة ومتأخراتها لا يساوي صافي التشغيل المستحق الصرف؛ لم يُنفَّذ شيء. راجع الدفعة قبل المتابعة.";

    public static decimal PayableOf(IEnumerable<SalaryLine> lines) => lines.Where(l => l.NetPay > 0).Sum(l => l.NetPay);

    public static decimal OrdersOf(IEnumerable<PaymentOrder> orders) =>
        orders.Where(o => o.Status != PaymentOrderStatus.Cancelled).Sum(o => o.Amount);

    /// <summary>Null when the rule holds; the refusal otherwise.</summary>
    public static string? Check(PaymentBatch batch, IEnumerable<PaymentOrder> orders, decimal payable) =>
        OrdersOf(orders) + batch.ArrearsAmount == payable ? null : Broken;

    /// <summary>The rule on what is saved (read inside the caller's transaction, after its save and before its commit).</summary>
    public static async Task<string?> CheckAsync(IPayrollRepository repository, PaymentBatch batch, CancellationToken cancellationToken)
    {
        if (batch.BatchKind != PaymentBatchKind.Disbursement || batch.Source == PaymentBatchSource.Seeded)
            return null;
        var payable = await repository.SalaryLines.AsNoTracking()
            .Where(l => l.PayrollRunId == batch.PayrollRunId && l.NetPay > 0)
            .SumAsync(l => l.NetPay, cancellationToken);
        var orders = await repository.PaymentOrders.AsNoTracking()
            .Where(o => o.PaymentBatchId == batch.Id && o.Status != PaymentOrderStatus.Cancelled)
            .SumAsync(o => o.Amount, cancellationToken);
        return orders + batch.ArrearsAmount == payable ? null : Broken;
    }
}
