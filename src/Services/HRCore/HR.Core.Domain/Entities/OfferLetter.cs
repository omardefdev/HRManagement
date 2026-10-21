using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-004: persisted offer letter linked to recruitment application.</summary>
public class OfferLetter : AuditableEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    public virtual RecruitmentApplication? RecruitmentApplication { get; set; }

    [Display(Name = "المنصب الممول")]
    public Guid FundedPositionId { get; set; }

    public virtual FundedPosition? FundedPosition { get; set; }

    [Required, StringLength(50)]
    [Display(Name = "رقم العرض")]
    public string OfferNumber { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    [Display(Name = "تاريخ الإصدار")]
    public DateTime? IssuedDate { get; set; }

    [Display(Name = "تاريخ الانتهاء")]
    public DateTime? ExpiryDate { get; set; }

    [Display(Name = "تاريخ القبول")]
    public DateTime? AcceptedDate { get; set; }

    [Display(Name = "الراتب المعروض")]
    public decimal OfferedSalary { get; set; }

    [StringLength(50)]
    [Display(Name = "الدرجة")]
    public string? GradeCode { get; set; }

    [Display(Name = "تاريخ المباشرة المقترح")]
    public DateTime ProposedStartDate { get; set; }

    [StringLength(100)]
    [Display(Name = "مرجع قانوني")]
    public string? LegalSourceId { get; set; }

    public virtual LegislationSource? LegalSource { get; set; }

    [Display(Name = "تحوّل إلى موظف")]
    public bool IsConvertedToEmployee { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }
}
