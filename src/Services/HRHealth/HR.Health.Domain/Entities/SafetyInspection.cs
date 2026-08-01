using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "تفتيش السلامة")]
public class SafetyInspection : AuditableEntity
{
    [Required(ErrorMessage = "الموقع مطلوب")]
    [Display(Name = "الموقع المُفتَّش")]
    [StringLength(200)]
    public string Location { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ التفتيش")]
    public DateOnly InspectionDate { get; set; }

    [Display(Name = "المُفتِّش")]
    [StringLength(200)]
    public string? Inspector { get; set; }

    [Display(Name = "مطابِق للمعايير")]
    public bool Passed { get; set; } = true;

    [Display(Name = "الملاحظات والمخالفات")]
    public string? Findings { get; set; }

    [Display(Name = "موعد التفتيش التالي")]
    public DateOnly? NextDue { get; set; }
}
