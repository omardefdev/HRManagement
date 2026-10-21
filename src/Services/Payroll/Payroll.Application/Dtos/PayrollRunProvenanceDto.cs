namespace Payroll.Application.Dtos;

/// <summary>AC-21: auditor-facing provenance for a payroll run (PAY-002 + PAY-003).</summary>
public record PayrollRunProvenanceDto(
    Guid PayrollRunId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string? ConfigContentHash,
    Guid? PayrollConfigurationId,
    DateTime? ConfigEffectiveFrom,
    Guid? JobId,
    int SnapshotEmployeeCount,
    int PartitionCount,
    DateTime? LastCheckpointAt,
    string? AggregateInputFingerprint,
    string DeterminismPolicy);

public record PayrollRunInputSnapshotItemDto(
    Guid EmployeeId,
    string ProfileVersion,
    string? InputFingerprint,
    string? ConfigContentHash,
    int SortOrder);

public record PayrollRunInputSnapshotsPageDto(
    Guid JobId,
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<PayrollRunInputSnapshotItemDto> Items);
