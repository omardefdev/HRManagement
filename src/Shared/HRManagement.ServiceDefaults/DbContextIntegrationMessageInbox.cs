using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public class DbContextIntegrationMessageInbox(DbContext db) : IIntegrationMessageInbox
{
    public async Task<bool> HasProcessedAsync(string messageId, string consumerType, CancellationToken cancellationToken = default)
    {
        Guid tenantId;
        try
        {
            tenantId = IntegrationMessageInboxExtensions.ResolveInboxTenantId();
        }
        catch (InvalidOperationException)
        {
            return await db.Set<IntegrationMessageReceipt>()
                .AnyAsync(r => r.MessageId == messageId && r.ConsumerType == consumerType, cancellationToken);
        }

        return await db.HasReceiptAsync(messageId, consumerType, tenantId, cancellationToken);
    }

    public async Task TryRecordAsync(string messageId, string consumerType, CancellationToken cancellationToken = default)
    {
        Guid tenantId;
        try
        {
            tenantId = IntegrationMessageInboxExtensions.ResolveInboxTenantId();
        }
        catch (InvalidOperationException)
        {
            tenantId = Guid.Empty;
        }

        db.StageReceipt(messageId, consumerType, tenantId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Duplicate race on redelivery.
        }
    }
}
