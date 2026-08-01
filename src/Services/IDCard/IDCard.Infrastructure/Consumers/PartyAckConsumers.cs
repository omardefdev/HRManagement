using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace IDCard.Infrastructure.Consumers;

public class SeparationPartyAckConsumer(IDCardDbContext db)
    : SeparationPartyAckConsumerBase(ProcessParties.IdCard)
{
    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var employeeId = context.Message.EmployeeId;
        var cards = await db.IssuedCards
            .Where(c => c.EmployeeId == employeeId && (c.Status == CardStatus.Active || c.Status == CardStatus.Lost))
            .ToListAsync(context.CancellationToken);

        foreach (var card in cards)
        {
            card.Status = CardStatus.Revoked;
            card.RevokedAt ??= DateTime.UtcNow;
            card.UrgentRevoke = true;
        }

        if (cards.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);

        return cards.Count > 0 ? $"idcard:revoked:{cards.Count}" : "idcard:ack-noop";
    }

    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context)
    {
        var employeeId = context.Message.EmployeeId;
        var cards = await db.IssuedCards
            .Where(c => c.EmployeeId == employeeId
                        && c.Status == CardStatus.Revoked
                        && c.UrgentRevoke)
            .ToListAsync(context.CancellationToken);

        foreach (var card in cards)
        {
            card.Status = CardStatus.Active;
            card.RevokedAt = null;
            card.UrgentRevoke = false;
        }

        if (cards.Count > 0)
            await db.SaveChangesAsync(context.CancellationToken);

        return cards.Count > 0 ? $"idcard:compensate-restored:{cards.Count}" : "idcard:compensate-noop";
    }
}

public class HirePartyAckConsumer() : HirePartyAckConsumerBase(ProcessParties.IdCard);
