using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "مهمة تشغيل الرواتب")]
public class PayrollRunJob : AuditableEntity
{
    public Guid PayrollRunId { get; set; }

    public PayrollRunJobPhase Phase { get; set; } = PayrollRunJobPhase.Snapshot;

    public PayrollRunJobStatus Status { get; set; } = PayrollRunJobStatus.Pending;

    public int Attempt { get; set; } = 1;

    [MaxLength(100)]
    public string? LeaseOwner { get; set; }

    public DateTime? LeaseUntil { get; set; }

    public DateTime? LastCheckpointAt { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }

    /// <summary>
    /// PLAN-54: the job's log, one Arabic line per fact a reader of the run must know: the legacy path of a framework older than
    /// PLAN-54, an interim framework and its exception, an expired exception.
    /// </summary>
    [MaxLength(LogMaxLength)]
    public string? Log { get; set; }

    public const int LogMaxLength = 4000;

    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || (Log?.Contains(line, StringComparison.Ordinal) ?? false))
            return;
        var next = string.IsNullOrEmpty(Log) ? line : Log + "\n" + line;
        Log = next.Length <= LogMaxLength ? next : next[^LogMaxLength..];
    }

    public int PartitionSize { get; set; } = 200;

    public bool ResultsValidated { get; set; }

    /// <summary>PERF-001: total employees in snapshot.</summary>
    public int TotalEmployeeCount { get; set; }

    /// <summary>PERF-001: employees processed (lines written or skipped as existing).</summary>
    public int ProcessedEmployeeCount { get; set; }

    public DateTime? CalculateStartedAt { get; set; }

    public DateTime? EstimatedCompletionAt { get; set; }

    public PayrollRun PayrollRun { get; set; } = null!;
    public ICollection<PayrollRunPartition> Partitions { get; set; } = new List<PayrollRunPartition>();
    public ICollection<PayrollRunInputSnapshot> InputSnapshots { get; set; } = new List<PayrollRunInputSnapshot>();
}
