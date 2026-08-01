using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Records.Archive.Domain.Entities;

/// <summary>FUN-006: append-only custody / chain-of-custody log.</summary>
public class CustodyEvent : BaseEntity
{
    public Guid ArchiveItemId { get; set; }

    [Required, MaxLength(64)]
    public string Action { get; set; } = default!;

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAt { get; set; }

    public ArchiveItem ArchiveItem { get; set; } = default!;
}
