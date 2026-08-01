using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "طلب السلفة")]
public class AdvanceRequest : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "المبلغ")]
    [Required(ErrorMessage = "المبلغ مطلوب")]
    public decimal Amount { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; }

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    public Guid? ServiceTransactionId { get; set; }
}
