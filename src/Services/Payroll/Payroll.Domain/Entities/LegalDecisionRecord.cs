using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>GOV-002: machine-readable legal decision row (mirror of register.json).</summary>
[Display(Name = "قرار قانوني")]
public class LegalDecisionRecord : BaseEntity
{
    [Required, MaxLength(64)]
    public string DecisionId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? ConfigField { get; set; }

    [MaxLength(128)]
    public string? RuleId { get; set; }

    [Required, MaxLength(512)]
    public string OfficialSource { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? IssueRef { get; set; }

    public DateTime EffectiveFrom { get; set; }

    [Required, MaxLength(128)]
    public string Owner { get; set; } = string.Empty;

    /// <summary>ApprovedInterim | PendingEnactment | Superseded</summary>
    [Required, MaxLength(32)]
    public string ApprovalStatus { get; set; } = "ApprovedInterim";

    [MaxLength(50)]
    public string? RegimeCode { get; set; }

    [MaxLength(64)]
    public string? ConfigContentHash { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
