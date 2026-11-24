using System.ComponentModel.DataAnnotations;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>PLAN-55: an employee's bank accounts, and what the caller may do with each.</summary>
public sealed class EmployeeBankAccountsViewModel
{
    public Guid EmployeeId { get; init; }
    public IReadOnlyList<EmployeeBankAccount> Accounts { get; init; } = [];
    public bool CanRegister { get; init; }
    public bool CanApprove { get; init; }

    /// <summary>A registration is possible while no account waits for verification.</summary>
    public bool HasPending => Accounts.Any(a => a.Status == BankAccountStatus.PendingVerification);
}

/// <summary>PLAN-55: the accounts that wait for verification, in the caller's scope.</summary>
public sealed class PendingBankAccountsViewModel
{
    public IReadOnlyList<EmployeeBankAccount> Accounts { get; init; } = [];
    public bool CanApprove { get; init; }
    public Guid CurrentUserId { get; init; }
}

/// <summary>
/// PLAN-55: the registration form. An allowlist of what a processor may say: no entity is bound, so the status, the registrar and the
/// verification never come from the browser.
/// </summary>
public sealed class RegisterBankAccountViewModel
{
    [Required(ErrorMessage = "اختر الموظف")]
    [Display(Name = "الموظف")]
    public Guid? EmployeeId { get; set; }

    [Display(Name = "نوع المعرّف")]
    public BankAccountKind AccountKind { get; set; } = BankAccountKind.Iban;

    [Required(ErrorMessage = BankAccountMessages.NumberMissing)]
    [MaxLength(60, ErrorMessage = BankAccountMessages.NumberTooLong)]
    [Display(Name = "رقم الحساب")]
    public string? AccountNumber { get; set; }

    [MaxLength(EmployeeBankAccount.BankCodeMaxLength)]
    [Display(Name = "المصرف المعرّف")]
    public string? BankCode { get; set; }

    [MaxLength(EmployeeBankAccount.BankNameMaxLength, ErrorMessage = BankAccountMessages.BankNameTooLong)]
    [Display(Name = "اسم المصرف")]
    public string? BankNameAr { get; set; }

    [MaxLength(EmployeeBankAccount.HolderNameMaxLength, ErrorMessage = BankAccountMessages.HolderNameTooLong)]
    [Display(Name = "اسم صاحب الحساب كما في المصرف")]
    public string? HolderNameAr { get; set; }

    /// <summary>The banks whose local numbers are accepted (empty until the bank file is adopted): for the form's list, not posted.</summary>
    public IReadOnlyList<PayrollBankDefinition> Banks { get; set; } = [];
}

/// <summary>
/// PLAN-55: the approver's verification page, the only place the number is shown whole (to be compared with the bank's confirmation).
/// </summary>
public sealed class VerifyBankAccountViewModel
{
    public required EmployeeBankAccount Account { get; init; }

    /// <summary>The account in force that this verification would retire, if any (shown masked).</summary>
    public EmployeeBankAccount? InForce { get; init; }

    /// <summary>The caller registered this account: another approver verifies it.</summary>
    public bool IsOwnRegistration { get; init; }

    /// <summary>The account has no known registrar (moved from old data): it can only be rejected.</summary>
    public bool HasNoRegistrar { get; init; }

    public bool CanVerify => !IsOwnRegistration && !HasNoRegistrar && Account.Status == BankAccountStatus.PendingVerification;

    /// <summary>The whole number is shown only to a person who can verify the account: while it waits, and never to its registrar.</summary>
    public bool ShowWholeNumber => CanVerify;

    public string? VerificationReference { get; init; }
}
