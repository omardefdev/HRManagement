using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Records.Archive.Domain.Entities;

[Display(Name = "ملف ورقي")]
public class PhysicalFileRecord : AuditableEntity
{
    [Required]
    [Display(Name = "عنصر الأرشيف")]
    public Guid ArchiveItemId { get; set; }

    [Display(Name = "رقم الصندوق")]
    public string? BoxNumber { get; set; }

    [Display(Name = "عدد الصفحات")]
    public int? PageCount { get; set; }

    [Display(Name = "الحالة")]
    public string? Condition { get; set; }

    public ArchiveItem ArchiveItem { get; set; } = default!;
}
