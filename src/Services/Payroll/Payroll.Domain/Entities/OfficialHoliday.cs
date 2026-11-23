using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "عطلة رسمية")]
public class OfficialHoliday : BaseEntity, ILegallySourcedValue
{
    [Display(Name = "التاريخ")]
    public DateOnly Date { get; set; }

    [Required, MaxLength(200)]
    [Display(Name = "الاسم")]
    public string NameAr { get; set; } = null!;

    [Display(Name = "متكررة سنوياً")]
    public bool IsRecurring { get; set; }

    [Display(Name = "إعداد الرواتب")]
    public Guid? PayrollConfigurationId { get; set; }

    /// <summary>PLAN-54: the holiday's legal source. Rows older than PLAN-54 are interim with no reference.</summary>
    [MaxLength(LegalValueRules.LegalReferenceMaxLength)]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }

    [Display(Name = "الحالة القانونية")]
    public PayrollLegalValueStatus LegalStatus { get; set; } = PayrollLegalValueStatus.Interim;

    [MaxLength(LegalValueRules.InterimExceptionIdMaxLength)]
    [Display(Name = "الاستثناء المؤقت")]
    public string? InterimExceptionId { get; set; }

    public virtual PayrollConfiguration? PayrollConfiguration { get; set; }

    public string DescribeAr() => FormattableString.Invariant($"العطلة {Date:yyyy-MM-dd}");
}
