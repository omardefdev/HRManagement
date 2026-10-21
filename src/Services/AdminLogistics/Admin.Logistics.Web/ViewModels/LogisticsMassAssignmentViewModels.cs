using System.ComponentModel.DataAnnotations;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Web.ViewModels;

public sealed class InventoryItemFormViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "رمز المخزون")]
    public string? Sku { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الكمية")]
    public int Quantity { get; set; }

    [Display(Name = "الوحدة")]
    public string? Unit { get; set; }

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [Display(Name = "حد إعادة الطلب")]
    public int? ReorderThreshold { get; set; }
}

public sealed class VendorFormViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم المورد مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "معلومات الاتصال")]
    public string? ContactInfo { get; set; }

    [Display(Name = "الدولة")]
    public string? Country { get; set; }
}

public sealed class SupplyOrderFormViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "التفاصيل")]
    public string? Items { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime? OrderedAt { get; set; }

    public SupplyOrderStatus Status { get; set; }

    public Guid? RequestedBy { get; set; }
}

public sealed class MaintenanceRecordFormViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "الأصل")]
    [Required(ErrorMessage = "يجب اختيار الأصل")]
    public Guid AssetId { get; set; }

    [Display(Name = "نوع الصيانة")]
    public MaintenanceType MaintenanceType { get; set; }

    [Display(Name = "تاريخ التنفيذ")]
    public DateTime? PerformedAt { get; set; }

    [Display(Name = "نفذ بواسطة")]
    public string? PerformedBy { get; set; }

    [Display(Name = "التكلفة")]
    public decimal? Cost { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public sealed class ReceiptRecordFormViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "طلب التجهيز")]
    public Guid? SupplyOrderId { get; set; }

    [Display(Name = "استلم بواسطة")]
    public Guid? ReceivedBy { get; set; }

    [Display(Name = "تاريخ الاستلام")]
    public DateTime? ReceivedAt { get; set; }

    [Display(Name = "المواد المستلمة")]
    public string? ItemsReceived { get; set; }

    [Display(Name = "عنصر المخزون")]
    public Guid? InventoryItemId { get; set; }

    [Display(Name = "الكمية المستلمة")]
    public int? QuantityReceived { get; set; }
}

public sealed class AssetEditFormViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "رقم العهدة")]
    public string TagNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الأصل مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الفئة")]
    public string? Category { get; set; }

    [Display(Name = "الماركة")]
    public string? Brand { get; set; }

    [Display(Name = "الموديل")]
    public string? Model { get; set; }

    [Display(Name = "تاريخ الشراء")]
    public DateTime? PurchaseDate { get; set; }

    [Display(Name = "القيمة")]
    public decimal? Value { get; set; }

    [Display(Name = "الحالة")]
    public AssetStatus Status { get; set; }

    [Display(Name = "مصدر التوريد (سجل الاستلام)")]
    public Guid? OriginReceiptRecordId { get; set; }

    [Display(Name = "أصل قديم / هبة")]
    public bool IsLegacyEntry { get; set; }

    [Display(Name = "سبب الإدخال الاستثنائي")]
    [MaxLength(256)]
    public string? LegacyEntryReason { get; set; }

    /// <summary>Read-only display for Edit view (origin receipt lineage).</summary>
    public DateTime? OriginReceiptReceivedAt { get; set; }

    public string? OriginReceiptItemsReceived { get; set; }

    public bool IsPrivileged { get; set; }
}

public sealed class AssetLegacyCreateFormViewModel
{
    [Required(ErrorMessage = "اسم الأصل مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الفئة")]
    public string? Category { get; set; }

    [Display(Name = "الماركة")]
    public string? Brand { get; set; }

    [Display(Name = "الموديل")]
    public string? Model { get; set; }

    [Display(Name = "تاريخ الشراء")]
    public DateTime? PurchaseDate { get; set; }

    [Display(Name = "القيمة")]
    public decimal? Value { get; set; }

    [Display(Name = "الحالة")]
    public AssetStatus Status { get; set; } = AssetStatus.InStorage;

    [Display(Name = "سبب الإدخال الاستثنائي")]
    [MaxLength(256)]
    public string? LegacyEntryReason { get; set; }
}
