using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "سجل مراجعة الرواتب")]
public class PayrollAuditRecord : AuditableEntity
{
    [Required(ErrorMessage = "تشغيل الرواتب مطلوب")]
    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    [Display(Name = "المدقق")]
    public Guid? AuditorId { get; set; }

    [Display(Name = "أغلق بواسطة")]
    public Guid? ClosedBy { get; set; }

    [Display(Name = "النتائج")]
    public string? Findings { get; set; }

    [Display(Name = "الحالة")]
    public AuditStatus Status { get; set; }

    [Display(Name = "تاريخ الإغلاق")]
    public DateTime? ClosedAt { get; set; }

    public PayrollRun PayrollRun { get; set; } = null!;
}
