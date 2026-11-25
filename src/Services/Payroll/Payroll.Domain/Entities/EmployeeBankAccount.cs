using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-55 (G-PAY-02, D-PAY-11, D-PAY-14): the bank account an employee's pay goes to. A payroll processor registers it
/// (<see cref="AuditableEntity.CreatedBy"/> is the registrar) and a payroll approver who is not that person verifies it against the bank's
/// confirmation; only then is it <see cref="BankAccountStatus.Active"/>. A change of account is a new registration: the account in force
/// stays in force until its replacement is verified, and is then <see cref="BankAccountStatus.Retired"/>. A run pins the account that is
/// in force at its snapshot (<see cref="PayrollRunInputSnapshot.BankAccountId"/>), so nothing done here afterwards moves that run's money.
/// <para>
/// The number is personal financial data: it is redacted in the audit log, shown masked everywhere but on the approver's verification
/// page, and written to no log line, error message or URL. Encryption at rest is wave 2 (SOV).
/// </para>
/// </summary>
[Display(Name = "حساب مصرفي لموظف")]
public class EmployeeBankAccount : AuditableEntity
{
    public const int AccountNumberMaxLength = 34;
    public const int BankCodeMaxLength = 20;
    public const int BankNameMaxLength = 120;
    public const int HolderNameMaxLength = 200;
    public const int ReferenceMaxLength = 100;
    public const int ReasonMaxLength = 500;

    /// <summary>The rule code at the start of the reason of the hold a retirement places (PAY-029's pattern).</summary>
    public const string RetiredHoldRule = "ACCOUNT-RETIRED";

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع المعرّف")]
    public BankAccountKind AccountKind { get; set; }

    /// <summary>The account's identifier, normalised (no spaces, upper case): an IBAN or a bank's local number.</summary>
    [Required]
    [MaxLength(AccountNumberMaxLength)]
    [AuditRedact]
    [Display(Name = "رقم الحساب")]
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>The bank's code in <c>Payroll:Bank:Banks</c>. Mandatory for a local number; optional for an IBAN.</summary>
    [MaxLength(BankCodeMaxLength)]
    [Display(Name = "رمز المصرف")]
    public string? BankCode { get; set; }

    [MaxLength(BankNameMaxLength)]
    [Display(Name = "المصرف")]
    public string? BankNameAr { get; set; }

    /// <summary>The holder's name as the bank writes it. Personal data: redacted in the audit log.</summary>
    [MaxLength(HolderNameMaxLength)]
    [AuditRedact]
    [Display(Name = "اسم صاحب الحساب")]
    public string? HolderNameAr { get; set; }

    [Display(Name = "الحالة")]
    public BankAccountStatus Status { get; set; } = BankAccountStatus.PendingVerification;

    [Display(Name = "تحقق منه")]
    public Guid? VerifiedBy { get; set; }

    [Display(Name = "تاريخ التحقق")]
    public DateTime? VerifiedAt { get; set; }

    /// <summary>The reference of the bank's confirmation the verifier compared the number with.</summary>
    [MaxLength(ReferenceMaxLength)]
    [Display(Name = "مرجع تأكيد المصرف")]
    public string? VerificationReference { get; set; }

    [Display(Name = "رفضه")]
    public Guid? RejectedBy { get; set; }

    [Display(Name = "تاريخ الرفض")]
    public DateTime? RejectedAt { get; set; }

    [MaxLength(ReasonMaxLength)]
    [Display(Name = "سبب الرفض")]
    public string? RejectionReason { get; set; }

    [Display(Name = "أحاله")]
    public Guid? RetiredBy { get; set; }

    [Display(Name = "تاريخ الإحالة")]
    public DateTime? RetiredAt { get; set; }

    [MaxLength(ReasonMaxLength)]
    [Display(Name = "سبب الإحالة")]
    public string? RetirementReason { get; set; }

    /// <summary>The account that was in force when this one was registered: the one its verification retires.</summary>
    [Display(Name = "يحل محل")]
    public Guid? SupersedesAccountId { get; set; }

    /// <summary>The number as every list and API answer shows it: its last four characters, the rest masked.</summary>
    public string MaskedNumber => Mask(AccountNumber);

    /// <summary>Masks a number to its last four characters. A number of four characters or fewer is masked whole.</summary>
    public static string Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return string.Empty;
        const int shown = 4;
        return number.Length <= shown
            ? new string('•', number.Length)
            : new string('•', number.Length - shown) + number[^shown..];
    }
}
