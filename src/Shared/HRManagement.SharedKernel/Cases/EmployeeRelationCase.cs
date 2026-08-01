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
    Grievance = 0,
    Discipline = 1,
    Investigation = 2,
    Whistleblower = 3,
    WorkplaceIncident = 4,
    Complaint = 5,
    Appeal = 6
}

public enum RelationCaseStatus
{
    Open = 0,
    UnderInvestigation = 1,
    PendingHearing = 2,
    ActionTaken = 3,
    AppealFiled = 4,
    Closed = 5,
    Dismissed = 6
}
