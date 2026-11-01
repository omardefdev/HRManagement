using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.CodeGeneration;
using MassTransit;
using Microsoft.Extensions.Logging;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class SeparationCompletedConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient,
    ILogger<SeparationCompletedConsumer> logger) : IConsumer<ISeparationCompleted>
{
    public async Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        var m = context.Message;
        var title = $"ملف فصل الموظف {m.EmployeeId} — {m.SeparationType} [{m.EmployeeId:N}]";

        logger.LogInformation(
            "Records.Archive: SeparationCompleted EmployeeId={EmployeeId}, Type={SeparationType}",
            m.EmployeeId, m.SeparationType);

        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync(codeClient, "SEP", nameof(ISeparationCompleted), context.CancellationToken);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(title, reference, "HRContracts/Separations"));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
