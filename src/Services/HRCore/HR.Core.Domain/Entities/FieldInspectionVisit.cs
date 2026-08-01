using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class FieldInspectionVisit : AuditableEntity
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [Display(Name = "تاريخ الزيارة")]
    public DateOnly VisitDate { get; set; }

    [Display(Name = "قائد التفتيش")]
    public Guid? LeadInspectorId { get; set; }

    [Display(Name = "الملخص")]
    public string? Summary { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(32)]
    public string Status { get; set; } = "planned";
}
