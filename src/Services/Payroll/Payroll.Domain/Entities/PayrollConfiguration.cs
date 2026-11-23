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

    /// <summary>
    /// PLAN-54: which fingerprint the version carries. <see cref="LegacyHashSchemaVersion"/> for every version that existed before
    /// PLAN-54 and for the Demo and Acceptance seeds (fields and brackets only; a run of it reads the general tables as before);
    /// <see cref="CurrentHashSchemaVersion"/> for every version created, cloned or imported since (the four tables, the field sources
    /// and every reference are inside the version and its fingerprint).
    /// </summary>
    [Display(Name = "إصدار البصمة")]
    public int HashSchemaVersion { get; set; } = LegacyHashSchemaVersion;

    public const int LegacyHashSchemaVersion = 1;
    public const int CurrentHashSchemaVersion = 2;

    /// <summary>True for a version older than PLAN-54: its run reads the general deduction types, holidays and priority rules.</summary>
    public bool IsLegacyFramework => HashSchemaVersion < CurrentHashSchemaVersion;

    public ICollection<TaxBracket> TaxBrackets { get; set; } = new List<TaxBracket>();

    /// <summary>PLAN-54: the statutory deductions of a v2 version (empty on a legacy version).</summary>
    public ICollection<StatutoryDeductionRule> StatutoryDeductions { get; set; } = new List<StatutoryDeductionRule>();

    /// <summary>The version's own official holidays. A v2 run reads these only; a legacy run also reads the general rows.</summary>
    public ICollection<OfficialHoliday> Holidays { get; set; } = new List<OfficialHoliday>();

    /// <summary>PLAN-54: the disbursement priority rules of a v2 version (empty on a legacy version).</summary>
    public ICollection<DisbursementPriorityRule> PriorityRules { get; set; } = new List<DisbursementPriorityRule>();

    /// <summary>PLAN-54: the legal source of each governed numeric field (v2).</summary>
    public ICollection<PayrollConfigurationFieldSource> FieldSources { get; set; } = new List<PayrollConfigurationFieldSource>();

    /// <summary>PLAN-54: everyone who created, imported into or edited this version. Not part of the fingerprint.</summary>
    public ICollection<PayrollConfigurationEditor> Editors { get; set; } = new List<PayrollConfigurationEditor>();

    /// <summary>Every legally sourced value of the version, for the governance gates and the screens.</summary>
    public IEnumerable<ILegallySourcedValue> LegalValues() =>
        FieldSources.OrderBy(f => f.FieldName, StringComparer.Ordinal).Cast<ILegallySourcedValue>()
            .Concat(TaxBrackets.OrderBy(b => b.SortOrder).ThenBy(b => b.UpperLimit))
            .Concat(StatutoryDeductions.OrderBy(d => d.SortOrder).ThenBy(d => d.Code, StringComparer.Ordinal))
            .Concat(Holidays.OrderBy(h => h.Date).ThenBy(h => h.NameAr, StringComparer.Ordinal))
            .Concat(PriorityRules.OrderBy(r => r.Priority).ThenBy(r => r.Category, StringComparer.Ordinal));

    /// <summary>Records <paramref name="userId"/> as an editor of this version (once per person and kind).</summary>
    public void RecordEditor(Guid userId, PayrollFrameworkEditKind kind, DateTime atUtc)
    {
        if (userId == Guid.Empty || Editors.Any(e => e.UserId == userId && e.Kind == kind))
            return;
        Editors.Add(new PayrollConfigurationEditor
        {
            PayrollConfigurationId = Id,
            UserId = userId,
            Kind = kind,
            At = atUtc,
            TenantId = TenantId
        });
    }

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
