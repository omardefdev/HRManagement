using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IDCard.Domain.Entities;

[Display(Name = "صلاحية الدخول")]
public class AccessPermission : AuditableEntity
{
    [Required]
    [Display(Name = "البطاقة")]
    public Guid IssuedCardId { get; set; }

    [Required(ErrorMessage = "المورد مطلوب")]
    [Display(Name = "المورد")]
    public string Resource { get; set; } = null!;

    [Display(Name = "مستوى الوصول")]
    public string? AccessLevel { get; set; }

    [Display(Name = "ساري من")]
    public DateOnly? ValidFrom { get; set; }

    [Display(Name = "ساري إلى")]
    public DateOnly? ValidTo { get; set; }

    public IssuedCard IssuedCard { get; set; } = null!;
}
