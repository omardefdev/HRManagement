using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>GOV-003-C: duplicate NationalId / employee review queue.</summary>
[Display(Name = "حالة تكرار بيانات رئيسية")]
public class MasterDataDuplicateCase : AuditableEntity
{
    [Required, MaxLength(64)]
    public string CaseType { get; set; } = "NationalId";

    [MaxLength(64)]
    public string? NormalizedKey { get; set; }

    public Guid? CandidateEmployeeIdA { get; set; }

    public Guid? CandidateEmployeeIdB { get; set; }

    public MasterDataDuplicateCaseStatus Status { get; set; } = MasterDataDuplicateCaseStatus.Open;

    public Guid? ResolvedIntoEmployeeId { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }
}
