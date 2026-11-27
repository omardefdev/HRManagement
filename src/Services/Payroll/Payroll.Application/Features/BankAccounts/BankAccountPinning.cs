using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.BankAccounts;

/// <summary>
/// PLAN-55 (§2, D-PAY-15): the account a run pins. A run takes each employee's account in force at its snapshot
/// (<see cref="PayrollRunInputSnapshot.BankAccountId"/>), its line copies it (<see cref="SalaryLine.BankAccountId"/>), and every reader
/// after that — payment orders, the treasury file, FR-04, the readiness report — takes the line's account: never the pay profile, and
/// never «the current account». So a change of account after the calculation applies from the next cycle.
/// </summary>
public static class BankAccountPinning
{
    /// <summary>The account in force of every employee of the agency, now: what a snapshot pins.</summary>
    public static async Task<IReadOnlyDictionary<Guid, Guid>> ActiveAccountsAsync(IPayrollRepository repository, CancellationToken ct)
    {
        var active = await repository.EmployeeBankAccounts.AsNoTracking()
            .Where(a => a.Status == BankAccountStatus.Active)
            .Select(a => new { a.EmployeeId, a.Id })
            .ToListAsync(ct);
        var byEmployee = new Dictionary<Guid, Guid>(active.Count);
        foreach (var account in active)
            byEmployee.TryAdd(account.EmployeeId, account.Id);   // one per employee and agency (the filtered unique index)
        return byEmployee;
    }

    /// <summary>The numbers of the accounts these lines pinned, by account id: what an order or a file row carries.</summary>
    public static async Task<IReadOnlyDictionary<Guid, PinnedAccount>> PinnedAccountsAsync(
        IQueryable<EmployeeBankAccount> accounts, IEnumerable<SalaryLine> lines, CancellationToken ct)
    {
        var ids = lines.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, PinnedAccount>();
        return await accounts.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new PinnedAccount(a.Id, a.AccountNumber, a.Status))
            .ToDictionaryAsync(a => a.Id, ct);
    }

    /// <summary>
    /// «Disbursed»: the lines, among <paramref name="lineIds"/>, of which a payment order has left for the channel (<c>Submitted</c>,
    /// <c>Acknowledged</c>, <c>Settled</c>, <c>PartiallySettled</c>) in a batch the channel did not send back. A line with no order, a
    /// draft order or a rejected order is not disbursed; neither is a line whose batch was rejected, returned, failed or reversed
    /// (that batch may be submitted again, to the same account).
    /// </summary>
    public static async Task<HashSet<Guid>> DisbursedLineIdsAsync(
        IQueryable<PaymentOrder> orders, IQueryable<PaymentBatch> batches, IReadOnlyCollection<Guid> lineIds, CancellationToken ct)
    {
        if (lineIds.Count == 0)
            return [];
        var disbursed = await (from order in orders.AsNoTracking()
                               join batch in batches.AsNoTracking() on order.PaymentBatchId equals batch.Id into joined
                               from batch in joined.DefaultIfEmpty()
                               where lineIds.Contains(order.SalaryLineId) && DisbursedOrderStatuses.Contains(order.Status)
                                     && (batch == null || !UnpaidBatchStatuses.Contains(batch.Status))
                               select order.SalaryLineId)
            .ToListAsync(ct);
        return disbursed.ToHashSet();
    }

    public static readonly PaymentOrderStatus[] DisbursedOrderStatuses =
    [
        PaymentOrderStatus.Submitted, PaymentOrderStatus.Acknowledged, PaymentOrderStatus.Settled, PaymentOrderStatus.PartiallySettled,
    ];

    /// <summary>
    /// The batch states in which nothing was paid, or what was paid came back: its lines are to be paid again. PLAN-56 (D-PAY-16): a
    /// batch voided by an approved reversal paid nothing.
    /// </summary>
    public static readonly PaymentBatchStatus[] UnpaidBatchStatuses =
    [
        PaymentBatchStatus.Rejected, PaymentBatchStatus.Returned, PaymentBatchStatus.Failed, PaymentBatchStatus.Reversed,
        PaymentBatchStatus.Voided,
    ];

    /// <summary>
    /// The employees of these lines whose pinned account was retired and whom no person released: an <c>ACCOUNT-RETIRED</c> hold of
    /// theirs in this run is still active, or none was ever released (a retirement that met neither a line nor a snapshot leaves no
    /// hold: the money still does not go to an account the employee withdrew). A released hold is a person's decision to pay the
    /// pinned account: such a line is not listed. Neither is a line that was already disbursed when its account was retired. The
    /// disbursement gate and the treasury file both refuse while this is not empty.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> RetiredAndNotReleasedAsync(
        IQueryable<EmployeeBankAccount> accounts,
        IQueryable<PaymentHold> holds,
        IQueryable<PaymentOrder> orders,
        IQueryable<PaymentBatch> batches,
        Guid payrollRunId,
        IReadOnlyCollection<SalaryLine> lines,
        CancellationToken ct)
    {
        var pinned = await PinnedAccountsAsync(accounts, lines, ct);
        var retiredLines = lines
            .Where(l => l.BankAccountId is Guid id && pinned.TryGetValue(id, out var account) && account.Status == BankAccountStatus.Retired)
            .ToList();
        if (retiredLines.Count == 0)
            return [];
        var disbursed = await DisbursedLineIdsAsync(orders, batches, retiredLines.Select(l => l.Id).ToList(), ct);
        var retired = retiredLines.Where(l => !disbursed.Contains(l.Id)).Select(l => l.EmployeeId).Distinct().ToList();
        if (retired.Count == 0)
            return retired;

        var ofTheRun = await holds.AsNoTracking()
            .Where(h => h.PayrollRunId == payrollRunId && retired.Contains(h.EmployeeId)
                        && h.HoldType == Domain.Constants.PaymentHoldType.Manual
                        && h.Reason.StartsWith(EmployeeBankAccount.RetiredHoldRule))
            .Select(h => new { h.EmployeeId, h.Status })
            .ToListAsync(ct);
        var active = ofTheRun.Where(h => h.Status == Domain.Constants.PaymentHoldStatus.Active).Select(h => h.EmployeeId).ToHashSet();
        var released = ofTheRun.Where(h => h.Status == Domain.Constants.PaymentHoldStatus.Released).Select(h => h.EmployeeId).ToHashSet();
        return retired.Where(e => active.Contains(e) || !released.Contains(e)).Order().ToList();
    }
}

/// <summary>A pinned account as a reader needs it: its number (for the payment instruction only) and its status now.</summary>
public sealed record PinnedAccount(Guid Id, string AccountNumber, BankAccountStatus Status);

/// <summary>PLAN-55 (§2): a run's bank readiness: how many of its lines pinned an account, and which employees have none.</summary>
public sealed record GetRunBankReadinessQuery(Guid PayrollRunId) : IRequest<RunBankReadiness?>;

/// <summary>
/// The readiness of one run. A line without an account blocks neither the calculation nor the approval (the disbursement path is
/// PLAN-58's): this report is where it shows. <see cref="EmployeesWithRetiredAccount"/> are the lines whose pinned account was retired
/// since: each is behind a payment hold until a person releases it.
/// </summary>
public sealed record RunBankReadiness(
    Guid PayrollRunId,
    int Lines,
    int LinesWithAccount,
    int LinesWithoutAccount,
    IReadOnlyList<Guid> EmployeesWithoutAccount,
    IReadOnlyList<Guid> EmployeesWithRetiredAccount)
{
    /// <summary>
    /// Of <see cref="EmployeesWithRetiredAccount"/>, those the run will not pay: no person released their hold (read from the holds,
    /// not assumed). The others were released: their line is paid into the retired account it pinned.
    /// </summary>
    public IReadOnlyList<Guid> EmployeesHeldForRetiredAccount { get; init; } = [];
}

public sealed class GetRunBankReadinessHandler(IPayrollRepository repository) : IRequestHandler<GetRunBankReadinessQuery, RunBankReadiness?>
{
    public async Task<RunBankReadiness?> Handle(GetRunBankReadinessQuery request, CancellationToken cancellationToken)
    {
        if (!await repository.PayrollRuns.AsNoTracking().AnyAsync(r => r.Id == request.PayrollRunId, cancellationToken))
            return null;

        var lines = await repository.SalaryLines.AsNoTracking()
            .Where(l => l.PayrollRunId == request.PayrollRunId)
            .Select(l => new { l.Id, l.EmployeeId, l.BankAccountId })
            .ToListAsync(cancellationToken);
        var pinnedIds = lines.Where(l => l.BankAccountId.HasValue).Select(l => l.BankAccountId!.Value).Distinct().ToList();
        var retired = pinnedIds.Count == 0
            ? []
            : (await repository.EmployeeBankAccounts.AsNoTracking()
                .Where(a => pinnedIds.Contains(a.Id) && a.Status == BankAccountStatus.Retired)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

        var without = lines.Where(l => !l.BankAccountId.HasValue).Select(l => l.EmployeeId).Distinct().Order().ToList();
        var withRetired = lines.Where(l => l.BankAccountId is Guid id && retired.Contains(id)).Select(l => l.EmployeeId).Distinct().Order().ToList();
        var held = withRetired.Count == 0
            ? []
            : await BankAccountPinning.RetiredAndNotReleasedAsync(
                repository.EmployeeBankAccounts,
                repository.PaymentHolds,
                repository.PaymentOrders,
                repository.PaymentBatches,
                request.PayrollRunId,
                lines.Select(l => new SalaryLine { Id = l.Id, EmployeeId = l.EmployeeId, BankAccountId = l.BankAccountId }).ToList(),
                cancellationToken);
        return new RunBankReadiness(
            request.PayrollRunId,
            lines.Count,
            lines.Count(l => l.BankAccountId.HasValue),
            lines.Count(l => !l.BankAccountId.HasValue),
            without,
            withRetired)
        {
            EmployeesHeldForRetiredAccount = held,
        };
    }
}

/// <summary>
/// PLAN-55's naming rule, kept by PLAN-58 for the held employees (the treasury file whose refusals used it is gone): a list of people
/// shows up to <see cref="NamesShown"/> names, in order, and counts the rest; an employee whose name the directory did not give is
/// counted, never shown by an id.
/// </summary>
public static class PayrollEmployeeNames
{
    /// <summary>The most names one message lists; the rest are counted.</summary>
    public const int NamesShown = 25;

    /// <summary>«: name، name، و3 آخرون», or « (عددهم 2)» when no name is known; never an id.</summary>
    public static string Listed(IReadOnlyList<Guid> employeeIds, IReadOnlyDictionary<Guid, string> names)
    {
        var known = employeeIds.Select(id => names.TryGetValue(id, out var name) ? name?.Trim() : null)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var shown = known.Take(NamesShown).ToList();
        var others = employeeIds.Count - shown.Count;
        if (shown.Count == 0)
            return FormattableString.Invariant($" (عددهم {others})");
        return ": " + string.Join("، ", shown) + (others <= 0 ? string.Empty : FormattableString.Invariant($"، و{others} آخرون"));
    }
}
