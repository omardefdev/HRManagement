using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IDCard.Domain.Entities;

[Display(Name = "قائمة صلاحيات البطاقات")]
public class CardPermissionSet : AuditableEntity
{
    [Required(ErrorMessage = "اسم مجموعة الصلاحيات مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = null!;

    [Display(Name = "الصلاحيات")]
    public string? Permissions { get; set; }
}
