using System.ComponentModel.DataAnnotations;
using HR.Health.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "التوصية الطبية")]
public class MedicalRecommendation : AuditableEntity
{
    [Display(Name = "التقرير")]
    public Guid? ReportId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع التوصية")]
    public MedicalRecommendationType RecommendationType { get; set; }

    [Display(Name = "التفاصيل")]
    public string? Details { get; set; }

    [Display(Name = "تاريخ الإصدار")]
    public DateTime? IssuedAt { get; set; }

    [Display(Name = "صادر عن")]
    public string? IssuedBy { get; set; }

    public MedicalCommitteeReport? Report { get; set; }
}
