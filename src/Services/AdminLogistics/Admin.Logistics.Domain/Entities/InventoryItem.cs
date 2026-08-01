using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "عنصر المخزون")]
public class InventoryItem : AuditableEntity
{
    [Required(ErrorMessage = "رمز المخزون مطلوب")]
    [Display(Name = "رمز المخزون")]
    public string Sku { get; set; } = default!;

    [Required]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = default!;

    [Display(Name = "الكمية")]
    public int Quantity { get; set; }

    [Display(Name = "الوحدة")]
    public string? Unit { get; set; }

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [Display(Name = "حد إعادة الطلب")]
    public int? ReorderThreshold { get; set; }
}
