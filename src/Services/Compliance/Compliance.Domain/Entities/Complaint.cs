using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "شكوى / تظلم")]
public class Complaint : AuditableEntity
{
    [Display(Name = "مقدم الشكوى")]
    [Required(ErrorMessage = "مقدم الشكوى مطلوب")]
    [AuditRedact]
    public string FiledBy { get; set; } = string.Empty;

    [Display(Name = "ضد")]
    [AuditRedact]
    public string? Against { get; set; }

    [Display(Name = "الموضوع")]
    [Required(ErrorMessage = "الموضوع مطلوب")]
    public string Subject { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    [AuditRedact]
    public string? Description { get; set; }

    [Display(Name = "تاريخ التقديم")]
    public DateTime FiledAt { get; set; }

    [Display(Name = "الحالة")]
    public ComplaintStatus Status { get; set; }

    [Display(Name = "مسند إلى")]
    public Guid? AssignedTo { get; set; }

    [Display(Name = "القرار")]
    [AuditRedact]
    public string? Resolution { get; set; }

    /// <summary>FUN-004: whistleblower — hide FiledBy from non-investigators.</summary>
    public bool IsAnonymous { get; set; }

    /// <summary>Protected reporter identity (GUID) — not shown in UI except to investigator.</summary>
    public Guid? ReporterProtectedId { get; set; }

    public Guid? EvidenceFileId { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public virtual ICollection<ComplaintFollowUp> FollowUps { get; set; } = new List<ComplaintFollowUp>();
}
