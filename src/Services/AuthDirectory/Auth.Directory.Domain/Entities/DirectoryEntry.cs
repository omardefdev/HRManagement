using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "مدخل الدليل المركزي")]
public class DirectoryEntry : AuditableEntity
{
    [Display(Name = "معرّف الموظف")]
    [Required(ErrorMessage = "معرّف الموظف مطلوب")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الاسم المعروض")]
    [Required(ErrorMessage = "الاسم المعروض مطلوب")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "البريد الإلكتروني الرئيسي")]
    public string? PrimaryEmail { get; set; }

    [Display(Name = "الهاتف الرئيسي")]
    public string? PrimaryPhone { get; set; }

    [Display(Name = "الوحدة التنظيمية الرئيسية")]
    public Guid? PrimaryOrgUnitId { get; set; }

    [Display(Name = "الحالة")]
    public string? Status { get; set; }

    [Display(Name = "آخر مزامنة")]
    public DateTime? LastSyncedAt { get; set; }
}
