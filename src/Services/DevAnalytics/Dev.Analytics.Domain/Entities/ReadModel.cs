using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "نموذج القراءة")]
public class ReadModel : BaseEntity
{
    [Display(Name = "الاسم")]
    [Required(ErrorMessage = "الاسم مطلوب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "المخطط")]
    public string? Schema { get; set; }

    [Display(Name = "آخر تحديث")]
    public DateTime? LastRefreshed { get; set; }

    [MaxLength(128)]
    public string? LastFingerprint { get; set; }

    [MaxLength(64)]
    public string? SourceEventName { get; set; }
}
