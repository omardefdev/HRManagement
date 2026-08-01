using HR.Movements.Domain.Enums;

using HR.Movements.Domain.Services;

using HR.Movements.Infrastructure.Data;

using HRManagement.MessageContracts.Commands;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using HRManagement.SharedKernel.Entities;

using HRManagement.SharedKernel.Enums;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;



namespace HR.Movements.Infrastructure.Consumers;



/// <summary>FUN-003: finalize transfer only after saga publishes ITransferExecuted.</summary>

public class TransferExecutedConsumer(MovementsDbContext db, ILogger<TransferExecutedConsumer> logger)

    : IConsumer<ITransferExecuted>

{

    public async Task Consume(ConsumeContext<ITransferExecuted> context)

    {

        var message = context.Message;

        var transfer = await db.TransferRequests.FirstOrDefaultAsync(

            t => t.Id == message.TransferRequestId, context.CancellationToken);

        if (transfer is null || transfer.IsCancelled)

            return;



        if (transfer.Status == ApprovalStatus.Approved)

            transfer.Status = ApprovalStatus.Executed;



        var executedStep = await db.MovementChecklists.FirstOrDefaultAsync(

            c => c.CorrelationId == message.TransferRequestId

                 && c.MovementKind == MovementKind.Transfer

                 && c.Step == MovementChecklistStep.Executed,

            context.CancellationToken);

        if (executedStep is not null && !executedStep.IsCompleted)

            MovementWorkflowRules.MarkStep(executedStep, null, "transfer-saga:executed");



        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(

            "Transfer {Id} marked executed after saga finalized", message.TransferRequestId);

    }

}



/// <summary>FUN-003: Movements party — records checklist evidence; compensates without leaving Executed set.</summary>

public class TransferPartyAckConsumer(MovementsDbContext db)

    : TransferPartyAckConsumerBase(ProcessParties.Movements)

{

    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteTransferParty> context)

    {

        var transferId = context.Message.TransferRequestId;

        if (!await db.MovementChecklists.AnyAsync(

                c => c.CorrelationId == transferId && c.MovementKind == MovementKind.Transfer,

                context.CancellationToken))

        {

            foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Transfer, transferId))

                db.MovementChecklists.Add(step);

        }



        var recorded = await db.MovementChecklists.FirstOrDefaultAsync(

            c => c.CorrelationId == transferId

                 && c.MovementKind == MovementKind.Transfer

                 && c.Step == MovementChecklistStep.RequestRecorded,

            context.CancellationToken);

        if (recorded is not null && !recorded.IsCompleted)

            MovementWorkflowRules.MarkStep(recorded, null, $"movements:transfer-party:{transferId:N}");



        await db.SaveChangesAsync(context.CancellationToken);

        return $"movements:transfer-party:{transferId:N}";

    }



    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteTransferParty> context)

    {

        var transferId = context.Message.TransferRequestId;

        var evidence = $"movements:transfer-party:{transferId:N}";

        var steps = await db.MovementChecklists

            .Where(c => c.CorrelationId == transferId

                        && c.MovementKind == MovementKind.Transfer

                        && c.EvidenceRef == evidence)

            .ToListAsync(context.CancellationToken);



        foreach (var step in steps)

        {

            step.IsCompleted = false;

            step.CompletedAt = null;

            step.CompletedBy = null;

            step.CompletedByName = null;

            step.EvidenceRef = null;

        }



        var transfer = await db.TransferRequests.FindAsync([transferId], context.CancellationToken);

        if (transfer is not null

            && transfer.Status == ApprovalStatus.Executed

            && transfer.EffectiveDate > DateOnly.FromDateTime(DateTime.UtcNow))

        {

            transfer.Status = ApprovalStatus.Approved;

        }



        if (steps.Count > 0 || transfer?.Status == ApprovalStatus.Approved)

            await db.SaveChangesAsync(context.CancellationToken);



        return steps.Count > 0

            ? $"movements:compensate-cleared:{steps.Count}"

            : "movements:compensate-noop";

    }

}



public class SecondmentPartyAckConsumer(MovementsDbContext db)

    : SecondmentPartyAckConsumerBase(ProcessParties.Movements)

{

    protected override async Task<string?> ExecuteAsync(ConsumeContext<IExecuteSecondmentParty> context)

    {

        var id = context.Message.SecondmentRequestId;

        if (!await db.MovementChecklists.AnyAsync(

                c => c.CorrelationId == id && c.MovementKind == MovementKind.Secondment,

                context.CancellationToken))

        {

            foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Secondment, id))

                db.MovementChecklists.Add(step);

        }



        var recorded = await db.MovementChecklists.FirstOrDefaultAsync(

            c => c.CorrelationId == id

                 && c.MovementKind == MovementKind.Secondment

                 && c.Step == MovementChecklistStep.RequestRecorded,

            context.CancellationToken);

        if (recorded is not null && !recorded.IsCompleted)

            MovementWorkflowRules.MarkStep(recorded, null, $"movements:secondment-party:{id:N}");



        await db.SaveChangesAsync(context.CancellationToken);

        return $"movements:secondment-party:{id:N}";

    }



    protected override async Task<string?> CompensateAsync(ConsumeContext<IExecuteSecondmentParty> context)

    {

        var id = context.Message.SecondmentRequestId;

        var evidence = $"movements:secondment-party:{id:N}";

        var steps = await db.MovementChecklists

            .Where(c => c.CorrelationId == id

                        && c.MovementKind == MovementKind.Secondment

                        && c.EvidenceRef == evidence)

            .ToListAsync(context.CancellationToken);



        foreach (var step in steps)

        {

            step.IsCompleted = false;

            step.CompletedAt = null;

            step.CompletedBy = null;

            step.CompletedByName = null;

            step.EvidenceRef = null;

        }



        if (steps.Count > 0)

            await db.SaveChangesAsync(context.CancellationToken);



        return steps.Count > 0

            ? $"movements:compensate-cleared:{steps.Count}"

            : "movements:compensate-noop";

    }

}



/// <summary>INT-003: human intervention queue (Transfer / Secondment).</summary>

public class ProcessInterventionRequestedConsumer(

    MovementsDbContext db,

    ILogger<ProcessInterventionRequestedConsumer> logger) : IConsumer<IProcessInterventionRequested>

{

    public async Task Consume(ConsumeContext<IProcessInterventionRequested> context)

    {

        var m = context.Message;

        if (m.SagaType is not (SagaTypeNames.Transfer or SagaTypeNames.Secondment))

            return;



        var exists = await db.ProcessInterventions.AnyAsync(

            i => i.SagaType == m.SagaType

                 && i.CorrelationId == m.CorrelationId

                 && i.Status == ProcessInterventionStatuses.Open,

            context.CancellationToken);

        if (exists) return;



        db.ProcessInterventions.Add(new ProcessIntervention

        {

            SagaType = m.SagaType,

            CorrelationId = m.CorrelationId,

            Reason = m.Reason,

            FailedParty = m.FailedParty,

            Status = ProcessInterventionStatuses.Open,

            TenantId = m.TenantId ?? Guid.Empty

        });

        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogWarning("Movements intervention {Saga} {Id}: {Reason}", m.SagaType, m.CorrelationId, m.Reason);

    }

}


