using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: national legislation register entry (platform-global, no tenant).</summary>
public class LegislationSource
{
    [Key]
    [MaxLength(100)]
    public string LegalSourceId { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string InstrumentType { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Number { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string TitleAr { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? TitleEn { get; set; }

    public DateOnly IssueDate { get; set; }

    public DateOnly EffectiveDate { get; set; }

    public DateOnly? PublicationDate { get; set; }

    [Required, MaxLength(16)]
    public string Status { get; set; } = "active";

    [MaxLength(500)]
    public string? Scope { get; set; }

    [Required, MaxLength(200)]
    public string Owner { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? CertifiedTextHash { get; set; }

    [MaxLength(16)]
    public string RegisterVersion { get; set; } = "1.0.0";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LegislationKeyArticle> KeyArticles { get; set; } = [];

    public ICollection<LegislationInstrumentLink> OutgoingLinks { get; set; } = [];

    public ICollection<LegislationInstrumentLink> IncomingLinks { get; set; } = [];

    public ICollection<LegislationSourceAlias> Aliases { get; set; } = [];
}
