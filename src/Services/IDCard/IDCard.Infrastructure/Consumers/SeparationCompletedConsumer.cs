using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using IDCard.Domain.Enums;
using IDCard.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IDCard.Infrastructure.Consumers;

public class SeparationCompletedConsumer(IDCardDbContext db, ILogger<SeparationCompletedConsumer> logger)
    : IConsumer<ISeparationCompleted>
{
    public async Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, logger, nameof(ISeparationCompleted)))
            return;

        logger.LogInformation(
            "IDCard: SeparationCompleted EmployeeId={EmployeeId} Type={SeparationType}",
            m.EmployeeId, m.SeparationType);

        var cards = await db.IssuedCards
            .Where(c => c.EmployeeId == m.EmployeeId
                        && (c.Status == CardStatus.Active || c.Status == CardStatus.Lost))
            .ToListAsync(context.CancellationToken);

        if (cards.Count == 0)
            return;

        foreach (var card in cards)
        {
            card.Status = CardStatus.Revoked;
            card.RevokedAt ??= DateTime.UtcNow;
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
