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
    string DeterminismPolicy)
{
    /// <summary>PLAN-54: the pinned framework version's code and number.</summary>
    public string? FrameworkCode { get; init; }

    public int? FrameworkVersionNumber { get; init; }

    /// <summary>1 for a version older than PLAN-54 (it predates legal packs; its run read the general tables), 2 since.</summary>
    public int? FrameworkHashSchemaVersion { get; init; }

    public bool? FrameworkPredatesLegalPacks { get; init; }

    /// <summary>«إطار مؤقت — استثناء EX-…» when the pinned version holds interim values; null otherwise.</summary>
    public string? InterimIndicator { get; init; }

    /// <summary>The STR-002 exceptions the pinned version's interim values cite.</summary>
    public IReadOnlyList<string> InterimExceptionIds { get; init; } = [];

    /// <summary>The job's log: the legacy path, the interim indicator, an expired exception.</summary>
    public string? JobLog { get; init; }
}

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
