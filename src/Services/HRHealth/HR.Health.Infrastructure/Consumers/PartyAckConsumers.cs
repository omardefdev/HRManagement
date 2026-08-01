using HR.Health.Domain.Entities;

using HR.Health.Domain.Enums;

using HR.Health.Domain.Services;

using HR.Health.Infrastructure.Data;

using HRManagement.MessageContracts.Commands;

using HRManagement.ServiceDefaults;

using MassTransit;

using Microsoft.EntityFrameworkCore;



namespace HR.Health.Infrastructure.Consumers;



/// <summary>FUN-004: terminate insurance coverage during separation saga; restore on compensate.</summary>

public class SeparationPartyAckConsumer(HealthDbContext db)

    : SeparationPartyAckConsumerBase(ProcessParties.Health)

{

    private static string CancelMarker(Guid separationRecordId) =>

        $"separation-saga:{separationRecordId:N}";



    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)

    {

        var employeeId = context.Message.EmployeeId;

        var marker = CancelMarker(context.Message.SeparationRecordId);

        var cards = await db.InsuranceCards

            .Where(c => c.EmployeeId == employeeId

                        && !c.IsCancelled

                        && (c.Status == InsuranceCardStatus.Active || c.Status == InsuranceCardStatus.Suspended))

            .ToListAsync(context.CancellationToken);



        foreach (var card in cards)

        {

            InsuranceLifecycleRules.SoftCancel(card, marker);

        }



        if (cards.Count > 0)

            await db.SaveChangesAsync(context.CancellationToken);



        return cards.Count > 0 ? $"health:insurance-cancelled:{cards.Count}" : "health:ack-noop";

    }



    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context)

    {

        var employeeId = context.Message.EmployeeId;

        var marker = CancelMarker(context.Message.SeparationRecordId);

        var cards = await db.InsuranceCards

            .Where(c => c.EmployeeId == employeeId

                        && c.IsCancelled

                        && c.CancellationReason == marker)

            .ToListAsync(context.CancellationToken);



        foreach (var card in cards)

        {

            card.IsCancelled = false;

            card.CancelledAt = null;

            card.CancellationReason = null;

            card.Status = InsuranceCardStatus.Active;

        }



        if (cards.Count > 0)

            await db.SaveChangesAsync(context.CancellationToken);



        return cards.Count > 0

            ? $"health:compensate-restored:{cards.Count}"

            : "health:compensate-noop";

    }

}



public class HirePartyAckConsumer(HealthDbContext db) : HirePartyAckConsumerBase(ProcessParties.Health)

{

    protected override async Task ExecuteAsync(ConsumeContext<IExecuteHireParty> context)

    {

        var employeeId = context.Message.EmployeeId;

        var exists = await db.InsuranceCards.AsNoTracking()

            .AnyAsync(c => c.EmployeeId == employeeId && !c.IsCancelled, context.CancellationToken);

        if (!exists)

        {

            db.InsuranceCards.Add(new InsuranceCard

            {

                EmployeeId = employeeId,

                MemberNumber = $"HIRE-{employeeId:N}"[..20],

                IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),

                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),

                Status = InsuranceCardStatus.Draft

            });

            await db.SaveChangesAsync(context.CancellationToken);

        }

    }



    protected override async Task CompensateAsync(ConsumeContext<IExecuteHireParty> context)

    {

        var employeeId = context.Message.EmployeeId;

        var drafts = await db.InsuranceCards

            .Where(c => c.EmployeeId == employeeId

                        && !c.IsCancelled

                        && c.Status == InsuranceCardStatus.Draft)

            .ToListAsync(context.CancellationToken);



        foreach (var card in drafts)

            InsuranceLifecycleRules.SoftCancel(card, "hire-saga-compensate");



        if (drafts.Count > 0)

            await db.SaveChangesAsync(context.CancellationToken);

    }

}


