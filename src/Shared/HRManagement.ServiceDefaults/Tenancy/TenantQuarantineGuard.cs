namespace HRManagement.ServiceDefaults.Tenancy;

public interface ITenantQuarantineQuery
{
    Task<bool> IsQuarantinedAsync(
        string serviceName,
        string tableName,
        Guid recordId,
        CancellationToken ct = default);
}

/// <summary>TEN-003 / AC-08: block operational mutations on quarantined rows.</summary>
public sealed class TenantQuarantineGuard(ITenantQuarantineQuery query)
{
    public const string ServiceName = "HRCore";

    public async Task ThrowIfQuarantinedAsync(
        string tableName,
        Guid recordId,
        CancellationToken ct = default) =>
        await ThrowIfQuarantinedAsync(ServiceName, tableName, recordId, ct);

    public async Task ThrowIfQuarantinedAsync(
        string serviceName,
        string tableName,
        Guid recordId,
        CancellationToken ct = default)
    {
        if (await query.IsQuarantinedAsync(serviceName, tableName, recordId, ct))
            throw new QuarantinedRecordException(serviceName, tableName, recordId);
    }
}

public sealed class QuarantinedRecordException : Exception
{
    public const string ErrorCode = "QuarantinedRecord";

    public QuarantinedRecordException(string serviceName, string tableName, Guid recordId)
        : base($"السجل محجور pending resolution ({serviceName}.{tableName}:{recordId:D}).")
    {
        ServiceName = serviceName;
        TableName = tableName;
        RecordId = recordId;
    }

    public string ServiceName { get; }
    public string TableName { get; }
    public Guid RecordId { get; }
}
