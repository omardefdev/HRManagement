using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Constants;

namespace Payroll.Domain.Entities;

[Display(Name = "حجز صرف")]
public class PaymentHold : AuditableEntity
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تشغيل الرواتب")]
    public Guid? PayrollRunId { get; set; }

    [Display(Name = "تنبيه الاحتيال")]
    public Guid? FraudAlertId { get; set; }

    [Required(ErrorMessage = "سبب الحجز مطلوب")]
    [Display(Name = "السبب")]
    [MaxLength(2000)]
    public string Reason { get; set; } = "";

    [Required]
    [Display(Name = "نوع الحجز")]
    [MaxLength(32)]
    public string HoldType { get; set; } = PaymentHoldType.Fraud;

    [Required]
    [Display(Name = "الحالة")]
    [MaxLength(32)]
    public string Status { get; set; } = PaymentHoldStatus.Active;

    [Display(Name = "أُفرج بواسطة")]
    public Guid? ReleasedBy { get; set; }

    [Display(Name = "اعتمد الإفراج")]
    public Guid? ReleaseApprovedBy { get; set; }

    [Display(Name = "سبب الإفراج")]
    [MaxLength(2000)]
    public string? ReleaseReason { get; set; }

    [Display(Name = "سجل الأدلة")]
    public Guid? EvidenceRecordId { get; set; }

    [Display(Name = "وقت الحجز")]
    public DateTime HeldAtUtc { get; set; } = DateTime.UtcNow;

    [Display(Name = "وقت الإفراج")]
    public DateTime? ReleasedAtUtc { get; set; }
}
