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

    public TransferRequest TransferRequest { get; set; } = null!;
}
