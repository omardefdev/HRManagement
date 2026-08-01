using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

public class InvestigationChecklist : BaseEntity
{
    public Guid ComplaintId { get; set; }
    public InvestigationChecklistStep Step { get; set; }
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
