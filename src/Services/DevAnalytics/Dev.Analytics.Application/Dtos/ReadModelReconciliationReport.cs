namespace Dev.Analytics.Application.Dtos;

public sealed record ReadModelReconciliationReport(
    Guid ReadModelId,
    bool DriftDetected,
    bool DryRun,
    bool Corrected,
    string BeforeFingerprint,
    string AfterFingerprint,
    string? BeforeSchemaHash,
    string? AfterSchemaHash,
    IReadOnlyList<string> Changes,
    DateTime RanAt);
