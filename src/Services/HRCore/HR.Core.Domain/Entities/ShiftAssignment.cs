using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "إسناد وردية")]
public class ShiftAssignment : AuditableEntity
{
    [Display(Name = "جدول المناوبات")]
    public Guid ShiftRosterId { get; set; }

    [Display(Name = "الوردية")]
    public Guid ShiftId { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ العمل")]
    public DateOnly WorkDate { get; set; }

    [Display(Name = "ساعات العمل الإضافي")]
    [Range(0, 24, ErrorMessage = "ساعات العمل الإضافي بين 0 و24")]
    public decimal OvertimeHours { get; set; }

    public virtual ShiftRoster? ShiftRoster { get; set; }
    public virtual Shift? Shift { get; set; }
    public virtual Employee? Employee { get; set; }
}
