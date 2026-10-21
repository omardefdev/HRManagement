using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Tenancy;

public sealed class HrCoreTenantQuarantineQuery(HRCoreDbContext db) : ITenantQuarantineQuery
{
    public Task<bool> IsQuarantinedAsync(
        string serviceName,
        string tableName,
        Guid recordId,
        CancellationToken ct = default) =>
        db.TenantQuarantineEntries.AsNoTracking().AnyAsync(
            e => e.ServiceName == serviceName
                 && e.TableName == tableName
                 && e.RecordId == recordId
                 && e.ResolvedAt == null,
            ct);
}
