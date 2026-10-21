using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Talent;

/// <summary>HCM-005: tracks an employee's enrollment in a learning/training activity.</summary>
public class LearningEnrollment : BaseEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    [Required, MaxLength(100)]
    public string CourseCode { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string CourseName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ProviderName { get; set; }

    public LearningType Type { get; set; } = LearningType.Training;

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Nominated;

    public DateTime EnrollmentDate { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? CompletionDate { get; set; }

    /// <summary>Total allocated budget for this enrollment (SYP).</summary>
    public decimal? BudgetAmount { get; set; }

    /// <summary>Actual cost incurred.</summary>
    public decimal? ActualCost { get; set; }

    /// <summary>Completion score or grade (percentage).</summary>
    public decimal? Score { get; set; }

    /// <summary>Verifiable completion certificate ID.</summary>
    [MaxLength(200)]
    public string? CertificateId { get; set; }

    /// <summary>Skill codes targeted by this learning activity (comma-separated).</summary>
    [MaxLength(500)]
    public string? TargetSkillCodes { get; set; }

    /// <summary>Manager who approved the nomination.</summary>
    public Guid? ApprovedByManagerId { get; set; }

    public bool IsCompleted => Status == EnrollmentStatus.Completed;

    public bool IsWithinBudget => BudgetAmount is null || (ActualCost ?? 0) <= BudgetAmount.Value;
}

public enum LearningType
{
    [Display(Name = "تدريب")]
    Training = 0,
    [Display(Name = "ورشة")]
    Workshop = 1,
    [Display(Name = "مؤتمر")]
    Conference = 2,
    [Display(Name = "شهادة")]
    Certification = 3,
    [Display(Name = "تعلّم إلكتروني")]
    ELearning = 4,
    [Display(Name = "أثناء العمل")]
    OnTheJob = 5,
    [Display(Name = "إرشاد")]
    Mentoring = 6
}

public enum EnrollmentStatus
{
    [Display(Name = "مرشّح")]
    Nominated = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "قيد التنفيذ")]
    InProgress = 2,
    [Display(Name = "مكتمل")]
    Completed = 3,
    [Display(Name = "فشل")]
    Failed = 4,
    [Display(Name = "ملغى")]
    Cancelled = 5,
    [Display(Name = "منسحب")]
    Withdrawn = 6
}
