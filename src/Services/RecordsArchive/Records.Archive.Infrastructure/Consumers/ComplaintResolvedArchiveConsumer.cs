using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.CodeGeneration;
using MassTransit;
using Microsoft.Extensions.Logging;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class ComplaintResolvedArchiveConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient,
    ILogger<ComplaintResolvedArchiveConsumer> logger) : IConsumer<IComplaintResolved>
{
    public async Task Consume(ConsumeContext<IComplaintResolved> context)
    {
        var m = context.Message;
        var title = $"ملف شكوى — {m.Subject}: {m.Resolution} [{m.ComplaintId:N}]";
        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync<IComplaintResolved>(codeClient, "CMP", context.CancellationToken);

        logger.LogInformation("Records.Archive: ComplaintResolved ComplaintId={ComplaintId} Ref={Reference}", m.ComplaintId, reference);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(title, reference, "Compliance/Complaints"));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
