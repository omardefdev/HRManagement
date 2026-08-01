using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class LeaveApprovedConsumer(ArchiveDbContext db) : IConsumer<ILeaveApproved>
{
    public async Task Consume(ConsumeContext<ILeaveApproved> context)
    {
        var m = context.Message;
        var reference = $"LEAVE-{m.LeaveRequestId}";
        if (await db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference, context.CancellationToken))
        {
            return;
        }

        db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"قرار اعتماد إجازة للموظف {m.EmployeeId}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "HRContracts/LeaveApprovals",
            RetentionPeriodYears = 10
        });

        await db.SaveChangesAsync(context.CancellationToken);

        await context.Publish<ITransactionArchived>(new
        {
            m.TransactionId,
            m.EmployeeId,
            ArchiveRef = reference,
            ArchivedAt = DateTime.UtcNow,
            SourceSystem = "Records.Archive",
            CorrelationId = m.CorrelationId
        });
    }
}

public class AdvanceApprovedConsumer(ArchiveDbContext db) : IConsumer<IAdvanceApproved>
{
    public async Task Consume(ConsumeContext<IAdvanceApproved> context)
    {
        var m = context.Message;
        var reference = $"ADV-{m.AdvanceRequestId}";
        if (await db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference, context.CancellationToken))
        {
            return;
        }

        db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"قرار اعتماد سلفة للموظف {m.EmployeeId}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "HRContracts/AdvanceApprovals",
            RetentionPeriodYears = 10
        });

        await db.SaveChangesAsync(context.CancellationToken);

        await context.Publish<ITransactionArchived>(new
        {
            m.TransactionId,
            m.EmployeeId,
            ArchiveRef = reference,
            ArchivedAt = DateTime.UtcNow,
            SourceSystem = "Records.Archive",
            CorrelationId = m.CorrelationId
        });
    }
}
