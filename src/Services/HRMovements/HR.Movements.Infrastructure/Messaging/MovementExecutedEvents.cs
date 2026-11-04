using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;

namespace HR.Movements.Infrastructure.Messaging;

/// <summary>
/// PLAN-40 (class A, D-40-3): the executed-promotion and executed-delegation events. Both stage the message in the bus
/// outbox and its ledger row (the caller's single save writes them with the request's state), with a message id derived
/// from the request, so the execution and a later replay (<c>MessagingRepairController</c>) are one message for the
/// consumers' inboxes.
/// </summary>
public static class MovementExecutedEvents
{
    [StagesOutboxMessages]
    public static async Task PublishPromotionExecutedAsync(
        MovementsDbContext db, IPublishEndpoint publish, PromotionRequest item, Guid? actor,
        string ledgerAction = PublishedEventLedger.Published, CancellationToken ct = default)
    {
        await publish.Publish<IPromotionExecuted>(new
        {
            PromotionRequestId = item.Id,
            item.EmployeeId,
            item.SourceLegalRegimeId,
            item.FromGrade,
            item.ToGrade,
            item.FromStep,
            item.ToStep,
            item.TransitionDecisionRef,
            EffectiveDate = item.EffectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            CorrelationId = item.Id,
            SourceSystem = "HR.Movements"
        }, p => p.MessageId = PromotionMessageId(item.Id), ct);
        db.AuditLogs.Add(PublishedEventLedger.Entry<IPromotionExecuted>(item.Id, actor, ledgerAction));
    }

    [StagesOutboxMessages]
    public static async Task PublishDelegationExecutedAsync(
        MovementsDbContext db, IPublishEndpoint publish, DelegationRequest item, Guid? actor,
        string ledgerAction = PublishedEventLedger.Published, CancellationToken ct = default)
    {
        await publish.Publish<IDelegationExecuted>(new
        {
            DelegationRequestId = item.Id,
            item.EmployeeId,
            item.HostOrg,
            StartDate = item.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndDate = item.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            CorrelationId = item.Id,
            SourceSystem = "HR.Movements"
        }, p => p.MessageId = DelegationMessageId(item.Id), ct);
        db.AuditLogs.Add(PublishedEventLedger.Entry<IDelegationExecuted>(item.Id, actor, ledgerAction));
    }

    public static Guid PromotionMessageId(Guid promotionRequestId) =>
        DeterministicMessageId.For<IPromotionExecuted>(promotionRequestId);

    public static Guid DelegationMessageId(Guid delegationRequestId) =>
        DeterministicMessageId.For<IDelegationExecuted>(delegationRequestId);
}
