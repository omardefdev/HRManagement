using System.Diagnostics.Metrics;

namespace Payroll.Application.Services;

/// <summary>PERF-001: payroll run batch metrics (hrm.payroll.run).</summary>
public static class PayrollRunMetrics
{
    public static readonly Meter Meter = new("hrm.payroll.run", "1.0.0");

    public static readonly Counter<long> EmployeesProcessed =
        Meter.CreateCounter<long>("hrm.payroll.run.employees_processed", unit: "{employee}");

    public static readonly Counter<long> EmployeesFailed =
        Meter.CreateCounter<long>("hrm.payroll.run.employees_failed", unit: "{employee}");

    public static readonly Counter<long> WriteBatches =
        Meter.CreateCounter<long>("hrm.payroll.run.write_batches", unit: "{batch}");

    public static readonly Histogram<double> PartitionDurationMs =
        Meter.CreateHistogram<double>("hrm.payroll.run.partition_duration", unit: "ms");

    public static readonly UpDownCounter<int> ActivePartitions =
        Meter.CreateUpDownCounter<int>("hrm.payroll.run.active_partitions", unit: "{partition}");
}
