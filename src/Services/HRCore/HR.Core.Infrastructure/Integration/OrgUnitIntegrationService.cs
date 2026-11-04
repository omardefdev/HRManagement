using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Integration;

public class OrgUnitIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    /// <summary>Stages <c>IOrgUnitChanged</c> in the bus outbox; the caller's save writes it with the unit (PLAN-40).</summary>
    [StagesOutboxMessages]
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

    [StagesOutboxMessages]
    public async Task PublishManyOrgUnitsChangedAsync(IEnumerable<OrgUnit> units, CancellationToken ct = default)
    {
        foreach (var unit in units)
            await PublishOrgUnitChangedAsync(unit, false, ct);
    }

    /// <summary>
    /// Full re-sync (HR.Core start-up, agency onboarding, <c>resync/org-units</c>): one <c>IOrgUnitChanged</c> per unit,
    /// written by its own save. PLAN-40 (class A): before, nothing saved after the publish at start-up, so every message
    /// was discarded with the scope. Consumers upsert by id, so a repeated re-sync changes nothing.
    /// </summary>
    /// <returns>The number of units published.</returns>
    public async Task<int> PublishAllOrgUnitsAsync(CancellationToken ct = default)
    {
        var units = await db.OrgUnits.AsNoTracking().ToListAsync(ct);
        foreach (var unit in units)
            await PublishOrgUnitChangedAsync(unit, false, ct);
        await db.SaveChangesAsync(ct);
        return units.Count;
    }

    /// <summary>
    /// The re-sync of HR.Core start-up, which has no tenant of its own: every tenant's units, each message under its
    /// tenant (the consumers' inboxes need the header), in one save. PLAN-40 (AC-MSG-40h).
    /// </summary>
    /// <returns>The number of units published.</returns>
    public async Task<int> PublishAllTenantsOrgUnitsAsync(CancellationToken ct = default)
    {
        var units = await db.OrgUnits
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.OrganizationMaintenance)
            .AsNoTracking()
            .ToListAsync(ct);

        var scopeTenant = TenantIdFlow.Get();
        try
        {
            foreach (var unit in units)
            {
                TenantIdFlow.Set(unit.TenantId);
                await PublishOrgUnitChangedAsync(unit, false, ct);
            }
        }
        finally
        {
            TenantIdFlow.Set(scopeTenant);
        }

        await db.SaveChangesAsync(ct);
        return units.Count;
    }
}
