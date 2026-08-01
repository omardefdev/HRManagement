using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "خطة التوزيع")]
public class DistributionPlan : AuditableEntity
{
    [Required]
    [Display(Name = "الوحدة التنظيمية")]
    public Guid OrgUnitId { get; set; }

    [Required]
    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    [Display(Name = "الأماكن المخطط لها")]
    public string? PlannedPositions { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    public new Guid? CreatedBy { get; set; }

    [Display(Name = "الحالة")]
    public DistributionPlanStatus Status { get; set; }

    [Display(Name = "طلب النقل المرتبط")]
    public Guid? TransferRequestId { get; set; }

    public TransferRequest? TransferRequest { get; set; }
}
