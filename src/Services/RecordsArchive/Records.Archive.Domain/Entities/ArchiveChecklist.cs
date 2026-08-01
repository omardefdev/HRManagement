using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Records.Archive.Domain.Enums;

namespace Records.Archive.Domain.Entities;

public class ArchiveChecklist : BaseEntity
{
    public Guid CorrelationId { get; set; }
    public ArchiveChecklistStep Step { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsRequired { get; set; } = true;
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }

    [MaxLength(64)]
    public string? OwnerRole { get; set; }

    public int SlaHours { get; set; }

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }
}
