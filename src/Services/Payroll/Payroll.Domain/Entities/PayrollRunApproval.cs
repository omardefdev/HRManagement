using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "اعتماد تشغيل الرواتب")]
public class PayrollRunApproval : BaseEntity
{
    [Required]
    [ForeignKey(nameof(PayrollRun))]
    public Guid PayrollRunId { get; set; }

    [Display(Name = "المعتمد")]
    public Guid? ApproverId { get; set; }

    [MaxLength(100)]
    [Display(Name = "الدور")]
    public string? ApproverRole { get; set; }

    [Display(Name = "القرار")]
    public ApprovalDecision Decision { get; set; } = ApprovalDecision.Pending;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime? DecidedAt { get; set; }

    /// <summary>
    /// PLAN-58 (D-PAY-5): the version of the run's results the decision was taken on (<see cref="PayrollRun.Revision"/>). The approvals
    /// a run needs are counted on its present version: a rejection reopens the run and its earlier approvals no longer count.
    /// </summary>
    [Display(Name = "رقم النسخة")]
    public int Revision { get; set; } = 1;

    public PayrollRun PayrollRun { get; set; } = null!;
}
