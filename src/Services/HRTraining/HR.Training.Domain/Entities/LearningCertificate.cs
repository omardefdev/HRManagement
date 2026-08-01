using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

[Display(Name = "شهادة تعلّم إلكتروني")]
public class LearningCertificate : AuditableEntity
{
    [Display(Name = "الالتحاق")]
    public Guid LearningEnrollmentId { get; set; }

    [Required]
    [Display(Name = "رقم الشهادة")]
    [StringLength(100)]
    public string CertificateNo { get; set; } = string.Empty;

    [Display(Name = "تاريخ الإصدار")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    public virtual LearningEnrollment? LearningEnrollment { get; set; }
}
