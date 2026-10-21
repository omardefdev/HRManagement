using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: key article reference for a legislation source.</summary>
public class LegislationKeyArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string LegalSourceId { get; set; } = string.Empty;

    public LegislationSource? LegislationSource { get; set; }

    public int ArticleNumber { get; set; }

    [Required, MaxLength(300)]
    public string Subject { get; set; } = string.Empty;
}
