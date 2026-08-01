using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "نشرة التنقلات")]
public class MovementNotice : AuditableEntity
{
    [Required(ErrorMessage = "العنوان مطلوب")]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ الإصدار")]
    public DateOnly IssueDate { get; set; }

    [Display(Name = "الفترة")]
    public string? Period { get; set; }

    [Display(Name = "البنود")]
    public string? Items { get; set; }

    [Display(Name = "صادر عن")]
    public Guid? IssuedBy { get; set; }
}
