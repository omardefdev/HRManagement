using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Integration;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

public class HireCaseCompletedDiwanConsumer(
    HR.Core.Infrastructure.Data.HRCoreDbContext db,
    DiwanIntegrationService diwan,
    ILogger<HireCaseCompletedDiwanConsumer> logger) : IConsumer<IHireCaseCompleted>
{
    public async Task Consume(ConsumeContext<IHireCaseCompleted> context)
    {
        var m = context.Message;
        var reference = $"DIWAN-HIRE-{m.ServiceTransactionId:N}";

        if (await db.DiwanCorrespondences.AnyAsync(
                x => x.ServiceTransactionId == m.ServiceTransactionId,
                context.CancellationToken))
            return;

        await diwan.RegisterCorrespondenceAsync(new DiwanCorrespondence
        {
            ReferenceNumber = reference,
            Subject = $"تسجيل ديوان — تعيين موظف {m.EmployeeId}",
            Direction = CorrespondenceDirection.Outbound,
            RegisteredAt = DateTime.UtcNow,
            RelatedEmployeeId = m.EmployeeId,
            ServiceTransactionId = m.ServiceTransactionId,
            Body = "أُنجزت معاملة التعيين وتم تسجيلها في الديوان."
        }, context.CancellationToken);

        logger.LogInformation(
            "HR.Core: Diwan registered for hire ServiceTransactionId={TransactionId}",
            m.ServiceTransactionId);
    }
}
