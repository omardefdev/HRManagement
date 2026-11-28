using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Infrastructure.Consumers;

/// <summary>
/// PLAN-60 (§5): a separation whose saga stopped on a party (a fault or a timeout) waits for a person: <c>Executing</c> →
/// <c>AwaitingIntervention</c>. The actor is the system, named in the reason; <c>ProcessInterventionsController.Resume</c> takes it back.
/// </summary>
public sealed class SeparationInterventionStatusConsumer(ContractsDbContext db, IContractsRepository repository)
    : IConsumer<IProcessInterventionRequested>
{
    public async Task Consume(ConsumeContext<IProcessInterventionRequested> context)
    {
        var m = context.Message;
        if (m.SagaType != SagaTypeNames.Separation)
            return;

        var record = await db.SeparationRecords.FirstOrDefaultAsync(r => r.Id == m.CorrelationId, context.CancellationToken);
        if (record is not { Status: SeparationStatus.Executing })
            return;

        var reason = $"تعثّر طرف ({m.FailedParty ?? "غير معروف"}): {m.Reason} — رحلة الفصل (النظام)";
        await SeparationStatusSync.TransitionAsync(
            repository, record, SeparationStatus.AwaitingIntervention, null, reason.Length <= 900 ? reason : reason[..900], context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
