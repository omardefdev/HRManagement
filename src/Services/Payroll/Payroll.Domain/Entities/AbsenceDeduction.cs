using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "خصم غياب")]
public class AbsenceDeduction : BaseEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    [Display(Name = "أيام الغياب")]
    public int AbsentDays { get; set; }

    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [MaxLength(500)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    /// <summary>
    /// Stable key of the upstream fact this deduction represents (for unpaid leave:
    /// <c>LeaveRequestId=&lt;guid&gt;</c>). Carries a filtered unique index so a redelivered or
    /// replayed approval cannot create a second deduction for the same leave request.
    /// </summary>
    [MaxLength(100)]
    [Display(Name = "مرجع المصدر")]
    public string? SourceReference { get; set; }

    [Display(Name = "مطبّق في التشغيل")]
    public Guid? AppliedInRunId { get; set; }
}
