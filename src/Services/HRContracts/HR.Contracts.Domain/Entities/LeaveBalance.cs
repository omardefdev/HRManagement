using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "رصيد الإجازات")]
public class LeaveBalance : BaseEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الإجازة")]
    public LeaveType LeaveType { get; set; }

    [Display(Name = "السنة")]
    public int Year { get; set; }

    [Display(Name = "الاستحقاق الكلي (أيام)")]
    public int TotalEntitlement { get; set; }

    [Display(Name = "المستخدم (أيام)")]
    public int Used { get; set; }

    [Display(Name = "المتبقي (أيام)")]
    public int Remaining => TotalEntitlement - Used;
}
