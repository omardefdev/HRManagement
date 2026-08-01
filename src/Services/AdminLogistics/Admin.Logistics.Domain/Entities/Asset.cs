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

    /// <summary>FUN-005: privileged assets require evidence on assign/return.</summary>
    public bool IsPrivileged { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public ICollection<AssetAssignment> Assignments { get; set; } = new List<AssetAssignment>();
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; } = new List<MaintenanceRecord>();
}
