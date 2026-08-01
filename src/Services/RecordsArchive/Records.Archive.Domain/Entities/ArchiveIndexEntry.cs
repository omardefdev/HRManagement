using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Records.Archive.Domain.Entities;

[Display(Name = "فهرس الأرشيف")]
public class ArchiveIndexEntry : BaseEntity
{
    [Required]
    [Display(Name = "عنصر الأرشيف")]
    public Guid ArchiveItemId { get; set; }

    [Display(Name = "الكلمات المفتاحية")]
    public string? Keywords { get; set; }

    [Display(Name = "الملخص")]
    public string? Summary { get; set; }

    public ArchiveItem ArchiveItem { get; set; } = default!;
}
