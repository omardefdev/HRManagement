using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "سجل الاستلام")]
public class ReceiptRecord : BaseEntity
{
    [Display(Name = "طلب التجهيز")]
    public Guid? SupplyOrderId { get; set; }

    [Display(Name = "استلم بواسطة")]
    public Guid? ReceivedBy { get; set; }

    [Display(Name = "تاريخ الاستلام")]
    public DateTime? ReceivedAt { get; set; }

    [Display(Name = "المواد المستلمة")]
    public string? ItemsReceived { get; set; }

    public SupplyOrder? SupplyOrder { get; set; }
}
