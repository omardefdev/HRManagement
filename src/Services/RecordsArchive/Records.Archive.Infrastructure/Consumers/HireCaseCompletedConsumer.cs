using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.CodeGeneration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class HireCaseCompletedConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient,
    ILogger<HireCaseCompletedConsumer> logger) : IConsumer<IHireCaseCompleted>
{
    public async Task Consume(ConsumeContext<IHireCaseCompleted> context)
    {
        var m = context.Message;
        var title = $"ملف تعيين الموظف {m.EmployeeId} [{m.ServiceTransactionId:N}]";

        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync<IHireCaseCompleted>(codeClient, "HIRE", context.CancellationToken);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(
            title, reference, "HR.Contracts/Appointments"));

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation(
            "Records.Archive: HireCaseCompleted EmployeeId={EmployeeId}, TransactionId={TransactionId}, Ref={Reference}",
            m.EmployeeId, m.ServiceTransactionId, reference);
    }
}
