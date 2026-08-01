using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "طلب الإجازة")]
public class LeaveRequest : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "نوع الإجازة")]
    public LeaveType LeaveType { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    [Required]
    public DateOnly EndDate { get; set; }

    [Display(Name = "عدد الأيام")]
    public int DurationDays { get; set; }

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; }

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public Guid? ServiceTransactionId { get; set; }
}
