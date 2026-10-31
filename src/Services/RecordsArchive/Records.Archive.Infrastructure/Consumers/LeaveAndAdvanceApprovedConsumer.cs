using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.CodeGeneration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class LeaveApprovedConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient) : IConsumer<ILeaveApproved>
{
    public async Task Consume(ConsumeContext<ILeaveApproved> context)
    {
        var m = context.Message;
        var title = $"قرار اعتماد إجازة للموظف {m.EmployeeId} [{m.LeaveRequestId:N}]";
        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync<ILeaveApproved>(codeClient, "LEAVE", context.CancellationToken);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(
            title, reference, "HRContracts/LeaveApprovals"));

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

public class AdvanceApprovedConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient) : IConsumer<IAdvanceApproved>
{
    public async Task Consume(ConsumeContext<IAdvanceApproved> context)
    {
        var m = context.Message;
        var title = $"قرار اعتماد سلفة للموظف {m.EmployeeId} [{m.AdvanceRequestId:N}]";
        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync<IAdvanceApproved>(codeClient, "ADV", context.CancellationToken);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(
            title, reference, "HRContracts/AdvanceApprovals"));

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
