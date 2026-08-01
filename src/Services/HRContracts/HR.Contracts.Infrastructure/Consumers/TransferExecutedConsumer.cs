using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using HR.Contracts.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HR.Contracts.Infrastructure.Consumers;

/// <summary>IAM-002: keep employee org-unit snapshot in sync after transfer.</summary>
public class TransferExecutedConsumer(ContractsDbContext db, ILogger<TransferExecutedConsumer> logger)
    : IConsumer<ITransferExecuted>
{
    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        logger.LogInformation(
            "HR.Contracts: TransferExecuted EmployeeId={EmployeeId} To={ToOrgUnitId}",
            m.EmployeeId, m.ToOrgUnitId);

        var snap = await db.EmployeeOrgUnitSnapshots.FindAsync([m.EmployeeId], context.CancellationToken);
        if (snap is null)
        {
            db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot
            {
                EmployeeId = m.EmployeeId,
                TenantId = m.TenantId ?? Guid.Empty,
                PrimaryOrgUnitId = m.ToOrgUnitId
            });
        }
        else
        {
            snap.PrimaryOrgUnitId = m.ToOrgUnitId;
            if (m.TenantId is { } tenantId)
                snap.TenantId = tenantId;
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
