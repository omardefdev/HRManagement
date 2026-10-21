using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Files;

/// <summary>SEC-003: visibility level for a file access grant.</summary>
public enum FileVisibility
{
    [Display(Name = "خاص")]
    Private = 0,
    [Display(Name = "وحدة")]
    Module = 1,
    [Display(Name = "جهة")]
    Tenant = 2,
    [Display(Name = "عام")]
    Public = 3
}

/// <summary>
/// SEC-003: binds a file to its owning tenant, module, holder, and visibility.
/// Object-level authorization decisions use this grant — no file is accessible
/// without an explicit grant linking the requester's context to the file.
/// </summary>
public class FileAccessGrant
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Platform file id (FileRecord.Id) this grant authorizes.</summary>
    public Guid FileId { get; set; }

    /// <summary>Owning bounded-context module (e.g. "HR.Core", "Payroll").</summary>
    [Required]
    [MaxLength(100)]
    public string Module { get; set; } = string.Empty;

    /// <summary>Domain entity type that owns the file (e.g. "Employee", "LeaveRequest").</summary>
    [Required]
    [MaxLength(100)]
    public string HolderType { get; set; } = string.Empty;

    /// <summary>Primary key of the owning entity.</summary>
    [Required]
    [MaxLength(100)]
    public string HolderId { get; set; } = string.Empty;

    /// <summary>Storage-level file identifier (MinIO object key or local path).</summary>
    [Required]
    [MaxLength(500)]
    public string FileReference { get; set; } = string.Empty;

    public FileVisibility Visibility { get; set; } = FileVisibility.Private;

    /// <summary>Optional explicit grantee user/service identity. Null = module/tenant-level.</summary>
    [MaxLength(200)]
    public string? GranteeId { get; set; }

    /// <summary>Presigned URL maximum lifetime in seconds (≤ 300).</summary>
    public int PresignedUrlMaxLifetimeSeconds { get; set; } = 300;

    /// <summary>When true, the grant has been revoked and presigned URLs must be rejected.</summary>
    public bool IsRevoked { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>SEC-003 AC-29: grant validity window; expired grants deny presign/download.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}
