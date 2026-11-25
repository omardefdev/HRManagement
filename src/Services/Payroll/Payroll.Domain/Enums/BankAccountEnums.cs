using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>PLAN-55 (D-PAY-11): how an employee's bank account is identified.</summary>
public enum BankAccountKind
{
    /// <summary>An IBAN (ISO 13616): always accepted after the mod 97 check.</summary>
    [Display(Name = "IBAN")]
    Iban = 0,

    /// <summary>A local account number of a bank defined in <c>Payroll:Bank:Banks</c>, checked by that bank's pattern.</summary>
    [Display(Name = "رقم حساب محلي")]
    Local = 1,
}

/// <summary>
/// PLAN-55 (D-PAY-14): the life of an employee's bank account. It is registered <see cref="PendingVerification"/> by one person and becomes
/// <see cref="Active"/> only when another person verifies it against the bank's confirmation. The numbers are stored and are part of the
/// filter of <c>IX_EmployeeBankAccounts_OneActiveOnePending</c>: never renumber them.
/// </summary>
public enum BankAccountStatus
{
    [Display(Name = "بانتظار التحقق")]
    PendingVerification = 0,

    [Display(Name = "نافذ")]
    Active = 1,

    [Display(Name = "مرفوض")]
    Rejected = 2,

    [Display(Name = "محال")]
    Retired = 3,
}
