using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Maker/checker/poster/reverser matrix row for a business entity type (IAM-003).
/// </summary>
public class SodRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(128)]
    public string EntityType { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string MakerRole { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string CheckerRole { get; set; } = string.Empty;

    [StringLength(100)]
    public string? PosterRole { get; set; }

    [StringLength(100)]
    public string? ReverserRole { get; set; }

    /// <summary>When true, maker and checker must be different users.</summary>
    public bool RequireDifferentActors { get; set; } = true;

    /// <summary>Amount above which dual (four-eyes) approval is required.</summary>
    public decimal? FinancialThreshold { get; set; }

    /// <summary>Minimum distinct approvals required (1 = single, 2 = four-eyes).</summary>
    public int RequiredApprovalCount { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
