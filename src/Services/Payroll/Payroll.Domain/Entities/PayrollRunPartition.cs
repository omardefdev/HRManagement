using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "قسم تشغيل الرواتب")]
public class PayrollRunPartition : BaseEntity
{
    public Guid PayrollRunJobId { get; set; }

    public int PartitionIndex { get; set; }

    public PayrollRunPartitionStatus Status { get; set; } = PayrollRunPartitionStatus.Pending;

    public int EmployeeCount { get; set; }

    public int ProcessedCount { get; set; }

    public Guid? CursorEmployeeId { get; set; }

    /// <summary>PERF-001: worker that claimed this partition.</summary>
    [MaxLength(100)]
    public string? WorkerId { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public PayrollRunJob Job { get; set; } = null!;
}
