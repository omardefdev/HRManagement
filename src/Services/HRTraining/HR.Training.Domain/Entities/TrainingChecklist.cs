using System.ComponentModel.DataAnnotations;
using HR.Training.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Training.Domain.Entities;

/// <summary>FUN-004: governance checklist for classroom enrollment.</summary>
public class TrainingChecklist : BaseEntity
{
    public Guid EnrollmentId { get; set; }

    public TrainingChecklistStep Step { get; set; }

    public bool IsCompleted { get; set; }
    public bool IsRequired { get; set; } = true;
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }

    [MaxLength(64)]
    public string? OwnerRole { get; set; }

    public int SlaHours { get; set; }

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}
