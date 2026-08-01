using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Temporary, dated transfer of approval authority from a principal to a delegate (IAM-003).
/// </summary>
public class ApprovalDelegation : BaseEntity
{
    [Required]
    public Guid PrincipalId { get; set; }

    [Required]
    public Guid DelegateId { get; set; }

    /// <summary>Null means all entity types.</summary>
    [StringLength(128)]
    public string? EntityTypeScope { get; set; }

    /// <summary>Null means all roles of the principal.</summary>
    [StringLength(100)]
    public string? RoleScope { get; set; }

    public DateTime ActiveFrom { get; set; }

    public DateTime ActiveUntil { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    public bool IsRevoked { get; set; }

    public Guid? RevokedBy { get; set; }

    public DateTime? RevokedAt { get; set; }
}
