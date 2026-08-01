using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Web.ViewModels;

public class LeavePolicyCreateViewModel
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

public class LeavePolicyEditViewModel : LeavePolicyCreateViewModel
{
    public Guid Id { get; set; }
}
