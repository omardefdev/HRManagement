using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Records.Archive.Domain.Entities;

[Display(Name = "تصنيف الوثيقة")]
public class DocumentClassification : AuditableEntity
{
    [Required(ErrorMessage = "الرمز مطلوب")]
    [Display(Name = "الرمز")]
    public string Code { get; set; } = default!;

    [Required]
    [Display(Name = "الاسم بالعربي")]
    public string NameAr { get; set; } = default!;

    [Display(Name = "الاسم بالإنكليزي")]
    public string? NameEn { get; set; }

    [Display(Name = "سنوات الاحتفاظ")]
    public int? RetentionYears { get; set; }

    /// <summary>FUN-006: GOV-001 schedule code (e.g. ret-archive-docs).</summary>
    [MaxLength(64)]
    [Display(Name = "رمز جدول الاحتفاظ")]
    public string? RetentionCode { get; set; }
}
