using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace Compliance.Domain.Entities;

[Display(Name = "توصية رقابية")]
public class ComplianceRecommendation : AuditableEntity
{
    [Display(Name = "معرّف النتيجة")]
    public Guid? FindingId { get; set; }

    [Display(Name = "الشكوى")]
    public Guid? ComplaintId { get; set; }

    [Display(Name = "الإجراء الموصى به")]
    public string? RecommendedAction { get; set; }

    [Display(Name = "تاريخ الاستحقاق")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [Display(Name = "الوحدة المسؤولة")]
    public Guid? ResponsibleUnitId { get; set; }

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; }

    public virtual AuditFinding? Finding { get; set; }

    public virtual Complaint? Complaint { get; set; }
}
