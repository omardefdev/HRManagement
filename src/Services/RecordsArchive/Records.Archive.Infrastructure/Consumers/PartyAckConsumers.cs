using HRManagement.MessageContracts.Commands;

using HRManagement.ServiceDefaults;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Records.Archive.Domain.Enums;

using Records.Archive.Domain.Services;

using Records.Archive.Infrastructure.Data;



namespace Records.Archive.Infrastructure.Consumers;



/// <summary>FUN-002 / FUN-006: stage archive checklist during saga — final ArchiveItem only on ISeparationCompleted.</summary>

public class SeparationPartyAckConsumer(ArchiveDbContext db)

    : SeparationPartyAckConsumerBase(ProcessParties.Archive)

{

    private static string PartyEvidence(Guid separationRecordId) =>

        $"archive:separation-party:{separationRecordId:N}";



    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context)

    {

        var message = context.Message;

        var separationId = message.SeparationRecordId;



        var prematureArchiveRef = $"SEP-{message.EmployeeId:N}";

        if (await db.ArchiveItems.AnyAsync(

                x => x.ReferenceNumber == prematureArchiveRef, context.CancellationToken))

        {

            throw new InvalidOperationException(

                "لا يُنشأ عنصر أرشيف قبل اكتمال رحلة الفصل — انتظر ISeparationCompleted.");

        }



        if (!await db.ArchiveChecklists.AnyAsync(c => c.CorrelationId == separationId, context.CancellationToken))

        {

            var tenantId = db.CurrentTenantId
                ?? throw new InvalidOperationException("Tenant context is required for archive checklist staging.");

            foreach (var step in ArchiveChecklistDefaults.CreateFor(separationId))

            {

                step.TenantId = tenantId;

                db.ArchiveChecklists.Add(step);

            }

        }



        var ingested = await db.ArchiveChecklists.FirstOrDefaultAsync(

            c => c.CorrelationId == separationId && c.Step == ArchiveChecklistStep.Ingested,

            context.CancellationToken);

        if (ingested is not null && !ingested.IsCompleted)

            ArchiveChecklistDefaults.Mark(ingested, null, PartyEvidence(separationId));



        await db.SaveChangesAsync(context.CancellationToken);

        return PartyEvidence(separationId);

    }



    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context)

    {

        var separationId = context.Message.SeparationRecordId;

        var evidence = PartyEvidence(separationId);

        var staged = await db.ArchiveChecklists

            .Where(c => c.CorrelationId == separationId && c.EvidenceRef == evidence)

            .ToListAsync(context.CancellationToken);



        foreach (var step in staged)

        {

            step.IsCompleted = false;

            step.CompletedAt = null;

            step.CompletedBy = null;

            step.EvidenceRef = null;

        }



        if (staged.Count > 0)

            await db.SaveChangesAsync(context.CancellationToken);



        return staged.Count > 0

            ? $"archive:compensate-cleared:{staged.Count}"

            : "archive:compensate-noop";

    }

}



public class HirePartyAckConsumer(ArchiveDbContext db) : HirePartyAckConsumerBase(ProcessParties.Archive)

{

    protected override async Task ExecuteAsync(ConsumeContext<IExecuteHireParty> context)

    {

        var txnId = context.Message.ServiceTransactionId;

        if (!await db.ArchiveChecklists.AnyAsync(c => c.CorrelationId == txnId, context.CancellationToken))

        {

            foreach (var step in ArchiveChecklistDefaults.CreateFor(txnId))

                db.ArchiveChecklists.Add(step);

        }



        var classified = await db.ArchiveChecklists.FirstOrDefaultAsync(

            c => c.CorrelationId == txnId && c.Step == ArchiveChecklistStep.Classified,

            context.CancellationToken);

        if (classified is not null && !classified.IsCompleted)

            ArchiveChecklistDefaults.Mark(classified, null, $"archive:hire-party:{txnId:N}");



        await db.SaveChangesAsync(context.CancellationToken);

    }



    protected override async Task CompensateAsync(ConsumeContext<IExecuteHireParty> context)

    {

        var txnId = context.Message.ServiceTransactionId;

        var evidence = $"archive:hire-party:{txnId:N}";

        var staged = await db.ArchiveChecklists

            .Where(c => c.CorrelationId == txnId && c.EvidenceRef == evidence)

            .ToListAsync(context.CancellationToken);



        foreach (var step in staged)

        {

            step.IsCompleted = false;

            step.CompletedAt = null;

            step.CompletedBy = null;

            step.EvidenceRef = null;

        }



        if (staged.Count > 0)

            await db.SaveChangesAsync(context.CancellationToken);

    }

}


