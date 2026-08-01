using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;

namespace HR.Core.Infrastructure.Integration;

public class DiwanIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    public async Task RegisterCorrespondenceAsync(DiwanCorrespondence correspondence, CancellationToken cancellationToken = default)
    {
        db.DiwanCorrespondences.Add(correspondence);

        if (correspondence.ServiceTransactionId.HasValue)
        {
            await publish.Publish<ITransactionRegisteredInDiwan>(new
            {
                TransactionId = correspondence.ServiceTransactionId.Value,
                DiwanNumber = correspondence.ReferenceNumber,
                RegisteredAt = correspondence.RegisteredAt,
                SourceSystem = "HR.Core",
                CorrelationId = CorrelationIdFlow.Get()
            }, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
