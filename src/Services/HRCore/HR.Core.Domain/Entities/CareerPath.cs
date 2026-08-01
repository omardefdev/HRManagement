using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "المسار الوظيفي")]
public class CareerPath : AuditableEntity
{
    [Required(ErrorMessage = "عنوان المسار مطلوب")]
    [Display(Name = "عنوان المسار")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    public virtual ICollection<CareerPathStep> Steps { get; set; } = new List<CareerPathStep>();
}
