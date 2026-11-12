using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class DiwanCorrespondence : AuditableEntity
{
    [Required]
    [Display(Name = "المرجع")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "الموضوع")]
    public string Subject { get; set; } = string.Empty;

    [Display(Name = "الاتجاه")]
    public CorrespondenceDirection Direction { get; set; }

    [Display(Name = "الجهة")]
    public string? ExternalParty { get; set; }

    [Display(Name = "تاريخ التسجيل")]
    public DateTime RegisteredAt { get; set; }

    [Display(Name = "نص / ملخص")]
    public string? Body { get; set; }

    [Display(Name = "موظف مرتبط")]
    public Guid? RelatedEmployeeId { get; set; }

    [Display(Name = "معاملة الخدمة المرتبطة")]
    public Guid? ServiceTransactionId { get; set; }

    /// <summary>PLAN-52 (D-52-2): the record's org unit: its related employee's, else the restricted creator's scope root, else the central
    /// user's choice; empty = central (D-52-1).</summary>
    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    public virtual Employee? RelatedEmployee { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }
}
