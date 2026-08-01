using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Records.Archive.Domain.Entities;

[Display(Name = "سجل الإتلاف")]
public class DisposalRecord : BaseEntity
{
    [Required]
    [Display(Name = "عنصر الأرشيف")]
    public Guid ArchiveItemId { get; set; }

    [Required]
    [Display(Name = "تاريخ الإتلاف")]
    public DateTime DisposalDate { get; set; }

    [Display(Name = "الطريقة")]
    public string? Method { get; set; }

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    public Guid? ApprovalEvidenceFileId { get; set; }

    public ArchiveItem ArchiveItem { get; set; } = default!;
}
