using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using MassTransit;

namespace Admin.Logistics.Infrastructure.Consumers;

public class OrgUnitChangedConsumer(AdminLogisticsDbContext db) : IConsumer<IOrgUnitChanged>
{
    public async Task Consume(ConsumeContext<IOrgUnitChanged> context)
    {
        var m = context.Message;
        var snap = await db.OrgUnitSnapshots.FindAsync([m.OrgUnitId], context.CancellationToken);

        if (m.IsDeleted)
        {
            if (snap is not null)
                db.OrgUnitSnapshots.Remove(snap);
        }
        else if (snap is null)
        {
            db.OrgUnitSnapshots.Add(new OrgUnitSnapshot
            {
                Id = m.OrgUnitId,
                TenantId = m.TenantId ?? Guid.Empty,
                ParentId = m.ParentId,
                Code = m.Code,
                NameAr = m.NameAr,
                MaterializedPath = m.MaterializedPath
            });
        }
        else
        {
            snap.TenantId = m.TenantId ?? Guid.Empty;
            snap.ParentId = m.ParentId;
            snap.Code = m.Code;
            snap.NameAr = m.NameAr;
            snap.MaterializedPath = m.MaterializedPath;
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
