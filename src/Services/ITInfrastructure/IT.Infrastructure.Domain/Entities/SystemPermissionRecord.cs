using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "سجل الصلاحيات التقنية")]
public class SystemPermissionRecord : BaseEntity
{
    [Required]
    [Display(Name = "المستخدم")]
    public Guid UserId { get; set; }

    [Required]
    [Display(Name = "النظام")]
    public string System { get; set; } = default!;

    [Display(Name = "الأدوار")]
    public string? Roles { get; set; }

    [Display(Name = "تاريخ المنح")]
    public DateTime GrantedAt { get; set; }

    [Display(Name = "تاريخ السحب")]
    public DateTime? RevokedAt { get; set; }
}
