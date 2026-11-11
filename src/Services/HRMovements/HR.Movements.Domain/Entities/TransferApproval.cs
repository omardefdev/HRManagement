using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "موافقة النقل")]
public class TransferApproval : BaseEntity
{
    [Required]
    [Display(Name = "طلب النقل")]
    public Guid TransferRequestId { get; set; }

    [Required]
    [Display(Name = "المعتمد")]
    public Guid ApprovedBy { get; set; }

    [Required]
    [Display(Name = "تاريخ الموافقة")]
    public DateTime ApprovedAt { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>PLAN-50 (§7): the principal for whom <see cref="ApprovedBy"/> approved through a delegation; null for a direct approval.</summary>
    [Display(Name = "بالنيابة عن")]
    public Guid? OnBehalfOfUserId { get; set; }

    [MaxLength(200)]
    [Display(Name = "اسم الأصيل")]
    public string? OnBehalfOfName { get; set; }

    public TransferRequest TransferRequest { get; set; } = null!;
}
