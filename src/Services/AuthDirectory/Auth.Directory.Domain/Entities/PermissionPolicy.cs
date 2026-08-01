using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "سياسة الصلاحيات")]
public class PermissionPolicy : AuditableEntity
{
    [Display(Name = "اسم السياسة")]
    [Required(ErrorMessage = "اسم السياسة مطلوب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "القواعد")]
    public string? Rules { get; set; }
}
