using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>LEG-001: legal hold status.</summary>
public enum LegalHoldStatus
{
    Active = 0,
    Released = 1
}

/// <summary>
/// LEG-001: prevents disposition of records matching scope while active.
/// No record under hold may be deleted, archived, or disposed regardless of retention schedule.
/// </summary>
public class LegalHold
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Short identifier for the hold (e.g. case number, investigation id).</summary>
    [Required]
    [MaxLength(100)]
    public string HoldReference { get; set; } = string.Empty;

    /// <summary>Description of why the hold was placed.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>
    /// JSON filter describing which records are held — record class, entity type, entity ids, date ranges.
    /// </summary>
    public string? ScopeJson { get; set; }

    public LegalHoldStatus Status { get; set; } = LegalHoldStatus.Active;

    [Required]
    [MaxLength(200)]
    public string IssuedBy { get; set; } = string.Empty;

    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? ReleasedBy { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
