using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Cases;

/// <summary>HCM-006: an employee relations case (grievance, discipline, investigation, or whistleblower report).</summary>
public class EmployeeRelationCase : BaseEntity
{
    [Required, MaxLength(50)]
    public string CaseNumber { get; set; } = string.Empty;

    public RelationCaseType CaseType { get; set; }

    public RelationCaseStatus Status { get; set; } = RelationCaseStatus.Open;

    public Guid EmployeeId { get; set; }

    public Guid? ReportedById { get; set; }

    [MaxLength(500)]
    public string? Subject { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    public DateTime? Deadline { get; set; }

    public bool IsWhistleblower { get; set; }

    public bool IsConfidential { get; set; } = true;

    [MaxLength(200)]
    public string? AssignedInvestigatorId { get; set; }

    [MaxLength(500)]
    public string? ResolutionSummary { get; set; }

    [MaxLength(100)]
    public string? LegalHoldReference { get; set; }

    public bool HasConflictOfInterest { get; set; }

    public bool EscalatedToLegal { get; set; }
}

public enum RelationCaseType
{
    [Display(Name = "تظلم")]
    Grievance = 0,
    [Display(Name = "تأديب")]
    Discipline = 1,
    [Display(Name = "تحقيق")]
    Investigation = 2,
    [Display(Name = "إبلاغ")]
    Whistleblower = 3,
    [Display(Name = "حادث عمل")]
    WorkplaceIncident = 4,
    [Display(Name = "شكوى")]
    Complaint = 5,
    [Display(Name = "استئناف")]
    Appeal = 6
}

public enum RelationCaseStatus
{
    [Display(Name = "مفتوح")]
    Open = 0,
    [Display(Name = "قيد التحقيق")]
    UnderInvestigation = 1,
    [Display(Name = "بانتظار جلسة")]
    PendingHearing = 2,
    [Display(Name = "إجراء متخذ")]
    ActionTaken = 3,
    [Display(Name = "استئناف مقدّم")]
    AppealFiled = 4,
    [Display(Name = "مغلق")]
    Closed = 5,
    [Display(Name = "مرفوض")]
    Dismissed = 6
}
