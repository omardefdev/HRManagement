using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "مستخدم النظام")]
public class AuthUser : AuditableEntity
{
    [Display(Name = "اسم المستخدم")]
    [Required(ErrorMessage = "اسم المستخدم مطلوب")]
    [AuditRedact]
    public string Username { get; set; } = string.Empty;

    [Display(Name = "البريد الإلكتروني")]
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [AuditRedact]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "كلمة المرور (تجزئة)")]
    [Required]
    [AuditRedact]
    public string PasswordHash { get; set; } = string.Empty;

    [Display(Name = "معرّف الموظف")]
    public Guid? EmployeeId { get; set; }

    [Display(Name = "نطاق الوحدة التنظيمية")]
    public Guid? ScopeOrgUnitId { get; set; }

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "آخر تسجيل دخول")]
    public DateTime? LastLoginAt { get; set; }

    [Display(Name = "Keycloak User Id")]
    [StringLength(100)]
    public string? KeycloakUserId { get; set; }

    public virtual ICollection<AuthToken> Tokens { get; set; } = new List<AuthToken>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
