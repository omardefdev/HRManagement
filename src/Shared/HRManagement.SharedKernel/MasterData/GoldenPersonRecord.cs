using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.MasterData;

/// <summary>MDM-002: golden person record status.</summary>
public enum GoldenPersonStatus
{
    Active = 0,
    Merged = 1,
    Deceased = 2
}

/// <summary>
/// MDM-002: single authoritative identity for a government employee across agencies.
/// PersonId is system-wide; NationalIdHash links to civil registry without storing raw PII.
/// </summary>
public class GoldenPersonRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>System-wide person identifier, stable across merges and agency transfers.</summary>
    public Guid PersonId { get; set; }

    /// <summary>SHA-256 hex of national ID — never store raw.</summary>
    [Required]
    [MaxLength(128)]
    public string NationalIdHash { get; set; } = string.Empty;

    /// <summary>JSON array of merge/unmerge events with timestamps, actors, and provenance.</summary>
    public string? MergeHistory { get; set; }

    /// <summary>JSON object recording field-level source system, date, and confidence.</summary>
    public string? ProvenanceJson { get; set; }

    public GoldenPersonStatus Status { get; set; } = GoldenPersonStatus.Active;

    /// <summary>When merged, points to the surviving record.</summary>
    public Guid? SurvivorPersonId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
