using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Consumers;

public class EmployeeCreatedConsumer(AdminLogisticsDbContext db, ILogger<EmployeeCreatedConsumer> logger)
    : IConsumer<IEmployeeCreated>
{
    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "Admin.Logistics: EmployeeCreated EmployeeId={EmployeeId}",
            m.EmployeeId);

        var snap = await db.EmployeeOrgUnitSnapshots.FindAsync([m.EmployeeId], context.CancellationToken);
        if (snap is null)
        {
            db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot
            {
                EmployeeId = m.EmployeeId,
                TenantId = m.TenantId ?? Guid.Empty,
                PrimaryOrgUnitId = m.PrimaryOrgUnitId
            });
        }
        else
        {
            snap.TenantId = m.TenantId ?? Guid.Empty;
            snap.PrimaryOrgUnitId = m.PrimaryOrgUnitId;
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
