using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

[Display(Name = "استثناء تسوية ملفات")]
public class FileReconcileException : BaseEntity
{
    [Required]
    [StringLength(64)]
    public string Kind { get; set; } = default!;

    public Guid? FileId { get; set; }

    [StringLength(128)]
    public string? Bucket { get; set; }

    [StringLength(512)]
    public string? ObjectKey { get; set; }

    [Required]
    [StringLength(2000)]
    public string Detail { get; set; } = default!;

    public bool Approved { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }
}
