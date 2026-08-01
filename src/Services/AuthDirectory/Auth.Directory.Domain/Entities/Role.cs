using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "الدور / الصلاحية")]
public class Role : AuditableEntity
{
    [Display(Name = "اسم الدور")]
    [Required(ErrorMessage = "اسم الدور مطلوب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "الصلاحيات")]
    public string? Permissions { get; set; }
}
