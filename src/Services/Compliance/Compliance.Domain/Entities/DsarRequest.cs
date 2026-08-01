using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

/// <summary>GOV-001-E: lightweight DSAR (Access / Rectify) workflow.</summary>
[Display(Name = "طلب حقوق الذات (DSAR)")]
public class DsarRequest : AuditableEntity
{
    [Display(Name = "نوع الطلب")]
    public DsarRequestType RequestType { get; set; }

    [Display(Name = "الحالة")]
    public DsarRequestStatus Status { get; set; } = DsarRequestStatus.Open;

    [Display(Name = "معرّف الموظف")]
    public Guid? SubjectEmployeeId { get; set; }

    /// <summary>SHA-256 hex of national ID when employee id is unknown (never store raw).</summary>
    [Display(Name = "تجزئة الرقم الوطني")]
    [StringLength(64)]
    [AuditRedact]
    public string? SubjectNationalIdHash { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "تاريخ الإكمال")]
    public DateTime? CompletedAt { get; set; }

    [Display(Name = "ملاحظات")]
    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>JSON discovery package (masked PII) assembled at fulfill time.</summary>
    [Display(Name = "حزمة الاكتشاف")]
    public string? DiscoveryPackageJson { get; set; }
}
