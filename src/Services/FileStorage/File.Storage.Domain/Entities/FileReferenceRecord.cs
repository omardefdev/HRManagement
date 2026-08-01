using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

[Display(Name = "مرجع ملف")]
public class FileReferenceRecord : BaseEntity
{
    public Guid FileId { get; set; }

    public FileRecord File { get; set; } = default!;

    [Display(Name = "مفتاح الحامل")]
    [Required]
    [StringLength(256)]
    public string HolderKey { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
