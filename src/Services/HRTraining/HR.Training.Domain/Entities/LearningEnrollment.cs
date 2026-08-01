using System.ComponentModel.DataAnnotations;
using HR.Training.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "التحاق بدورة إلكترونية")]
public class LearningEnrollment : AuditableEntity
{
    [Display(Name = "الدورة")]
    public Guid ELearningCourseId { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ الالتحاق")]
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "نسبة الإنجاز")]
    [Range(0, 100)]
    public int ProgressPercent { get; set; }

    [Display(Name = "الحالة")]
    public LearningEnrollmentStatus Status { get; set; } = LearningEnrollmentStatus.Enrolled;

    [Display(Name = "تاريخ الإكمال")]
    public DateTime? CompletedAt { get; set; }

    public virtual ELearningCourse? ELearningCourse { get; set; }
    public virtual LearningCertificate? Certificate { get; set; }
}
