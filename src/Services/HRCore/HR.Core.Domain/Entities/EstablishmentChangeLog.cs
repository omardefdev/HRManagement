using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>EST-002: append-only governance log for applied establishment changes.</summary>
public class EstablishmentChangeLog : BaseEntity
{
    public Guid ChangeRequestId { get; set; }
    public virtual EstablishmentChangeRequest? ChangeRequest { get; set; }

    [Required]
    [StringLength(64)]
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public EstablishmentChangeType ChangeType { get; set; }

    public string? SnapshotBeforeJson { get; set; }

    public string? SnapshotAfterJson { get; set; }

    [StringLength(100)]
    public string? LegalSourceId { get; set; }

    [StringLength(100)]
    public string? DecisionReference { get; set; }

    public Guid? WorkflowInstanceId { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
