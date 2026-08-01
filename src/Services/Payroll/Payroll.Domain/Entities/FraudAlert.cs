using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Constants;

namespace Payroll.Domain.Entities;

[Display(Name = "تنبيه احتيال")]
public class FraudAlert : AuditableEntity
{
    [Display(Name = "تشغيل الرواتب")]
    public Guid? PayrollRunId { get; set; }

    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "معرف القاعدة مطلوب")]
    [Display(Name = "معرف القاعدة")]
    [MaxLength(16)]
    public string RuleId { get; set; } = "";

    [Required]
    [Display(Name = "الفئة")]
    [MaxLength(128)]
    public string Category { get; set; } = "";

    [Required]
    [Display(Name = "الخطورة")]
    [MaxLength(32)]
    public string Severity { get; set; } = "";

    [Required]
    [Display(Name = "الحالة")]
    [MaxLength(32)]
    public string Status { get; set; } = FraudAlertStatus.Open;

    [Display(Name = "المالك")]
    public Guid? OwnerId { get; set; }

    [Display(Name = "التفاصيل")]
    [MaxLength(2000)]
    public string? Details { get; set; }

    [Display(Name = "سبب الحل")]
    [MaxLength(2000)]
    public string? ResolutionReason { get; set; }

    [Display(Name = "حُل بواسطة")]
    public Guid? ResolvedBy { get; set; }

    [Display(Name = "اعتمد بواسطة")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "وقت الاكتشاف")]
    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;

    [Display(Name = "حجز الصرف")]
    public Guid? PaymentHoldId { get; set; }
}
