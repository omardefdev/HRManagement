using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: alternate identifier (e.g. SYR-003 legislation_ref law50_2004).</summary>
public class LegislationSourceAlias
{
    [Key]
    [MaxLength(100)]
    public string Alias { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LegalSourceId { get; set; } = string.Empty;

    public LegislationSource? LegislationSource { get; set; }
}
