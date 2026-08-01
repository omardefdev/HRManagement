using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "ملف قضائي")]
public class LegalCase : AuditableEntity
{
    [Display(Name = "رقم القضية")]
    [Required(ErrorMessage = "رقم القضية مطلوب")]
    public string CaseNumber { get; set; } = string.Empty;

    [Display(Name = "الموضوع")]
    [Required(ErrorMessage = "الموضوع مطلوب")]
    public string Subject { get; set; } = string.Empty;

    [Display(Name = "الأطراف")]
    public string? Parties { get; set; }

    [Display(Name = "الحالة")]
    public CaseStatus Status { get; set; }

    [Display(Name = "تفاصيل المحكمة")]
    public string? CourtDetails { get; set; }

    [Display(Name = "مسند إلى")]
    public Guid? AssignedTo { get; set; }
}
