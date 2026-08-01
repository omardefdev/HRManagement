namespace HRManagement.SharedKernel.Tenancy;

/// <summary>Before/after reconciliation summary for a single table (TEN-003).</summary>
public record TenantReconciliationReport(
    string ServiceName,
    string TableName,
    int TotalRows,
    int AssignedRows,
    int QuarantinedRows,
    int OrphanRows,
    DateTime GeneratedAt);
