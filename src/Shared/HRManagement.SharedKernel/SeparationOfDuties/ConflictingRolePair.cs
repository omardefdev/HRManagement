using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Pair of roles that must not be held by the same user (IAM-003).
/// </summary>
public class ConflictingRolePair
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(100)]
    public string RoleA { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string RoleB { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
