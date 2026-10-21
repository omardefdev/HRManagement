using System.ComponentModel.DataAnnotations;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "الأصل / العهدة")]
public class Asset : AuditableEntity
{
    [Required(ErrorMessage = "رقم العهدة مطلوب")]
    [Display(Name = "رقم العهدة")]
    public string TagNumber { get; set; } = default!;

    [Required(ErrorMessage = "اسم الأصل مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = default!;

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

    /// <summary>FUN-005: policy-classified privileged assets require evidence on assign/return.</summary>
    [Display(Name = "تصنيف مميز (سياسة)")]
    public bool IsPrivileged { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public ICollection<AssetAssignment> Assignments { get; set; } = new List<AssetAssignment>();
    public ICollection<AssetLossReport> LossReports { get; set; } = new List<AssetLossReport>();
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; } = new List<MaintenanceRecord>();

    [Display(Name = "مصدر التوريد (سجل الاستلام)")]
    public Guid? OriginReceiptRecordId { get; set; }

    public ReceiptRecord? OriginReceiptRecord { get; set; }

    [Display(Name = "بند طلب التجهيز المصدر")]
    public Guid? OriginSupplyOrderLineId { get; set; }

    public SupplyOrderLine? OriginSupplyOrderLine { get; set; }

    [Display(Name = "أصل قديم / هبة")]
    public bool IsLegacyEntry { get; set; }

    [Display(Name = "سبب الإدخال الاستثنائي")]
    [MaxLength(256)]
    public string? LegacyEntryReason { get; set; }
}
