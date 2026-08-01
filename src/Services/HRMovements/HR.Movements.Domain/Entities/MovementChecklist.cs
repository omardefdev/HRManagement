using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

/// <summary>FUN-003: governance checklist shared by Transfer / Secondment / Loan.</summary>
public class MovementChecklist : BaseEntity
{
    public MovementKind MovementKind { get; set; }

    /// <summary>TransferRequestId / SecondmentRequestId / LoanAssignmentRequestId.</summary>
    public Guid CorrelationId { get; set; }

    public MovementChecklistStep Step { get; set; }

    public bool IsCompleted { get; set; }

    public bool IsRequired { get; set; } = true;

    public DateTime? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }

    [MaxLength(128)]
    public string? CompletedByName { get; set; }

    [MaxLength(64)]
    public string? OwnerRole { get; set; }

    public int SlaHours { get; set; }

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}
