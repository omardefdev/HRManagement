using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "سياسة الإجازة")]
public class LeavePolicy : BaseEntity
{
    [Display(Name = "نوع الإجازة")]
    [Required]
    public LeaveType LeaveType { get; set; }

    [Display(Name = "الحد الأقصى للأيام سنوياً")]
    public int MaxDaysPerYear { get; set; }

    [Display(Name = "تتطلب شهادة طبية")]
    public bool RequiresMedicalCertificate { get; set; }

    [Display(Name = "مدفوعة")]
    public bool IsPaid { get; set; } = true;

    [Display(Name = "الحد الأدنى لأيام الإشعار المسبق")]
    public int MinNoticeDays { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Description { get; set; }
}
