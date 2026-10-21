using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "بند طلب التجهيز")]
public class SupplyOrderLine : BaseEntity
{
    [Required]
    public Guid SupplyOrderId { get; set; }

    [Display(Name = "رمز المخزون")]
    public string? Sku { get; set; }

    [Display(Name = "الكمية المطلوبة")]
    public int QtyRequested { get; set; }

    [Display(Name = "الكمية المستلمة")]
    public int QtyReceived { get; set; }

    [Display(Name = "تكلفة الوحدة")]
    public decimal? UnitCost { get; set; }

    [Display(Name = "قابل للرسملة")]
    public bool IsCapitalizable { get; set; }

    public Guid? InventoryItemId { get; set; }

    public SupplyOrder SupplyOrder { get; set; } = default!;
    public InventoryItem? InventoryItem { get; set; }
}
