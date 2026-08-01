using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "وصفة البيئة التشغيلية")]
public class EnvironmentConfiguration : BaseEntity
{
    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = default!;

    [Display(Name = "الإعدادات")]
    public string? Config { get; set; }

    [Display(Name = "الإصدار")]
    public string? Version { get; set; }

    [Display(Name = "تاريخ التطبيق")]
    public DateTime? AppliedAt { get; set; }
}
