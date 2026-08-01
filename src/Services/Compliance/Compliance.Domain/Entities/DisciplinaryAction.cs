using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "إجراء تأديبي")]
public class DisciplinaryAction : AuditableEntity
{
    [Display(Name = "معرّف الموظف")]
    [Required(ErrorMessage = "معرّف الموظف مطلوب")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الإجراء")]
    public ActionType ActionType { get; set; }

    [Display(Name = "صادر من")]
    public Guid? IssuedBy { get; set; }

    [Display(Name = "تاريخ الإصدار")]
    public DateTime IssuedAt { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "حالة الاستئناف")]
    public AppealStatus AppealStatus { get; set; }

    [Display(Name = "الشكوى المصدر")]
    public Guid? ComplaintId { get; set; }

    public virtual Complaint? Complaint { get; set; }

    /// <summary>FUN-004: FILE-002 evidence attachment.</summary>
    public Guid? EvidenceFileId { get; set; }
}
