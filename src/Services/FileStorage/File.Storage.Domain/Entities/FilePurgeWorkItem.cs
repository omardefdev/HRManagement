using System.ComponentModel.DataAnnotations;
using File.Storage.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

[Display(Name = "عنصر طابور حذف ملف")]
public class FilePurgeWorkItem : BaseEntity
{
    public Guid FileId { get; set; }

    [Required]
    [StringLength(128)]
    public string Bucket { get; set; } = default!;

    [Required]
    [StringLength(512)]
    public string ObjectKey { get; set; } = default!;

    public int Attempt { get; set; }

    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    [StringLength(2000)]
    public string? LastError { get; set; }

    [StringLength(32)]
    public string Status { get; set; } = FilePurgeWorkItemStatus.Pending;

    public DateTime? CompletedAt { get; set; }
}
