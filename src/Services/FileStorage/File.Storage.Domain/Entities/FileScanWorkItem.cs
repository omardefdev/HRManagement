using System.ComponentModel.DataAnnotations;
using File.Storage.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

[Display(Name = "عنصر طابور فحص ملف")]
public class FileScanWorkItem : BaseEntity
{
    public Guid FileId { get; set; }

    public int Attempt { get; set; }

    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    [StringLength(2000)]
    public string? LastError { get; set; }

    [StringLength(32)]
    public string Status { get; set; } = FileScanWorkItemStatus.Pending;

    public DateTime? CompletedAt { get; set; }
}
