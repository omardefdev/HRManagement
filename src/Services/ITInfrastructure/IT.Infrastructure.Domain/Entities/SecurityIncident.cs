using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IT.Infrastructure.Domain.Enums;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "حادثة أمنية")]
public class SecurityIncident : AuditableEntity
{
    [Required(ErrorMessage = "العنوان مطلوب")]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = default!;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "الشدة")]
    public Severity Severity { get; set; }

    [Display(Name = "تاريخ الإبلاغ")]
    public DateTime ReportedAt { get; set; }

    [Display(Name = "الحالة")]
    public TicketStatus Status { get; set; }

    [Display(Name = "معالج بواسطة")]
    public Guid? HandledBy { get; set; }
}
