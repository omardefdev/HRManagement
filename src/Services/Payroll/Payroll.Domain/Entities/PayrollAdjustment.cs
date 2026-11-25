using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "تسوية الراتب")]
public class PayrollAdjustment : AuditableEntity, IManualPayInput
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تشغيل الرواتب")]
    public Guid? PayrollRunId { get; set; }

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "سبب التسوية مطلوب")]
    [Display(Name = "سبب التسوية")]
    public string? AdjustmentReason { get; set; }

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

    /// <summary>
    /// PLAN-56 (G-PAY-03): an adjustment aimed at the correction of one posted run. A regular run never takes it, and the correction
    /// run of that source takes nothing else. Null for an ordinary adjustment, which the next regular run takes.
    /// </summary>
    [Display(Name = "تصحيح للتشغيل المرحّل")]
    public Guid? TargetSourceRunId { get; set; }

    public PayrollRun? PayrollRun { get; set; }

    public PayrollRun? TargetSourceRun { get; set; }
}
