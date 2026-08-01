using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "نتيجة تحقيق رقابي")]
public class AuditFinding : AuditableEntity
{
    [Display(Name = "معرّف التدقيق")]
    public Guid? AuditId { get; set; }

    [Display(Name = "الوصف")]
    [Required(ErrorMessage = "الوصف مطلوب")]
    public string? Description { get; set; }

    [Display(Name = "الخطورة")]
    public FindingSeverity Severity { get; set; }

    [Display(Name = "التوصية")]
    public string? Recommendation { get; set; }

    [Display(Name = "الحالة")]
    public FindingStatus Status { get; set; }

    [Display(Name = "تاريخ الحل")]
    public DateTime? ResolvedAt { get; set; }

    public virtual ICollection<ComplianceRecommendation> Recommendations { get; set; } = new List<ComplianceRecommendation>();
}
