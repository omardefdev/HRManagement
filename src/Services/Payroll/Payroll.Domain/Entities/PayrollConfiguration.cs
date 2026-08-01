using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "إعدادات الرواتب")]
public class PayrollConfiguration : BaseEntity
{
    [Required, MaxLength(100)]
    [Display(Name = "الكود")]
    public string Code { get; set; } = "DEFAULT";

    /// <summary>Legal regime identifier, e.g. LAW_50_2004 or CIVIL_SERVICE_2026.</summary>
    [Required, MaxLength(50)]
    [Display(Name = "الإطار القانوني")]
    public string RegimeCode { get; set; } = Constants.LegalRegimeCodes.Law50_2004;

    [Required, MaxLength(200)]
    [Display(Name = "الاسم بالعربي")]
    public string NameAr { get; set; } = "الإعداد الافتراضي";

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    /// <summary>PAY-002: monotonic version per Code.</summary>
    [Display(Name = "رقم الإصدار")]
    public int VersionNumber { get; set; } = 1;

    [Display(Name = "حالة النشر")]
    public PayrollConfigPublicationStatus PublicationStatus { get; set; } = PayrollConfigPublicationStatus.Draft;

    [MaxLength(64)]
    [Display(Name = "بصمة المحتوى")]
    public string? ContentHash { get; set; }

    [Display(Name = "ساري من")]
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;

    [Display(Name = "ساري إلى")]
    public DateTime? EffectiveTo { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTime? PublishedAt { get; set; }

    /// <summary>Prior version this draft/publish supersedes.</summary>
    public Guid? SupersedesConfigurationId { get; set; }

    [Display(Name = "أساس التناسب")]
    public ProrationDaysBasis ProrationDaysBasis { get; set; } = ProrationDaysBasis.WorkingDays;

    /// <summary>GOV-002: LD-DAILY-DIVISOR — divisor when using calendar-30 basis.</summary>
    [Display(Name = "قاسم اليوم (30)")]
    public int DailyRateDivisor { get; set; } = 30;

    [Display(Name = "بدل مهمة افتراضي")]
    public decimal MissionAllowanceAmount { get; set; }

    [Display(Name = "نسبة بدل انتداب")]
    public decimal SecondmentAllowancePercent { get; set; }

    [Display(Name = "تفعيل علاوة التدريب")]
    public bool EnableTrainingPremiums { get; set; }

    [Display(Name = "علاوة التدريب")]
    public decimal TrainingPremiumAmount { get; set; }

    [Display(Name = "تعويض طبي افتراضي")]
    public decimal MedicalCompensationDefaultAmount { get; set; }

    [Display(Name = "حد أشهر التصحيح الرجعي")]
    public int MaxRetroCorrectionMonths { get; set; } = 12;

    [Display(Name = "نسبة تعويض نهاية الخدمة — الفترة الأولى")]
    public decimal EosGratuityRateFirstPeriod { get; set; } = 0.5m;

    [Display(Name = "حد سنوات الفترة الأولى")]
    public int EosGratuityFirstYearsThreshold { get; set; } = 5;

    [Display(Name = "نسبة تعويض نهاية الخدمة — بعد الحد")]
    public decimal EosGratuityRateAfterThreshold { get; set; } = 1.0m;

    /// <summary>Comma-separated DayOfWeek names, e.g. Friday,Saturday.</summary>
    [Required, MaxLength(100)]
    [Display(Name = "أيام عطلة نهاية الأسبوع")]
    public string NonWorkingDays { get; set; } = "Friday,Saturday";

    [Display(Name = "الاعتماد على العطل من قاعدة البيانات فقط")]
    public bool UseDbHolidaysOnly { get; set; } = true;

    [Required, MaxLength(50)]
    [Display(Name = "أساس الضريبة")]
    public string TaxableBaseMode { get; set; } = "GrossPay";

    [Display(Name = "مبلغ الإعفاء الضريبي")]
    public decimal TaxExemptionAmount { get; set; }

    [Display(Name = "قاسم احتساب بدل الإجازات")]
    public int LeavePayoutDailyDivisor { get; set; } = 30;

    /// <summary>PAY-002: legal money rounding mode for this version.</summary>
    [Display(Name = "نمط التقريب")]
    public PayrollRoundingMode RoundingMode { get; set; } = PayrollRoundingMode.AwayFromZero;

    [Display(Name = "منازل عشرية")]
    [Range(0, 6)]
    public int DecimalPlaces { get; set; } = 2;

    [Required, MaxLength(3)]
    [Display(Name = "العملة")]
    public string CurrencyCode { get; set; } = "SYP";

    public ICollection<TaxBracket> TaxBrackets { get; set; } = new List<TaxBracket>();

    public bool IsImmutable =>
        PublicationStatus is PayrollConfigPublicationStatus.Published
            or PayrollConfigPublicationStatus.Superseded
            or PayrollConfigPublicationStatus.Approved;

    public MidpointRounding ToMidpointRounding() =>
        RoundingMode == PayrollRoundingMode.ToEven
            ? MidpointRounding.ToEven
            : MidpointRounding.AwayFromZero;

    public decimal RoundMoney(decimal value) =>
        decimal.Round(value, DecimalPlaces, ToMidpointRounding());
}
