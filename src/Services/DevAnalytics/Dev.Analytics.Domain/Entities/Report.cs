using System.ComponentModel.DataAnnotations;
using Dev.Analytics.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "التقرير")]
public class Report : AuditableEntity
{
    [Display(Name = "العنوان")]
    [Required(ErrorMessage = "العنوان مطلوب")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "نوع التقرير")]
    public ReportType ReportType { get; set; }

    [Display(Name = "المعاملات")]
    public string? Parameters { get; set; }

    [Display(Name = "تاريخ التوليد")]
    public DateTime? GeneratedAt { get; set; }

    [Display(Name = "تم التوليد بواسطة")]
    public Guid? GeneratedBy { get; set; }

    [Display(Name = "الرابط")]
    public string? Url { get; set; }

    /// <summary>FUN-006: Branch vs Tenant grain for golden reports.</summary>
    [MaxLength(32)]
    public string ScopeGrain { get; set; } = "Branch";

    [MaxLength(64)]
    public string? SourceEvent { get; set; }

    [MaxLength(128)]
    public string? LastFingerprint { get; set; }
}
