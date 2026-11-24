using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-54 (§2): the legal source of one numeric field of a framework version (<see cref="FieldName"/> is the property's name, for
/// example <c>TaxExemptionAmount</c>). A version of fingerprint v2 has one row for every field in
/// <see cref="GovernedFields"/>, and the rows are inside the fingerprint.
/// </summary>
[Display(Name = "مرجع حقل الإطار")]
public class PayrollConfigurationFieldSource : BaseEntity, ILegallySourcedValue
{
    public const int FieldNameMaxLength = 100;

    /// <summary>The fields of <see cref="PayrollConfiguration"/> that govern pay: the ones the fingerprint covers.</summary>
    public static readonly IReadOnlyList<string> GovernedFields =
    [
        nameof(PayrollConfiguration.ProrationDaysBasis),
        nameof(PayrollConfiguration.DailyRateDivisor),
        nameof(PayrollConfiguration.MissionAllowanceAmount),
        nameof(PayrollConfiguration.SecondmentAllowancePercent),
        nameof(PayrollConfiguration.EnableTrainingPremiums),
        nameof(PayrollConfiguration.TrainingPremiumAmount),
        nameof(PayrollConfiguration.MedicalCompensationDefaultAmount),
        nameof(PayrollConfiguration.MaxRetroCorrectionMonths),
        nameof(PayrollConfiguration.EosGratuityRateFirstPeriod),
        nameof(PayrollConfiguration.EosGratuityFirstYearsThreshold),
        nameof(PayrollConfiguration.EosGratuityRateAfterThreshold),
        nameof(PayrollConfiguration.NonWorkingDays),
        nameof(PayrollConfiguration.UseDbHolidaysOnly),
        nameof(PayrollConfiguration.TaxableBaseMode),
        nameof(PayrollConfiguration.TaxExemptionAmount),
        nameof(PayrollConfiguration.LeavePayoutDailyDivisor),
        nameof(PayrollConfiguration.RoundingMode),
        nameof(PayrollConfiguration.DecimalPlaces),
        nameof(PayrollConfiguration.CurrencyCode),
    ];

    public Guid PayrollConfigurationId { get; set; }

    [Required, MaxLength(FieldNameMaxLength)]
    [Display(Name = "الحقل")]
    public string FieldName { get; set; } = string.Empty;

    [MaxLength(LegalValueRules.LegalReferenceMaxLength)]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }

    [Display(Name = "الحالة القانونية")]
    public PayrollLegalValueStatus LegalStatus { get; set; } = PayrollLegalValueStatus.Interim;

    [MaxLength(LegalValueRules.InterimExceptionIdMaxLength)]
    [Display(Name = "الاستثناء المؤقت")]
    public string? InterimExceptionId { get; set; }

    public PayrollConfiguration PayrollConfiguration { get; set; } = null!;

    public string DescribeAr() => $"الحقل {FieldName}";
}
