namespace HRManagement.MessageContracts.Commands;

/// <summary>PERF-004: enqueue processing of a persisted ExportJob row.</summary>
public interface IExportJobRequested
{
    Guid JobId { get; }
    Guid TenantId { get; }
    string ServiceKey { get; }
    string ExportType { get; }

    int SchemaVersion => 1;
}
