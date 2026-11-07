using System.ComponentModel.DataAnnotations;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Admin.Logistics.Domain.Entities;

[Display(Name = "تقرير فقدان أصل")]
public class AssetLossReport : AuditableEntity, IAssetApprovalRequest
{
    [Required]
    [Display(Name = "الأصل")]
    public Guid AssetId { get; set; }

    [Display(Name = "حالة التقرير")]
    public AssetLossReportStatus Status { get; set; } = AssetLossReportStatus.ApprovalStarting;

    [Display(Name = "السياق")]
    public AssetLossContext Context { get; set; }

    [Display(Name = "السبب")]
    public AssetLossReason Reason { get; set; }

    [MaxLength(256)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [MaxLength(256)]
    [Display(Name = "مرجع الدليل")]
    public string? EvidenceRef { get; set; }

    [Display(Name = "ملف الدليل")]
    public Guid? LossFileId { get; set; }

    public Guid? WorkflowInstanceId { get; set; }

    [MaxLength(200)]
    [Display(Name = "طالب الإجراء")]
    public string? RequestedByName { get; set; }

    [Display(Name = "محاولات بدء الاعتماد")]
    public int ApprovalStartAttempts { get; set; }

    [MaxLength(500)]
    [Display(Name = "آخر خطأ في بدء الاعتماد")]
    public string? ApprovalStartLastError { get; set; }

    [Display(Name = "آخر محاولة لبدء الاعتماد")]
    public DateTime? ApprovalStartLastAttemptAt { get; set; }

    [Display(Name = "يتطلب تدخلاً")]
    public bool NeedsIntervention { get; set; }

    [Display(Name = "تاريخ السحب")]
    public DateTime? WithdrawnAt { get; set; }

    [Display(Name = "سحبه")]
    public Guid? WithdrawnBy { get; set; }

    [MaxLength(500)]
    [Display(Name = "سبب السحب")]
    public string? WithdrawnReason { get; set; }

    [Display(Name = "تاريخ تأكيد الفقدان")]
    public DateTime? ConfirmedLostAt { get; set; }

    [Display(Name = "تاريخ الاسترداد")]
    public DateTime? RecoveredAt { get; set; }

    [MaxLength(256)]
    [Display(Name = "ملاحظات الاسترداد")]
    public string? RecoveryNotes { get; set; }

    [Display(Name = "حالة الأصل قبل الفقد")]
    public AssetStatus? BeforeAssetStatus { get; set; }

    [Display(Name = "لقطة قبل الفقد")]
    public string? BeforeStateJson { get; set; }

    [Display(Name = "حالة الأصل بعد الفقد/التسوية")]
    public AssetStatus? AfterAssetStatus { get; set; }

    [Display(Name = "لقطة بعد الفقد/التسوية")]
    public string? AfterStateJson { get; set; }

    [Display(Name = "تاريخ اعتماد التسوية")]
    public DateTime? SettlementApprovedAt { get; set; }

    [MaxLength(256)]
    [Display(Name = "معتمد التسوية")]
    public string? SettlementApprovedBy { get; set; }

    public Asset Asset { get; set; } = default!;
}
