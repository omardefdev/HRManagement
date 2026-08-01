namespace Payroll.Application.Options;

/// <summary>PERF-001: bounded parallel payroll batch knobs.</summary>
public sealed class PayrollBatchOptions
{
    public const string SectionName = "Payroll";

    /// <summary>Max concurrent partition workers (default 4).</summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>Channel capacity for pending partition ids (backpressure).</summary>
    public int PartitionQueueCapacity { get; set; } = 8;

    /// <summary>Employees written per DB transaction inside a partition.</summary>
    public int BulkWriteSize { get; set; } = 50;

    /// <summary>Default employees per partition when not specified on start.</summary>
    public int DefaultPartitionSize { get; set; } = 200;
}
