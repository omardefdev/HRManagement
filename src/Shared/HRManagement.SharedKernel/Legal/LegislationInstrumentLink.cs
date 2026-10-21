using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: amended-by / repeals link between legislation sources.</summary>
public class LegislationInstrumentLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string FromLegalSourceId { get; set; } = string.Empty;

    public LegislationSource? FromLegislationSource { get; set; }

    [Required, MaxLength(100)]
    public string ToLegalSourceId { get; set; } = string.Empty;

    public LegislationSource? ToLegislationSource { get; set; }

    public LegislationInstrumentLinkType LinkType { get; set; }
}
