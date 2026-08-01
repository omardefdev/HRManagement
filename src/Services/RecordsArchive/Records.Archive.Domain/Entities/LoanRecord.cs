using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Records.Archive.Domain.Enums;

namespace Records.Archive.Domain.Entities;

[Display(Name = "سجل إعارة")]
public class LoanRecord : BaseEntity
{
    [Required]
    [Display(Name = "عنصر الأرشيف")]
    public Guid ArchiveItemId { get; set; }

    [Required(ErrorMessage = "المستعير مطلوب")]
    [Display(Name = "المستعير")]
    public string Borrower { get; set; } = default!;

    [Required]
    [Display(Name = "تاريخ الإعارة")]
    public DateTime LoanDate { get; set; }

    [Required]
    [Display(Name = "تاريخ الاستحقاق")]
    public DateTime DueDate { get; set; }

    [Display(Name = "تاريخ الإعادة")]
    public DateTime? ReturnedDate { get; set; }

    [Display(Name = "الحالة")]
    public LoanStatus Status { get; set; }

    public ArchiveItem ArchiveItem { get; set; } = default!;
}
