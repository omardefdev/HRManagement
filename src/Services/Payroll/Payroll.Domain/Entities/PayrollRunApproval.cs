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

    public PayrollRun PayrollRun { get; set; } = null!;
}
