using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "خصم / اقتطاع")]
public class DeductionRecord : AuditableEntity, IManualPayInput
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "نوع الخصم مطلوب")]
    [Display(Name = "نوع الخصم")]
    public string DeductionType { get; set; } = null!;

    [Required(ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    /// <summary>
    /// Period this deduction becomes payable. Null means "recover at the next opportunity".
    /// Advance instalments carry a schedule so a run only recovers what has actually fallen due;
    /// without it the whole repayment plan was visible to — and consumed by — the first run.
    /// </summary>
    [Display(Name = "تاريخ الاستحقاق")]
    public DateOnly? DueDate { get; set; }

    /// <summary>1-based position within a multi-instalment plan; null for one-off deductions.</summary>
    [Display(Name = "رقم القسط")]
    public int? InstallmentNumber { get; set; }

    /// <summary>Stable key of the upstream fact (for advances: <c>AdvanceRequestId=&lt;guid&gt;</c>).</summary>
    [MaxLength(100)]
    [Display(Name = "مرجع المصدر")]
    public string? SourceReference { get; set; }

    [Display(Name = "مطبق في تشغيل")]
    public Guid? AppliedInRunId { get; set; }

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

    public PayrollRun? AppliedInRun { get; set; }
}
