using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "استحقاق البدلات")]
public class AllowanceRecord : AuditableEntity, IManualPayInput
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "نوع البدل مطلوب")]
    [Display(Name = "نوع البدل")]
    public Guid AllowanceTypeId { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "تاريخ السريان مطلوب")]
    [Display(Name = "ساري من")]
    [DataType(DataType.Date)]
    public DateTime EffectiveFrom { get; set; }

    [Display(Name = "ساري إلى")]
    [DataType(DataType.Date)]
    public DateTime? EffectiveTo { get; set; }

    /// <summary>PLAN-54: why a person entered the allowance by hand.</summary>
    [MaxLength(ManualPayInputRules.ReasonMaxLength)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    /// <summary>PLAN-54 (D-PAY-6): only an approved input counts in a calculation. Existing and system rows are approved.</summary>
    [Display(Name = "حالة الاعتماد")]
    public ManualInputApprovalStatus ApprovalStatus { get; set; } = ManualInputApprovalStatus.Approved;

    [Display(Name = "المصدر")]
    public ManualInputOrigin Origin { get; set; } = ManualInputOrigin.System;

    [Display(Name = "قرّر فيه")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime? ApprovedAt { get; set; }

    [MaxLength(ManualPayInputRules.DecisionNoteMaxLength)]
    [Display(Name = "ملاحظة القرار")]
    public string? DecisionNote { get; set; }

    public CompensationType AllowanceType { get; set; } = null!;
}
