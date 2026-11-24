using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-54 (§2): a statutory deduction inside a framework version (income tax withheld as a rate, social insurance, health insurance,
/// pension). A run of a version of fingerprint v2 reads these rows only; the general <see cref="DeductionType"/> table no longer
/// decides it. The employee's share is <c>min(base, Ceiling) × EmployeePercent / 100 + FixedAmount</c>; the employer's share is
/// reported by the insurance export and never deducted from the employee.
/// </summary>
[Display(Name = "خصم نظامي")]
public class StatutoryDeductionRule : BaseEntity, ILegallySourcedValue
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int InsuranceBodyMaxLength = 20;

    /// <summary>The insurance bodies a rule may name; a rule that names one is reported by the insurance export.</summary>
    public static readonly IReadOnlyList<string> InsuranceBodies = ["GOSI", "GOIP"];

    public Guid PayrollConfigurationId { get; set; }

    [Required, MaxLength(CodeMaxLength)]
    [Display(Name = "الرمز")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(NameMaxLength)]
    [Display(Name = "الاسم")]
    public string NameAr { get; set; } = string.Empty;

    [Display(Name = "حصة الموظف %")]
    public decimal EmployeePercent { get; set; }

    [Display(Name = "حصة صاحب العمل %")]
    public decimal EmployerPercent { get; set; }

    [Display(Name = "مبلغ ثابت")]
    public decimal FixedAmount { get; set; }

    [Display(Name = "وعاء الاحتساب")]
    public StatutoryDeductionBaseKind BaseKind { get; set; } = StatutoryDeductionBaseKind.GrossPay;

    /// <summary>The most of the base the percentages apply to; null for no ceiling.</summary>
    [Display(Name = "سقف الوعاء")]
    public decimal? Ceiling { get; set; }

    /// <summary>GOSI or GOIP when the rule is an insurance contribution; null otherwise.</summary>
    [MaxLength(InsuranceBodyMaxLength)]
    [Display(Name = "جهة التأمين")]
    public string? InsuranceBody { get; set; }

    [MaxLength(LegalValueRules.LegalReferenceMaxLength)]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }

    [Display(Name = "الحالة القانونية")]
    public PayrollLegalValueStatus LegalStatus { get; set; } = PayrollLegalValueStatus.Interim;

    [MaxLength(LegalValueRules.InterimExceptionIdMaxLength)]
    [Display(Name = "الاستثناء المؤقت")]
    public string? InterimExceptionId { get; set; }

    [Display(Name = "الترتيب")]
    public int SortOrder { get; set; }

    public PayrollConfiguration PayrollConfiguration { get; set; } = null!;

    public string DescribeAr() => $"الخصم النظامي {Code}";

    /// <summary>The base the percentages apply to, after the ceiling.</summary>
    public decimal CappedBase(decimal grossPay, decimal baseSalary)
    {
        var amount = BaseKind == StatutoryDeductionBaseKind.BaseSalary ? baseSalary : grossPay;
        return Ceiling is decimal ceiling && amount > ceiling ? ceiling : amount;
    }
}
