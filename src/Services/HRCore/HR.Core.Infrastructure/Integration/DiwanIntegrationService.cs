using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using MassTransit;

namespace HR.Core.Infrastructure.Integration;

public class DiwanIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    public async Task RegisterCorrespondenceAsync(DiwanCorrespondence correspondence, CancellationToken cancellationToken = default)
    {
        // PLAN-52 (D-52-2): every path (the screen, the hire-completed consumer) gives a record its related employee's unit.
        if (correspondence.OrgUnitId is null && correspondence.RelatedEmployeeId is Guid related)
            correspondence.OrgUnitId = await db.Employees.AsNoTracking()
                .Where(e => e.Id == related)
                .Select(e => e.PrimaryOrgUnitId)
                .FirstOrDefaultAsync(cancellationToken);
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
