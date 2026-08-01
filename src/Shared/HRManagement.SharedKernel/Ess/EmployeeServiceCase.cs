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
    ProfileCorrection = 0,
    LeaveRequest = 1,
    AdvanceRequest = 2,
    DocumentRequest = 3,
    PayslipQuery = 4,
    BenefitEnquiry = 5,
    LearningEnrollment = 6,
    CardReplacement = 7,
    GeneralInquiry = 8,
    Other = 99
}

public enum ServiceCaseStatus
{
    Submitted = 0,
    Acknowledged = 1,
    InProgress = 2,
    PendingApproval = 3,
    Resolved = 4,
    Closed = 5,
    Withdrawn = 6,
    Rejected = 7
}
