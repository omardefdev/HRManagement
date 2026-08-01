using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "دور المستخدم")]
public class UserRole : BaseEntity
{
    [Display(Name = "معرّف المستخدم")]
    public Guid AuthUserId { get; set; }

    [Display(Name = "معرّف الدور")]
    public Guid RoleId { get; set; }

    [Display(Name = "تاريخ التعيين")]
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "عُيّن بواسطة")]
    public string? AssignedBy { get; set; }

    public virtual AuthUser AuthUser { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
}
