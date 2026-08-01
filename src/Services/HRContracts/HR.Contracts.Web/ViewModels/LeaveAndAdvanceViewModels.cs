using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HR.Contracts.Web.ViewModels;

public class LeaveRequestViewModel
{
    public Guid? Id { get; set; }

    [Display(Name = "معرّف الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الإجازة")]
    public LeaveType LeaveType { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly EndDate { get; set; }

    [Display(Name = "عدد الأيام")]
    public int DurationDays { get; set; }

    /// <summary>
    /// Server-assigned. Accepting an approval status from the request let a requester file a leave
    /// that was already approved, bypassing both the approver and the balance debit.
    /// </summary>
    [BindNever]
    [ValidateNever]
    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;

    /// <summary>Server-assigned from the approving user's identity, never from the form.</summary>
    [BindNever]
    [ValidateNever]
    public Guid? ApprovedBy { get; init; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class AdvanceRequestViewModel
{
    public Guid? Id { get; set; }

    [Display(Name = "معرّف الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "المبلغ")]
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
    public decimal Amount { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    /// <summary>Server-assigned; see <see cref="LeaveRequestViewModel.Status"/>.</summary>
    [BindNever]
    [ValidateNever]
    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;

    /// <summary>Server-assigned from the approving user's identity, never from the form.</summary>
    [BindNever]
    [ValidateNever]
    public Guid? ApprovedBy { get; init; }
}
