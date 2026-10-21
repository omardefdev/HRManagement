using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>SYR-003/SYR-07: unclassified or blocked service regime cases awaiting HR resolution.</summary>
public class ServiceRegimeException : AuditableEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    public Guid? EmploymentRecordId { get; set; }

    public ServiceRegimeExceptionType ExceptionType { get; set; }

    public ServiceRegimeExceptionStatus Status { get; set; } = ServiceRegimeExceptionStatus.Open;

    public ServiceRegimeExceptionSource SourceContext { get; set; }

    [MaxLength(100)]
    public string? LegalSourceId { get; set; }

    [MaxLength(50)]
    public string? AttemptedRegimeId { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    [MaxLength(500)]
    public string? Resolution { get; set; }

    [MaxLength(50)]
    public string? ResolvedRegimeId { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    /// <summary>SYR-009: Article 159 conflict notes persisted on exception.</summary>
    [MaxLength(1000)]
    public string? ConflictNotes { get; set; }

    /// <summary>SYR-009: GOV-002 legal decision reference for Article 159 resolution.</summary>
    [MaxLength(64)]
    public string? LegalDecisionId { get; set; }

    /// <summary>SYR-009: structured entitlement overlap payload (JSON).</summary>
    [MaxLength(2000)]
    public string? ConflictingEntitlementsJson { get; set; }

    public virtual Employee? Employee { get; set; }

    public virtual EmploymentRecord? EmploymentRecord { get; set; }
}

public enum ServiceRegimeExceptionType
{
    UnclassifiedRegime = 0,
    MissingLegislationLink = 1,
    ManualReview = 2,
    Article159Conflict = 3
}

public enum ServiceRegimeExceptionStatus
{
    Open = 0,
    Resolved = 1,
    Dismissed = 2
}

public enum ServiceRegimeExceptionSource
{
    Hire = 0,
    Backfill = 1,
    Transition = 2,
    Separation = 3,
    Promotion = 4
}
