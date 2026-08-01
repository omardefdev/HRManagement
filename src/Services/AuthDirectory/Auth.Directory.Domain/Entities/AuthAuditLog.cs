using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "سجل التدقيق")]
public class AuthAuditLog : BaseEntity
{
    [Display(Name = "الإجراء")]
    [Required]
    public string Action { get; set; } = string.Empty;

    [Display(Name = "نوع الكيان")]
    [Required]
    public string EntityType { get; set; } = string.Empty;

    [Display(Name = "معرّف الكيان")]
    public Guid EntityId { get; set; }

    [Display(Name = "نُفّذ بواسطة")]
    public string? PerformedBy { get; set; }

    [Display(Name = "وقت التنفيذ")]
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "التفاصيل")]
    public string? Details { get; set; }
}
