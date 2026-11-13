using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HR.Health.Infrastructure.Consumers;

/// <summary>
/// PLAN-52 (§5, P-10): HR.Health's employee snapshot follows a transfer, as HR.Contracts' does, so a branch scope over HR.Health's records
/// sees the employee where he now works. Idempotent on <c>TransferRequestId</c> (or the message id when it is null), with a receipt in the
/// same save as the change (PLAN-40's inbox table): a redelivery of a transfer already applied never moves the snapshot back. Messages are
/// not ordered: a first delivery of an older transfer that arrives after a newer one is applied, as HR.Contracts' and Admin.Logistics'
/// consumers do (review 5, recorded in the PLAN-52 report). Registered by the assembly scan of the consumers.
/// </summary>
public class TransferExecutedConsumer(HealthDbContext db, ILogger<TransferExecutedConsumer> logger) : IConsumer<ITransferExecuted>
{
    public const string ReceiptConsumer = "hr-health.transfer-executed.snapshot";

    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;
        logger.LogInformation(
            "HR.Health: TransferExecuted EmployeeId={EmployeeId} To={ToOrgUnitId}",
            m.EmployeeId, m.ToOrgUnitId);

        var key = m.TransferRequestId is Guid transfer ? $"transfer:{transfer:D}" : context.MessageId?.ToString("D");
        var tenant = m.TenantId ?? TenantIdFlow.Get() ?? Guid.Empty;
        if (key is not null && await db.HasReceiptAsync(key, ReceiptConsumer, tenant, ct))
        {
            logger.LogInformation("HR.Health: transfer already applied, skipped (redelivery)");
            return;
        }

        var snapshot = await db.EmployeeOrgUnitSnapshots.FindAsync([m.EmployeeId], ct);
        if (snapshot is null)
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
            snapshot.PrimaryOrgUnitId = m.ToOrgUnitId;
            if (m.TenantId is { } tenantId)
                snapshot.TenantId = tenantId;
        }

        if (key is not null)
            db.StageReceipt(key, ReceiptConsumer, tenant);
        await db.SaveChangesAsync(ct);
    }
}
