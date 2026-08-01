namespace HRManagement.SharedKernel.Evidence;

/// <summary>
/// Unified decision evidence row (EVD-001). Append-only; sealed with a hash chain.
/// </summary>
public class EvidenceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>OpenTelemetry / business correlation id linking cross-service evidence.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Upstream event or evidence id that caused this record.</summary>
    public string? CausationId { get; set; }

    public string Actor { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;

    public string? AssuranceLevel { get; set; }
    public string? Policy { get; set; }
    public string? Rule { get; set; }
    public string? Reason { get; set; }
    public string Result { get; set; } = string.Empty;

    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }

    /// <summary>JSON array of related evidence/file/audit ids.</summary>
    public string? EvidenceLinksJson { get; set; }

    public string? ReleaseVersion { get; set; }
    public string? ConfigVersion { get; set; }

    /// <summary>UTC timestamp; source noted in TimeSource.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>e.g. system-clock, ntp-verified, tsa-rfc3161.</summary>
    public string TimeSource { get; set; } = "system-clock";

    public string TimeZoneId { get; set; } = "UTC";

    public string ChainKey { get; set; } = EvidenceHashChain.DefaultChainKey;
    public long Sequence { get; set; }
    public string PrevHash { get; set; } = EvidenceHashChain.GenesisPrevHash;
    public string EventHash { get; set; } = string.Empty;
}
