using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

/// <summary>Append-only scan audit row (FILE-002).</summary>
[Display(Name = "نتيجة فحص ملف")]
public class FileScanResult : BaseEntity
{
    public Guid FileId { get; set; }

    [Required]
    [StringLength(32)]
    public string Outcome { get; set; } = default!;

    [StringLength(128)]
    public string? ThreatName { get; set; }

    [Required]
    [StringLength(64)]
    public string ScannerName { get; set; } = default!;

    [StringLength(64)]
    public string? ScannerVersion { get; set; }

    public bool CdrApplied { get; set; }

    [StringLength(64)]
    public string? CdrProfile { get; set; }

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    [StringLength(2000)]
    public string? Detail { get; set; }
}
