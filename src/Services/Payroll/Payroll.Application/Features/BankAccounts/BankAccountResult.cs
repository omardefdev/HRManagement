using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.BankAccounts;

/// <summary>
/// PLAN-55 (D-PAY-11): the banks whose local account numbers are accepted. Section <c>Payroll:Bank</c>. The list is empty in Production
/// and Staging until the bank file is adopted (SYD-014): there, only an IBAN is accepted.
/// </summary>
public sealed class PayrollBankOptions
{
    public const string SectionName = "Payroll:Bank";

    private List<PayrollBankDefinition> _banks = [];

    /// <summary>Never null: an empty list in a settings file or in the environment (<c>Payroll__Bank__Banks=""</c>) is no bank.</summary>
    public List<PayrollBankDefinition> Banks
    {
        get => _banks;
        set => _banks = value ?? [];
    }

    /// <summary>
    /// What is wrong with the list, for the start-up check: a bank without a code or a name, two banks of one code, a pattern that
    /// does not compile. Empty when the list is usable (an empty list is).
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        for (var i = 0; i < Banks.Count; i++)
        {
            var bank = Banks[i];
            if (string.IsNullOrWhiteSpace(bank.Code) || string.IsNullOrWhiteSpace(bank.NameAr))
                problems.Add($"{SectionName}:Banks:{i} needs a code and an Arabic name.");
            if (!BankAccountNumberValidator.IsUsablePattern(bank.AccountRegex))
                problems.Add($"{SectionName}:Banks:{i}:AccountRegex is empty or does not compile.");
        }

        foreach (var duplicate in Banks.Where(b => !string.IsNullOrWhiteSpace(b.Code)).GroupBy(b => b.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"{SectionName}:Banks defines the code {duplicate.Key} more than once.");
        return problems;
    }

    /// <summary>The bank a code names, or null. Codes are compared without case and without surrounding space.</summary>
    public PayrollBankDefinition? Find(string? code)
    {
        var wanted = code?.Trim();
        return string.IsNullOrEmpty(wanted)
            ? null
            : Banks.FirstOrDefault(b => string.Equals(b.Code?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One bank: its code, its Arabic name, and the pattern a local account number of it must match whole.</summary>
public sealed class PayrollBankDefinition
{
    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string AccountRegex { get; set; } = string.Empty;
}

/// <summary>Why a bank account command was refused, and so its HTTP answer (the same for the screens and the API).</summary>
public enum BankAccountFailure
{
    None = 0,

    /// <summary>A broken input rule (the number's check, an unknown bank, a missing reference or reason, an unknown actor): 400.</summary>
    Invalid = 1,

    /// <summary>The registrar verifying his own registration: 400 (the plan's AC-PAY-55a).</summary>
    SelfVerification = 2,

    /// <summary>A second pending account, a concurrent decision, a state that does not take the act: 409.</summary>
    Conflict = 3,

    /// <summary>No such employee or account in the caller's agency and branch scope: 404.</summary>
    NotFound = 4,
}

/// <summary>
/// The answer of every bank account command. A refusal changes nothing: <see cref="Status"/> is then the account's unchanged status (when
/// there is an account), and <see cref="Error"/> an Arabic message that never carries the number.
/// </summary>
public sealed record BankAccountResult(
    bool Success,
    Guid? AccountId,
    BankAccountStatus? Status,
    string? Error,
    BankAccountFailure Failure = BankAccountFailure.None)
{
    /// <summary>The holds a retirement placed: the lines of runs not yet disbursed that had pinned the retired account.</summary>
    public int HoldsPlaced { get; init; }

    /// <summary>The account in force that this verification retired, if any.</summary>
    public Guid? RetiredAccountId { get; init; }

    public static BankAccountResult Ok(EmployeeBankAccount account) => new(true, account.Id, account.Status, null);

    public static BankAccountResult Fail(BankAccountFailure failure, string error, EmployeeBankAccount? account = null) =>
        new(false, account?.Id, account?.Status, error, failure);

    public int StatusCode => Failure switch
    {
        BankAccountFailure.None => 200,
        BankAccountFailure.Conflict => 409,
        BankAccountFailure.NotFound => 404,
        _ => 400,
    };
}

/// <summary>The Arabic messages of the bank account's acts. None of them names an account number.</summary>
public static class BankAccountMessages
{
    public const string UnknownActor = "تعذّر تحديد هوية المستخدم";
    public const string EmployeeNotFound = "الموظف غير موجود ضمن نطاقك";
    public const string AccountNotFound = "الحساب المصرفي غير موجود";
    public const string NumberMissing = "رقم الحساب إلزامي";
    public const string IbanForm = "صيغة IBAN غير صحيحة: حرفان لرمز البلد ثم رقمان للتحقق ثم أحرف وأرقام، بطول بين 15 و34 خانة";
    public const string IbanCheckDigits = "رقم IBAN غير صحيح: خانتا التحقق لا تطابقان الرقم";
    public const string BankRequired = "رمز المصرف إلزامي لرقم الحساب المحلي";
    public const string UnknownBank = "المصرف غير معرّف في إعدادات الرواتب؛ يُقبل رقم IBAN فقط إلى أن يُعرَّف المصرف";
    public const string LocalForm = "رقم الحساب لا يطابق صيغة أرقام هذا المصرف";
    public const string NumberTooLong = "رقم الحساب أطول من 34 خانة";
    public const string BankNameRequired = "اسم المصرف إلزامي";
    public const string BankNameTooLong = "اسم المصرف أطول من 120 حرفاً";
    public const string HolderNameTooLong = "اسم صاحب الحساب أطول من 200 حرف";
    public const string KindUnknown = "نوع معرّف الحساب غير معروف";
    public const string PendingExists = "للموظف حساب بانتظار التحقق؛ يُتحقق منه أو يُرفض قبل تسجيل حساب آخر";
    public const string AlreadyInForce = "هذا الحساب هو الحساب النافذ للموظف";
    public const string NotPending = "الحساب ليس بانتظار التحقق";
    public const string NotActive = "الحساب ليس نافذاً؛ لا يُحال إلا الحساب النافذ";
    public const string SelfVerification = "لا يتحقق من الحساب مَن سجّله: يتحقق منه شخص ثانٍ";
    public const string NoRegistrar = "لا يُعرف مسجِّل هذا الحساب (حساب مرحَّل من بيانات قديمة)؛ ارفضه ثم سجّله من جديد";
    public const string ReferenceRequired = "مرجع تأكيد المصرف إلزامي";
    public const string ReferenceTooLong = "مرجع تأكيد المصرف أطول من 100 حرف";
    public const string RejectReasonRequired = "سبب الرفض إلزامي";
    public const string RetireReasonRequired = "سبب الإحالة إلزامي";
    public const string ReasonTooLong = "السبب أطول من 500 حرف";
    public const string ChangedConcurrently = "تغيّر الحساب للتو من مستخدم آخر؛ أعد فتحه";
    public const string ReplacedByVerifiedAccount = "استُبدل بحساب جديد جرى التحقق منه";

    public static string Of(BankAccountNumberError error) => error switch
    {
        BankAccountNumberError.Missing => NumberMissing,
        BankAccountNumberError.IbanForm => IbanForm,
        BankAccountNumberError.IbanCheckDigits => IbanCheckDigits,
        BankAccountNumberError.BankRequired => BankRequired,
        BankAccountNumberError.UnknownBank => UnknownBank,
        BankAccountNumberError.LocalForm => LocalForm,
        BankAccountNumberError.TooLong => NumberTooLong,
        _ => NumberMissing,
    };

    /// <summary>
    /// The reason of the hold a retirement places on a run that had pinned the account. It names no number and no id; and it says
    /// what a release means, because the run's line keeps the account it pinned.
    /// </summary>
    public static string RetiredHoldReason(string reason) =>
        $"{EmployeeBankAccount.RetiredHoldRule}: أُحيل الحساب المصرفي المثبَّت للموظف في هذا التشغيل قبل صرفه — {reason}. "
        + "الإفراج عن هذا الحجز يعني الصرف إلى الحساب المحال نفسه؛ وإن لم يُجمَّد التشغيل فإعادة حسابه تثبّت الحساب النافذ.";
}
