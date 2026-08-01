using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IT.Infrastructure.Domain.Enums;

namespace IT.Infrastructure.Domain.Entities;

[Display(Name = "تذكرة الدعم الفني")]
public class ITSupportTicket : AuditableEntity
{
    [Display(Name = "مُبلغ بواسطة")]
    public Guid? ReportedBy { get; set; }

    [Required(ErrorMessage = "ملخص المشكلة مطلوب")]
    [Display(Name = "ملخص المشكلة")]
    public string IssueSummary { get; set; } = default!;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "الحالة")]
    public TicketStatus Status { get; set; }

    [Display(Name = "مُسند إلى")]
    public Guid? AssignedTo { get; set; }

    [Display(Name = "تاريخ الحل")]
    public DateTime? ResolvedAt { get; set; }

    /// <summary>FUN-005: hire provisioning outcome (directory sync).</summary>
    public ProvisioningStatus ProvisioningStatus { get; set; }

    [MaxLength(500)]
    public string? ProvisioningError { get; set; }

    public DateTime? ProvisionedAt { get; set; }
}
