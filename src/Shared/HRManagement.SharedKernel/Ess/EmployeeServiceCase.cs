using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Ess;

/// <summary>ESS-001: a durable employee service center case with lifecycle tracking and SLA.</summary>
public class EmployeeServiceCase : BaseEntity
{
    [Required, MaxLength(50)]
    public string CaseId { get; set; } = string.Empty;

    public Guid EmployeeId { get; set; }

    public ServiceCaseType CaseType { get; set; }

    public ServiceCaseStatus Status { get; set; } = ServiceCaseStatus.Submitted;

    [MaxLength(500)]
    public string? Subject { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AcknowledgedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime SlaDeadline { get; set; }

    public bool SlaBreached { get; set; }

    [MaxLength(200)]
    public string? AssignedTo { get; set; }

    [MaxLength(200)]
    public string? WorkflowInstanceId { get; set; }

    public bool IsWithdrawn { get; set; }

    [MaxLength(500)]
    public string? ResolutionNotes { get; set; }

    public bool IsSlaMet => !SlaBreached && (ResolvedAt is null || ResolvedAt <= SlaDeadline);
}

public enum ServiceCaseType
{
    [Display(Name = "تصحيح ملف")]
    ProfileCorrection = 0,
    [Display(Name = "طلب إجازة")]
    LeaveRequest = 1,
    [Display(Name = "طلب سلفة")]
    AdvanceRequest = 2,
    [Display(Name = "طلب مستند")]
    DocumentRequest = 3,
    [Display(Name = "استفسار قسيمة")]
    PayslipQuery = 4,
    [Display(Name = "استفسار مزايا")]
    BenefitEnquiry = 5,
    [Display(Name = "التحاق تعليمي")]
    LearningEnrollment = 6,
    [Display(Name = "بدل بطاقة")]
    CardReplacement = 7,
    [Display(Name = "استفسار عام")]
    GeneralInquiry = 8,
    [Display(Name = "أخرى")]
    Other = 99
}

public enum ServiceCaseStatus
{
    [Display(Name = "مُرسَل")]
    Submitted = 0,
    [Display(Name = "مُقرّ")]
    Acknowledged = 1,
    [Display(Name = "قيد التنفيذ")]
    InProgress = 2,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval = 3,
    [Display(Name = "محلول")]
    Resolved = 4,
    [Display(Name = "مغلق")]
    Closed = 5,
    [Display(Name = "منسحب")]
    Withdrawn = 6,
    [Display(Name = "مرفوض")]
    Rejected = 7
}
