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
    IReadOnlyList<Guid> EmployeesWithRetiredAccount);

public sealed class GetRunBankReadinessHandler(IPayrollRepository repository) : IRequestHandler<GetRunBankReadinessQuery, RunBankReadiness?>
{
    public async Task<RunBankReadiness?> Handle(GetRunBankReadinessQuery request, CancellationToken cancellationToken)
    {
        if (!await repository.PayrollRuns.AsNoTracking().AnyAsync(r => r.Id == request.PayrollRunId, cancellationToken))
            return null;

        var lines = await repository.SalaryLines.AsNoTracking()
            .Where(l => l.PayrollRunId == request.PayrollRunId)
            .Select(l => new { l.EmployeeId, l.BankAccountId })
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
        return new RunBankReadiness(
            request.PayrollRunId,
            lines.Count,
            lines.Count(l => l.BankAccountId.HasValue),
            lines.Count(l => !l.BankAccountId.HasValue),
            without,
            withRetired);
    }
}

/// <summary>
/// PLAN-55 (§2): the treasury file of a run is refused while a line of it has no pinned account. The refusal lists the employees:
/// <see cref="EmployeeIds"/> for an API, and their names in the message for a screen (an employee whose name the directory did not give
/// is counted, never shown by an id).
/// </summary>
public sealed class MissingPinnedBankAccountsException : InvalidOperationException
{
    /// <summary>The most names one message lists; the rest are counted.</summary>
    public const int NamesShown = 25;

    public MissingPinnedBankAccountsException(IReadOnlyList<Guid> employeeIds, IReadOnlyDictionary<Guid, string> names)
        : base(Describe(employeeIds, names))
    {
        EmployeeIds = employeeIds;
    }

    public IReadOnlyList<Guid> EmployeeIds { get; }

    private static string Describe(IReadOnlyList<Guid> employeeIds, IReadOnlyDictionary<Guid, string> names)
    {
        var known = employeeIds.Select(id => names.TryGetValue(id, out var name) ? name?.Trim() : null)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var shown = known.Take(NamesShown).ToList();
        var others = employeeIds.Count - shown.Count;
        var list = shown.Count == 0 ? string.Empty : ": " + string.Join("، ", shown);
        var rest = others <= 0
            ? string.Empty
            : shown.Count == 0
                ? FormattableString.Invariant($" (عددهم {others})")
                : FormattableString.Invariant($"، و{others} آخرون");
        return "لا يمكن تصدير ملف الصرف: موظفون بلا حساب مصرفي مثبَّت في هذا التشغيل"
               + list + rest
               + ". سجّل الحساب وتحقّق منه؛ يُثبَّت في التشغيل عند حسابه، والقائمة الكاملة في تقرير جاهزية الحسابات للتشغيل.";
    }
}
