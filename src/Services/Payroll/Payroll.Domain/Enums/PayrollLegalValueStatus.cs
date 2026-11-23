using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>
/// PLAN-54: the legal standing of one value of a payroll framework. A <see cref="Signed"/> value rests on an enacted source; an
/// <see cref="Interim"/> value is allowed only under an active STR-002 exception that the value cites.
/// </summary>
public enum PayrollLegalValueStatus
{
    [Display(Name = "موقّعة")]
    Signed = 0,
    [Display(Name = "مؤقتة")]
    Interim = 1
}

/// <summary>PLAN-54: what a statutory deduction's percentage is taken of.</summary>
public enum StatutoryDeductionBaseKind
{
    [Display(Name = "إجمالي الأجر")]
    GrossPay = 0,
    [Display(Name = "الراتب الأساسي")]
    BaseSalary = 1
}

/// <summary>PLAN-54: how a person entered the record of a framework version's editors.</summary>
public enum PayrollFrameworkEditKind
{
    [Display(Name = "إنشاء")]
    Created = 0,
    [Display(Name = "استيراد")]
    Imported = 1,
    [Display(Name = "تعديل")]
    Edited = 2
}
