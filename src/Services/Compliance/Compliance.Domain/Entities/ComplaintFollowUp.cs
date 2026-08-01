using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "سجل متابعة الشكوى")]
public class ComplaintFollowUp : BaseEntity
{
    [Display(Name = "معرّف الشكوى")]
    [Required(ErrorMessage = "معرّف الشكوى مطلوب")]
    public Guid ComplaintId { get; set; }

    [Display(Name = "الإجراء المتخذ")]
    public string? ActionTaken { get; set; }

    [Display(Name = "تم بواسطة")]
    public Guid? PerformedBy { get; set; }

    [Display(Name = "تاريخ التنفيذ")]
    public DateTime? PerformedAt { get; set; }

    public virtual Complaint Complaint { get; set; } = null!;
}
