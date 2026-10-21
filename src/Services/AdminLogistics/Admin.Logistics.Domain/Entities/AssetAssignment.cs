using System.ComponentModel.DataAnnotations;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "سجل إعطاء العهدة")]
public class AssetAssignment : AuditableEntity
{
    [Required]
    [Display(Name = "الأصل")]
    public Guid AssetId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "تاريخ التسليم")]
    public DateTime AssignedAt { get; set; }

    [Display(Name = "تاريخ الإعادة المتوقع")]
    public DateTime? DueBackDate { get; set; }

    [Display(Name = "الحالة عند التسليم")]
    public AssetHandoverCondition? HandoverCondition { get; set; }

    [MaxLength(256)]
    [Display(Name = "ملاحظات الحالة")]
    public string? HandoverConditionNotes { get; set; }

    [MaxLength(256)]
    [Display(Name = "ملاحظات الإعادة")]
    public string? ReturnNotes { get; set; }

    [Display(Name = "تاريخ الإعادة")]
    public DateTime? ReturnedAt { get; set; }

    [MaxLength(256)]
    [Display(Name = "مرجع الدليل")]
    public string? EvidenceRef { get; set; }

    [Display(Name = "ملف التسليم")]
    public Guid? HandoverFileId { get; set; }

    [Display(Name = "ملف الإعادة")]
    public Guid? ReturnFileId { get; set; }

    [Display(Name = "حالة العهدة")]
    public AssetAssignmentStatus Status { get; set; } = AssetAssignmentStatus.Active;

    public Guid? WorkflowInstanceId { get; set; }

    public Asset Asset { get; set; } = default!;
}
