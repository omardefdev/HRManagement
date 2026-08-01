using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Integration;

public class OrgUnitIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    public async Task PublishOrgUnitChangedAsync(OrgUnit unit, bool isDeleted = false, CancellationToken ct = default)
    {
        await publish.Publish<IOrgUnitChanged>(new
        {
            OrgUnitId = unit.Id,
            TenantId = unit.TenantId,
            unit.ParentId,
            unit.Code,
            unit.NameAr,
            unit.MaterializedPath,
            IsDeleted = isDeleted
        }, ct);
    }

    public async Task PublishManyOrgUnitsChangedAsync(IEnumerable<OrgUnit> units, CancellationToken ct = default)
    {
        foreach (var unit in units)
            await PublishOrgUnitChangedAsync(unit, false, ct);
    }

    public async Task PublishAllOrgUnitsAsync(CancellationToken ct = default)
    {
        var units = await db.OrgUnits.AsNoTracking().ToListAsync(ct);
        foreach (var unit in units)
            await PublishOrgUnitChangedAsync(unit, false, ct);
    }
}
