using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Training.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "تسجيل الدورة")]
public class TrainingEnrollment : AuditableEntity
{
    [Required]
    [Display(Name = "الدورة")]
    public Guid CourseId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ التسجيل")]
    public DateTime EnrolledAt { get; set; }

    [Display(Name = "الحالة")]
    public EnrollmentStatus Status { get; set; }

    [Display(Name = "الدرجة النهائية")]
    public decimal? FinalScore { get; set; }

    [Display(Name = "معرف ملف الشهادة")]
    public Guid? CertificateFileId { get; set; }

    /// <summary>FUN-004: classroom certificate number for verify API.</summary>
    [MaxLength(100)]
    public string? CertificateNo { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    [ForeignKey(nameof(CourseId))]
    public virtual TrainingCourse Course { get; set; } = default!;

    public virtual TrainingResult? Result { get; set; }
}
