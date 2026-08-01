using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Legal;

/// <summary>LEG-001: official record lifecycle status.</summary>
public enum OfficialRecordStatus
{
    Active = 0,
    Archived = 1,
    PendingDisposition = 2,
    Disposed = 3,
    UnderLegalHold = 4
}

/// <summary>
/// LEG-001: official electronic record subject to statutory retention.
/// Immutable once created; corrections create new records that supersede the original.
/// </summary>
public class OfficialRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Record class from the retention schedule (e.g. "employee-file", "payroll-run", "decision").</summary>
    [Required]
    [MaxLength(100)]
    public string RecordClass { get; set; } = string.Empty;

    /// <summary>Domain entity type this record covers.</summary>
    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key of the covered entity.</summary>
    [Required]
    [MaxLength(100)]
    public string EntityId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Title { get; set; }

    /// <summary>Retention period code from retention-schedule.json.</summary>
    [Required]
    [MaxLength(50)]
    public string RetentionCode { get; set; } = string.Empty;

    /// <summary>Date the retention clock starts (e.g. separation date, decision date).</summary>
    public DateTime RetentionStartUtc { get; set; }

    /// <summary>Computed disposition date based on schedule.</summary>
    public DateTime? DispositionDueUtc { get; set; }

    public OfficialRecordStatus Status { get; set; } = OfficialRecordStatus.Active;

    /// <summary>SHA-256 hash of the record content for integrity verification.</summary>
    [MaxLength(128)]
    public string? ContentHash { get; set; }

    /// <summary>Id of the record this one supersedes (correction/replacement).</summary>
    public Guid? SupersedesRecordId { get; set; }

    /// <summary>Actor who authorized disposition, when applicable.</summary>
    [MaxLength(200)]
    public string? DispositionApprovedBy { get; set; }

    public DateTime? DisposedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
