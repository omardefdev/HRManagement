using System.ComponentModel.DataAnnotations;
using HR.Health.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "تقرير اللجنة الطبية")]
public class MedicalCommitteeReport : AuditableEntity
{
    [Display(Name = "الملف الطبي")]
    public Guid MedicalRecordId { get; set; }

    [Required]
    [Display(Name = "تاريخ الانعقاد")]
    public DateOnly CommitteeDate { get; set; }

    [Display(Name = "النتائج")]
    public string? Findings { get; set; }

    [Display(Name = "التوصية")]
    public MedicalCommitteeRecommendation Recommendation { get; set; }

    [Display(Name = "صادر عن")]
    public string? IssuedBy { get; set; }

    [Display(Name = "المستندات")]
    public string? RelatedDocuments { get; set; }

    public MedicalRecord MedicalRecord { get; set; } = null!;
    public ICollection<MedicalRecommendation> Recommendations { get; set; } = new List<MedicalRecommendation>();
}
