using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "نتيجة التدريب")]
public class TrainingResult : BaseEntity
{
    [Required]
    [Display(Name = "التسجيل")]
    public Guid EnrollmentId { get; set; }

    [Display(Name = "الدرجة")]
    public decimal Score { get; set; }

    [Display(Name = "ملاحظات التقييم")]
    public string? EvaluationNotes { get; set; }

    [Display(Name = "مقيّم بواسطة")]
    public Guid? GradedBy { get; set; }

    [Display(Name = "تاريخ التقييم")]
    public DateTime? GradedAt { get; set; }

    [ForeignKey(nameof(EnrollmentId))]
    public virtual TrainingEnrollment Enrollment { get; set; } = default!;
}
