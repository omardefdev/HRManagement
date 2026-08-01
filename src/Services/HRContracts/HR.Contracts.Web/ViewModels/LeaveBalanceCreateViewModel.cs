using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Web.ViewModels;

/// <summary>
/// Input for opening a leave balance.
/// <para>
/// Binding the <c>LeaveBalance</c> entity directly exposed <c>Used</c> — the consumed-days counter
/// that leave approval maintains — alongside <c>Id</c> and <c>TenantId</c>. A balance can only be
/// opened with nothing consumed; days are consumed by approving leave, never by typing a number
/// into a form.
/// </para>
/// </summary>
public class LeaveBalanceCreateViewModel
{
    [Display(Name = "الموظف")]
    [Required(ErrorMessage = "الموظف مطلوب")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الإجازة")]
    public LeaveType LeaveType { get; set; }

    [Display(Name = "السنة")]
    [Range(2000, 2200, ErrorMessage = "سنة غير صالحة")]
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Display(Name = "الاستحقاق الكلي (أيام)")]
    [Range(0, 365, ErrorMessage = "الاستحقاق يجب أن يكون بين 0 و365 يوماً")]
    public int TotalEntitlement { get; set; }
}
