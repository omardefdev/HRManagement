using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "شريحة ضريبة")]
public class TaxBracket : BaseEntity, ILegallySourcedValue
{
    [Required]
    [ForeignKey(nameof(PayrollConfiguration))]
    public Guid PayrollConfigurationId { get; set; }

    [Display(Name = "الحد الأعلى")]
    public decimal UpperLimit { get; set; }

    [Display(Name = "النسبة %")]
    public decimal RatePercent { get; set; }

    [Display(Name = "الترتيب")]
    public int SortOrder { get; set; }

    /// <summary>PLAN-54: the bracket's legal source. Rows older than PLAN-54 are interim with no reference.</summary>
    [MaxLength(LegalValueRules.LegalReferenceMaxLength)]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }

    [Display(Name = "الحالة القانونية")]
    public PayrollLegalValueStatus LegalStatus { get; set; } = PayrollLegalValueStatus.Interim;

    [MaxLength(LegalValueRules.InterimExceptionIdMaxLength)]
    [Display(Name = "الاستثناء المؤقت")]
    public string? InterimExceptionId { get; set; }

    public PayrollConfiguration PayrollConfiguration { get; set; } = null!;

    public string DescribeAr() => FormattableString.Invariant($"شريحة الضريبة {SortOrder + 1}");
}
