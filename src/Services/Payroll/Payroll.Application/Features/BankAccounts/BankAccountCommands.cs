using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.BankAccounts;

/// <summary>
/// PLAN-55 (§1, D-PAY-14): a payroll processor registers an employee's bank account. It is written <c>PendingVerification</c> with the
/// registrar in <c>CreatedBy</c>, and pays nothing until another person verifies it. The account in force stays in force meanwhile.
/// </summary>
public sealed record RequestBankAccountCommand(
    Guid EmployeeId,
    BankAccountKind Kind,
    string? AccountNumber,
    string? BankCode,
    string? BankNameAr,
    string? HolderNameAr,
    Guid ActorId,
    OrgAccessScope? Scope) : IRequest<BankAccountResult>;

/// <summary>
/// PLAN-55 (§1, rule 1): a payroll approver who is not the registrar verifies a pending account against the bank's confirmation, whose
/// reference is mandatory. The account becomes <c>Active</c>; the account that was in force is retired in the same save.
/// </summary>
public sealed record VerifyBankAccountCommand(
    Guid EmployeeId,
    Guid AccountId,
    string? VerificationReference,
    Guid ActorId,
    OrgAccessScope? Scope) : IRequest<BankAccountResult>;

/// <summary>PLAN-55 (§1): a pending account is rejected, with a reason. Nothing else changes.</summary>
public sealed record RejectBankAccountCommand(
    Guid EmployeeId,
    Guid AccountId,
    string? Reason,
    Guid ActorId,
    OrgAccessScope? Scope) : IRequest<BankAccountResult>;

/// <summary>
/// PLAN-55 (§1, rule 5): the account in force is retired, with a reason. A run line that had pinned it and is not yet disbursed gets a
/// payment hold, so no money goes to an account the employee withdrew.
/// </summary>
public sealed record RetireBankAccountCommand(
    Guid EmployeeId,
    Guid AccountId,
    string? Reason,
    Guid ActorId,
    OrgAccessScope? Scope) : IRequest<BankAccountResult>;

/// <summary>What the four commands share: who sees an employee, and what a retirement does to the runs that pinned the account.</summary>
public sealed class BankAccountRules(IPayrollRepository repository)
{
    /// <summary>
    /// Whether the caller reaches this employee: the employee has a pay profile in the caller's agency, and the unit of the active one is
    /// in the caller's branch scope (PLAN-52: an employee with no active profile or no unit is central). Outside it the answer is 404.
    /// </summary>
    public async Task<bool> SeesEmployeeAsync(Guid employeeId, OrgAccessScope? scope, CancellationToken ct)
    {
        if (employeeId == Guid.Empty)
            return false;
        var profiles = await repository.EmployeePayProfiles.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .Select(p => new { p.IsActive, p.OrgUnitId })
            .ToListAsync(ct);
        if (profiles.Count == 0)
            return false;
        return PayProfileChangeScope.Sees(scope, profiles.FirstOrDefault(p => p.IsActive)?.OrgUnitId);
    }

    /// <summary>The account, tracked, when it belongs to this employee and the caller reaches the employee; null otherwise (404).</summary>
    public async Task<EmployeeBankAccount?> FindAsync(Guid employeeId, Guid accountId, OrgAccessScope? scope, CancellationToken ct)
    {
        if (!await SeesEmployeeAsync(employeeId, scope, ct))
            return null;
        return await repository.EmployeeBankAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.EmployeeId == employeeId, ct);
    }

    /// <summary>
    /// A retirement is a retirement, whatever caused it (a person's act, or the verification of a replacement): every run that pinned
    /// this account for the employee and has not yet disbursed his line gets one active hold for him, <c>ACCOUNT-RETIRED</c> at the
    /// start of its reason. A run pins the account on its <b>snapshot</b> first and writes the line later (a long, paused or resumed
    /// calculation), so both are read: a line that carries the account, and a snapshot that carries it while the employee has no line
    /// in that run yet. The money then waits for a person (the hold's existing release). Staged in the unit of work, unsaved.
    /// </summary>
    public async Task<int> StageRetirementHoldsAsync(
        EmployeeBankAccount account, Guid actorId, string reason, DateTime now, CancellationToken ct)
    {
        var lines = await repository.SalaryLines.AsNoTracking()
            .Where(l => l.BankAccountId == account.Id
                        && l.PayrollRun.Status != PayrollRunStatus.Reversed && l.PayrollRun.Status != PayrollRunStatus.Cancelled)
            .Select(l => new { l.Id, l.PayrollRunId, l.EmployeeId })
            .ToListAsync(ct);

        // «Not yet disbursed» is BankAccountPinning.DisbursedLineIdsAsync's rule, the one the gate and the treasury file read too.
        var disbursed = await BankAccountPinning.DisbursedLineIdsAsync(
            repository.PaymentOrders, repository.PaymentBatches, lines.Select(l => l.Id).ToList(), ct);
        var runsToHold = lines.Where(l => !disbursed.Contains(l.Id)).Select(l => l.PayrollRunId).ToHashSet();

        // The runs whose snapshot pinned the account and whose line for this employee is not written yet.
        // PLAN-56 (P-10): only the snapshot of a calculation still in hand. A job that completed wrote every line (the query above
        // reads them); if its run was reopened by a rejection, or its job was given up by a restart or a cancellation, its snapshot
        // pins nothing any more: the next calculation takes a new snapshot and pins the account in force then (D-PAY-15).
        var snapshotRuns = await repository.PayrollRunInputSnapshots.AsNoTracking()
            .Where(s => s.BankAccountId == account.Id && s.EmployeeId == account.EmployeeId
                        && s.Job.Status != PayrollRunJobStatus.Completed && s.Job.Status != PayrollRunJobStatus.Cancelled
                        && s.Job.PayrollRun.Status != PayrollRunStatus.Reversed && s.Job.PayrollRun.Status != PayrollRunStatus.Cancelled)
            .Select(s => s.Job.PayrollRunId)
            .Distinct()
            .ToListAsync(ct);
        if (snapshotRuns.Count > 0)
        {
            var runsWithALine = (await repository.SalaryLines.AsNoTracking()
                    .Where(l => snapshotRuns.Contains(l.PayrollRunId) && l.EmployeeId == account.EmployeeId)
                    .Select(l => l.PayrollRunId)
                    .ToListAsync(ct))
                .ToHashSet();
            foreach (var run in snapshotRuns.Where(run => !runsWithALine.Contains(run)))
                runsToHold.Add(run);
        }

        if (runsToHold.Count == 0)
            return 0;

        var held = (await repository.PaymentHolds.AsNoTracking()
                .Where(h => h.EmployeeId == account.EmployeeId
                            && h.PayrollRunId != null && runsToHold.Contains(h.PayrollRunId.Value)
                            && h.Status == PaymentHoldStatus.Active
                            && h.HoldType == PaymentHoldType.Manual
                            && h.Reason.StartsWith(EmployeeBankAccount.RetiredHoldRule))
                .Select(h => h.PayrollRunId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var placed = 0;
        foreach (var run in runsToHold.Where(run => !held.Contains(run)).Order())
        {
            await repository.AddAsync(new PaymentHold
            {
                EmployeeId = account.EmployeeId,
                PayrollRunId = run,
                Reason = BankAccountMessages.RetiredHoldReason(reason),
                HoldType = PaymentHoldType.Manual,
                Status = PaymentHoldStatus.Active,
                HeldAtUtc = now,
                CreatedBy = actorId,
                TenantId = account.TenantId,
            }, ct);
            placed++;
        }

        return placed;
    }

    /// <summary>Marks the account retired (unsaved) and stages its holds.</summary>
    public async Task<int> RetireAsync(EmployeeBankAccount account, Guid actorId, string reason, DateTime now, CancellationToken ct)
    {
        account.Status = BankAccountStatus.Retired;
        account.RetiredBy = actorId;
        account.RetiredAt = now;
        account.RetirementReason = reason;
        account.UpdatedBy = actorId;
        account.UpdatedAt = now;
        return await StageRetirementHoldsAsync(account, actorId, reason, now, ct);
    }

    /// <summary>The save of a decision: a concurrent change of the same account, or a second account in a state only one may hold, is a conflict.</summary>
    public async Task<BankAccountResult?> SaveAsync(EmployeeBankAccount account, BankAccountStatus before, CancellationToken ct)
    {
        try
        {
            await repository.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.ClearChangeTracker();
            return new BankAccountResult(false, account.Id, before, BankAccountMessages.ChangedConcurrently, BankAccountFailure.Conflict);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            repository.ClearChangeTracker();
            return new BankAccountResult(false, account.Id, before, BankAccountMessages.ChangedConcurrently, BankAccountFailure.Conflict);
        }
    }
}

public sealed class RequestBankAccountHandler(
    IPayrollRepository repository,
    BankAccountRules rules,
    IOptions<PayrollBankOptions> options,
    IPayrollCalendar calendar) : IRequestHandler<RequestBankAccountCommand, BankAccountResult>
{
    public async Task<BankAccountResult> Handle(RequestBankAccountCommand request, CancellationToken cancellationToken)
    {
        // An act with no known actor is refused: the verifier's check must never meet an empty registrar.
        if (request.ActorId == Guid.Empty)
            return Invalid(BankAccountMessages.UnknownActor);
        if (!await rules.SeesEmployeeAsync(request.EmployeeId, request.Scope, cancellationToken))
            return BankAccountResult.Fail(BankAccountFailure.NotFound, BankAccountMessages.EmployeeNotFound);
        if (!Enum.IsDefined(request.Kind))
            return Invalid(BankAccountMessages.KindUnknown);

        var bankCode = string.IsNullOrWhiteSpace(request.BankCode) ? null : request.BankCode.Trim();
        var bank = options.Value.Find(bankCode);
        var check = BankAccountNumberValidator.Validate(request.Kind, request.AccountNumber, bankCode is not null, bank?.AccountRegex);
        if (!check.IsValid)
            return Invalid(BankAccountMessages.Of(check.Error));

        // A defined bank gives its own name; otherwise (an IBAN with no bank code) the name is the registrar's text.
        var bankName = bank is not null ? bank.NameAr.Trim() : request.BankNameAr?.Trim();
        if (string.IsNullOrEmpty(bankName))
            return Invalid(BankAccountMessages.BankNameRequired);
        if (bankName.Length > EmployeeBankAccount.BankNameMaxLength)
            return Invalid(BankAccountMessages.BankNameTooLong);
        var holder = string.IsNullOrWhiteSpace(request.HolderNameAr) ? null : request.HolderNameAr.Trim();
        if (holder is { Length: > EmployeeBankAccount.HolderNameMaxLength })
            return Invalid(BankAccountMessages.HolderNameTooLong);

        var open = await repository.EmployeeBankAccounts.AsNoTracking()
            .Where(a => a.EmployeeId == request.EmployeeId
                        && (a.Status == BankAccountStatus.PendingVerification || a.Status == BankAccountStatus.Active))
            .Select(a => new { a.Id, a.Status, a.AccountKind, a.AccountNumber })
            .ToListAsync(cancellationToken);
        if (open.Any(a => a.Status == BankAccountStatus.PendingVerification))
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.PendingExists);
        var inForce = open.FirstOrDefault(a => a.Status == BankAccountStatus.Active);
        if (inForce is not null && inForce.AccountKind == request.Kind
            && string.Equals(inForce.AccountNumber, check.Normalized, StringComparison.Ordinal))
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.AlreadyInForce);

        var now = calendar.UtcNow;
        var account = new EmployeeBankAccount
        {
            EmployeeId = request.EmployeeId,
            AccountKind = request.Kind,
            AccountNumber = check.Normalized,
            BankCode = bank?.Code.Trim(),
            BankNameAr = bankName,
            HolderNameAr = holder,
            Status = BankAccountStatus.PendingVerification,
            SupersedesAccountId = inForce?.Id,
            CreatedBy = request.ActorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await repository.AddAsync(account, cancellationToken);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // Another registration for the same employee was saved between the read and the save (the one-pending index).
            repository.ClearChangeTracker();
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.PendingExists);
        }

        return BankAccountResult.Ok(account);
    }

    private static BankAccountResult Invalid(string error) => BankAccountResult.Fail(BankAccountFailure.Invalid, error);
}

public sealed class VerifyBankAccountHandler(IPayrollRepository repository, BankAccountRules rules, IPayrollCalendar calendar)
    : IRequestHandler<VerifyBankAccountCommand, BankAccountResult>
{
    public async Task<BankAccountResult> Handle(VerifyBankAccountCommand request, CancellationToken cancellationToken)
    {
        if (request.ActorId == Guid.Empty)
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.UnknownActor);
        var account = await rules.FindAsync(request.EmployeeId, request.AccountId, request.Scope, cancellationToken);
        if (account is null)
            return BankAccountResult.Fail(BankAccountFailure.NotFound, BankAccountMessages.AccountNotFound);
        if (account.Status != BankAccountStatus.PendingVerification)
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.NotPending, account);
        // An account the migration moved has no registrar: nobody can be shown to be a second person, so it is never verified.
        if (account.CreatedBy is not Guid registrar || registrar == Guid.Empty)
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.NoRegistrar, account);
        if (registrar == request.ActorId)
            return BankAccountResult.Fail(BankAccountFailure.SelfVerification, BankAccountMessages.SelfVerification, account);
        var reference = request.VerificationReference?.Trim();
        if (string.IsNullOrEmpty(reference))
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.ReferenceRequired, account);
        if (reference.Length > EmployeeBankAccount.ReferenceMaxLength)
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.ReferenceTooLong, account);

        var now = calendar.UtcNow;
        var before = account.Status;

        // The account in force is retired by this verification, and a line that pinned it and is not yet disbursed gets the same hold a
        // person's retirement places.
        var inForce = await repository.EmployeeBankAccounts
            .FirstOrDefaultAsync(a => a.EmployeeId == account.EmployeeId && a.Status == BankAccountStatus.Active, cancellationToken);
        var holds = 0;
        if (inForce is not null)
            holds = await rules.RetireAsync(inForce, request.ActorId, BankAccountMessages.ReplacedByVerifiedAccount, now, cancellationToken);

        account.Status = BankAccountStatus.Active;
        account.VerifiedBy = request.ActorId;
        account.VerifiedAt = now;
        account.VerificationReference = reference;
        account.UpdatedBy = request.ActorId;
        account.UpdatedAt = now;

        if (await rules.SaveAsync(account, before, cancellationToken) is { } conflict)
            return conflict;
        return BankAccountResult.Ok(account) with { HoldsPlaced = holds, RetiredAccountId = inForce?.Id };
    }
}

public sealed class RejectBankAccountHandler(BankAccountRules rules, IPayrollCalendar calendar)
    : IRequestHandler<RejectBankAccountCommand, BankAccountResult>
{
    public async Task<BankAccountResult> Handle(RejectBankAccountCommand request, CancellationToken cancellationToken)
    {
        if (request.ActorId == Guid.Empty)
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.UnknownActor);
        var account = await rules.FindAsync(request.EmployeeId, request.AccountId, request.Scope, cancellationToken);
        if (account is null)
            return BankAccountResult.Fail(BankAccountFailure.NotFound, BankAccountMessages.AccountNotFound);
        if (account.Status != BankAccountStatus.PendingVerification)
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.NotPending, account);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.RejectReasonRequired, account);
        if (reason.Length > EmployeeBankAccount.ReasonMaxLength)
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.ReasonTooLong, account);

        var now = calendar.UtcNow;
        var before = account.Status;
        account.Status = BankAccountStatus.Rejected;
        account.RejectedBy = request.ActorId;
        account.RejectedAt = now;
        account.RejectionReason = reason;
        account.UpdatedBy = request.ActorId;
        account.UpdatedAt = now;

        if (await rules.SaveAsync(account, before, cancellationToken) is { } conflict)
            return conflict;
        return BankAccountResult.Ok(account);
    }
}

public sealed class RetireBankAccountHandler(BankAccountRules rules, IPayrollCalendar calendar)
    : IRequestHandler<RetireBankAccountCommand, BankAccountResult>
{
    public async Task<BankAccountResult> Handle(RetireBankAccountCommand request, CancellationToken cancellationToken)
    {
        if (request.ActorId == Guid.Empty)
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.UnknownActor);
        var account = await rules.FindAsync(request.EmployeeId, request.AccountId, request.Scope, cancellationToken);
        if (account is null)
            return BankAccountResult.Fail(BankAccountFailure.NotFound, BankAccountMessages.AccountNotFound);
        if (account.Status != BankAccountStatus.Active)
            return BankAccountResult.Fail(BankAccountFailure.Conflict, BankAccountMessages.NotActive, account);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.RetireReasonRequired, account);
        if (reason.Length > EmployeeBankAccount.ReasonMaxLength)
            return BankAccountResult.Fail(BankAccountFailure.Invalid, BankAccountMessages.ReasonTooLong, account);

        var before = account.Status;
        var holds = await rules.RetireAsync(account, request.ActorId, reason, calendar.UtcNow, cancellationToken);
        if (await rules.SaveAsync(account, before, cancellationToken) is { } conflict)
            return conflict;
        return BankAccountResult.Ok(account) with { HoldsPlaced = holds };
    }
}
