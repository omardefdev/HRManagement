using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "رمز الجلسة")]
public class AuthToken : BaseEntity
{
    [Display(Name = "معرّف المستخدم")]
    [Required(ErrorMessage = "معرّف المستخدم مطلوب")]
    public Guid UserId { get; set; }

    [Display(Name = "الرمز")]
    [Required(ErrorMessage = "الرمز مطلوب")]
    public string Token { get; set; } = string.Empty;

    [Display(Name = "تاريخ الإصدار")]
    public DateTime IssuedAt { get; set; }

    [Display(Name = "تاريخ الانتهاء")]
    public DateTime ExpiresAt { get; set; }

    [Display(Name = "ملغى")]
    public bool Revoked { get; set; }

    public virtual AuthUser User { get; set; } = null!;
}
