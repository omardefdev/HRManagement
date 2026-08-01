using HR.Health.Domain.Entities;
using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Health.Infrastructure.Consumers;

// INT-002: inbox handled by InboxConsumeFilter
public class EmployeeCreatedConsumer(HealthDbContext db, ILogger<EmployeeCreatedConsumer> logger)
    : IConsumer<IEmployeeCreated>
{
    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "HR.Health creating MedicalRecord for new employee: EmployeeId={EmployeeId}, FullName={FullName}",
            m.EmployeeId, m.FullName);

        if (!await db.MedicalRecords.AnyAsync(r => r.EmployeeId == m.EmployeeId, context.CancellationToken))
        {
            db.MedicalRecords.Add(new MedicalRecord
            {
                Id = Guid.NewGuid(),
                EmployeeId = m.EmployeeId,
                TenantId = m.TenantId ?? Guid.Empty
            });
        }

        var employeeSnap = await db.EmployeeOrgUnitSnapshots
            .FindAsync([m.EmployeeId], context.CancellationToken);
        if (employeeSnap is null)
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
            employeeSnap.PrimaryOrgUnitId = m.PrimaryOrgUnitId;
            employeeSnap.TenantId = m.TenantId ?? Guid.Empty;
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
