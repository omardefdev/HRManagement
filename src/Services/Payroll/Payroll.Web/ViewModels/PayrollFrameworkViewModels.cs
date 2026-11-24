using System.ComponentModel.DataAnnotations;
using HRManagement.ServiceDefaults.Localization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>PLAN-54 (§1): the create form of a framework draft: a legal regime's template, or a clone of a version.</summary>
public sealed class PayrollFrameworkCreateViewModel
{
    [Display(Name = "النظام القانوني (قالب)")]
    public string? RegimeCode { get; set; }

    [Display(Name = "استنساخ نسخة")]
    public Guid? CloneFromId { get; set; }

    [Display(Name = "رمز الإطار")]
    public string? Code { get; set; }

    [Display(Name = "الاسم بالعربية")]
    public string? NameAr { get; set; }

    [Display(Name = "ساري من")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }
}

/// <summary>PLAN-54 (§1): the import form of a legal pack (a JSON file).</summary>
public sealed class PayrollFrameworkImportViewModel
{
    [Display(Name = "ملف الحزمة القانونية (JSON)")]
    public IFormFile? PackFile { get; set; }

    [Display(Name = "رمز الإطار (بدل رمز الحزمة)")]
    public string? Code { get; set; }

    [Display(Name = "ساري من (بدل تاريخ الحزمة)")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }

    [Display(Name = "استبدال محتوى مسودة")]
    public Guid? TargetDraftId { get; set; }
}

/// <summary>PLAN-54: the details page of a framework version: the version with its tables, and what the cycle recorded about it.</summary>
public sealed class PayrollFrameworkDetailsViewModel
{
    public required PayrollConfiguration Version { get; init; }

    public LegalAttestation? Attestation { get; init; }

    /// <summary>The Arabic message of a refused act, shown with status 400 (the version is unchanged).</summary>
    public string? Refusal { get; init; }

    /// <summary>
    /// How a value's interim exception is cited on the page: its number in the STR-002 control catalog (a legal citation a reader
    /// looks up, like a decree's number), or a dash for a signed value.
    /// </summary>
    public string ExceptionCitation(ILegallySourcedValue? value) =>
        string.IsNullOrWhiteSpace(value?.InterimExceptionId) ? "—" : value.InterimExceptionId.Trim();
}

/// <summary>
/// PLAN-54 (§1): the edit form of a framework draft: its name, its effective date and its numeric fields, and nothing else. The four
/// tables are edited by export and import; the cycle's state, the fingerprint and the editors are never the form's. Amounts bind with
/// the invariant decimal binder (a fraction would not bind under the service's Arabic culture).
/// </summary>
public sealed class PayrollFrameworkEditViewModel
{
    public Guid Id { get; set; }

    /// <summary>True when the version is approved, published or superseded: saving creates a new draft from it.</summary>
    public bool IsImmutable { get; set; }

    [Required(ErrorMessage = "الاسم بالعربية إلزامي"), MaxLength(200)]
    [Display(Name = "الاسم بالعربي")]
    public string NameAr { get; set; } = string.Empty;

    [Required(ErrorMessage = "تاريخ السريان إلزامي")]
    [Display(Name = "ساري من")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }

    [Display(Name = "أساس التناسب")]
    public ProrationDaysBasis ProrationDaysBasis { get; set; }

    [Range(1, 31)]
    [Display(Name = "قاسم اليوم (30)")]
    public int DailyRateDivisor { get; set; } = 30;

    [Range(0, 9999999999999999.99)]
    [Display(Name = "بدل مهمة افتراضي")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal MissionAllowanceAmount { get; set; }

    [Range(0, 100)]
    [Display(Name = "نسبة بدل انتداب")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal SecondmentAllowancePercent { get; set; }

    [Display(Name = "تفعيل علاوة التدريب")]
    public bool EnableTrainingPremiums { get; set; }

    [Range(0, 9999999999999999.99)]
    [Display(Name = "علاوة التدريب")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal TrainingPremiumAmount { get; set; }

    [Range(0, 9999999999999999.99)]
    [Display(Name = "تعويض طبي افتراضي")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal MedicalCompensationDefaultAmount { get; set; }

    [Range(0, 120)]
    [Display(Name = "حد أشهر التصحيح الرجعي")]
    public int MaxRetroCorrectionMonths { get; set; } = 12;

    [Range(0, 12)]
    [Display(Name = "نسبة تعويض نهاية الخدمة — الفترة الأولى")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal EosGratuityRateFirstPeriod { get; set; }

    [Range(0, 60)]
    [Display(Name = "حد سنوات الفترة الأولى")]
    public int EosGratuityFirstYearsThreshold { get; set; }

    [Range(0, 12)]
    [Display(Name = "نسبة تعويض نهاية الخدمة — بعد الحد")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal EosGratuityRateAfterThreshold { get; set; }

    [Required(ErrorMessage = "أيام عطلة نهاية الأسبوع إلزامية"), MaxLength(100)]
    [Display(Name = "أيام عطلة نهاية الأسبوع")]
    public string NonWorkingDays { get; set; } = "Friday,Saturday";

    [Display(Name = "الاعتماد على العطل من قاعدة البيانات فقط")]
    public bool UseDbHolidaysOnly { get; set; } = true;

    [Required(ErrorMessage = "أساس الضريبة إلزامي")]
    [Display(Name = "أساس الضريبة")]
    public string TaxableBaseMode { get; set; } = "GrossPay";

    [Range(0, 9999999999999999.99)]
    [Display(Name = "مبلغ الإعفاء الضريبي")]
    [ModelBinder(typeof(HrInvariantDecimalModelBinder))]
    public decimal TaxExemptionAmount { get; set; }

    [Range(1, 31)]
    [Display(Name = "قاسم احتساب بدل الإجازات")]
    public int LeavePayoutDailyDivisor { get; set; } = 30;

    [Display(Name = "نمط التقريب")]
    public PayrollRoundingMode RoundingMode { get; set; }

    [Range(0, 6)]
    [Display(Name = "منازل عشرية")]
    public int DecimalPlaces { get; set; } = 2;

    [Required(ErrorMessage = "العملة إلزامية"), RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "رمز العملة بثلاثة أحرف لاتينية")]
    [Display(Name = "العملة")]
    public string CurrencyCode { get; set; } = "SYP";

    public static PayrollFrameworkEditViewModel From(PayrollConfiguration c) => new()
    {
        Id = c.Id,
        IsImmutable = c.IsImmutable,
        NameAr = c.NameAr,
        EffectiveFrom = DateOnly.FromDateTime(c.EffectiveFrom),
        ProrationDaysBasis = c.ProrationDaysBasis,
        DailyRateDivisor = c.DailyRateDivisor,
        MissionAllowanceAmount = c.MissionAllowanceAmount,
        SecondmentAllowancePercent = c.SecondmentAllowancePercent,
        EnableTrainingPremiums = c.EnableTrainingPremiums,
        TrainingPremiumAmount = c.TrainingPremiumAmount,
        MedicalCompensationDefaultAmount = c.MedicalCompensationDefaultAmount,
        MaxRetroCorrectionMonths = c.MaxRetroCorrectionMonths,
        EosGratuityRateFirstPeriod = c.EosGratuityRateFirstPeriod,
        EosGratuityFirstYearsThreshold = c.EosGratuityFirstYearsThreshold,
        EosGratuityRateAfterThreshold = c.EosGratuityRateAfterThreshold,
        NonWorkingDays = c.NonWorkingDays,
        UseDbHolidaysOnly = c.UseDbHolidaysOnly,
        TaxableBaseMode = c.TaxableBaseMode,
        TaxExemptionAmount = c.TaxExemptionAmount,
        LeavePayoutDailyDivisor = c.LeavePayoutDailyDivisor,
        RoundingMode = c.RoundingMode,
        DecimalPlaces = c.DecimalPlaces,
        CurrencyCode = c.CurrencyCode,
    };

    /// <summary>Writes the form's fields to a draft. Every governed field of a version is on the form (a test checks).</summary>
    public void ApplyTo(PayrollConfiguration target)
    {
        target.NameAr = NameAr.Trim();
        target.ProrationDaysBasis = ProrationDaysBasis;
        target.DailyRateDivisor = DailyRateDivisor;
        target.MissionAllowanceAmount = MissionAllowanceAmount;
        target.SecondmentAllowancePercent = SecondmentAllowancePercent;
        target.EnableTrainingPremiums = EnableTrainingPremiums;
        target.TrainingPremiumAmount = TrainingPremiumAmount;
        target.MedicalCompensationDefaultAmount = MedicalCompensationDefaultAmount;
        target.MaxRetroCorrectionMonths = MaxRetroCorrectionMonths;
        target.EosGratuityRateFirstPeriod = EosGratuityRateFirstPeriod;
        target.EosGratuityFirstYearsThreshold = EosGratuityFirstYearsThreshold;
        target.EosGratuityRateAfterThreshold = EosGratuityRateAfterThreshold;
        target.NonWorkingDays = NonWorkingDays.Trim();
        target.UseDbHolidaysOnly = UseDbHolidaysOnly;
        target.TaxableBaseMode = TaxableBaseMode;
        target.TaxExemptionAmount = TaxExemptionAmount;
        target.LeavePayoutDailyDivisor = LeavePayoutDailyDivisor;
        target.RoundingMode = RoundingMode;
        target.DecimalPlaces = DecimalPlaces;
        target.CurrencyCode = CurrencyCode.Trim().ToUpperInvariant();
    }
}
