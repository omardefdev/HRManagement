using System.Text.Json;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Infrastructure.Sagas;

public class SeparationSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid TenantId { get; set; }
    public Guid SeparationRecordId { get; set; }
    public Guid EmployeeId { get; set; }
    public string SeparationType { get; set; } = "";
    public DateTime EffectiveDate { get; set; }
    public Guid? ContractId { get; set; }
    public int RemainingLeaveDays { get; set; }
    public string? BusinessCorrelationId { get; set; }
    public string PendingParties { get; set; } = "";
    public string CompletedParties { get; set; } = "";
    public string? LastFaultParty { get; set; }
    public string? LastFaultReason { get; set; }
    public Guid? TimeoutTokenId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class SeparationSagaOptions
{
    public const string SectionName = "Sagas:Separation";
    public string[] MandatoryParties { get; set; } =
    [
        ProcessParties.Payroll,
        ProcessParties.HrCore,
        ProcessParties.IdCard,
        ProcessParties.AuthDirectory,
        ProcessParties.AdminLogistics,
        ProcessParties.ItInfra,
        ProcessParties.Archive
    ];
    public int PartyTimeoutMinutes { get; set; } = 30;
}

public class SeparationStateMachine : MassTransitStateMachine<SeparationSagaState>
{
    public SeparationStateMachine(
        IOptions<SeparationSagaOptions> options,
        ILogger<SeparationStateMachine> logger,
        SagaMetrics metrics)
    {
        var opts = options.Value;
        var timeout = TimeSpan.FromMinutes(Math.Max(1, opts.PartyTimeoutMinutes));

        InstanceState(x => x.CurrentState);

        Event(() => Started, e =>
        {
            e.CorrelateById(ctx => ctx.Message.SeparationRecordId);
            e.SelectId(ctx => ctx.Message.SeparationRecordId);
        });
        Event(() => PartyCompleted, e => e.CorrelateById(ctx => ctx.Message.SeparationRecordId));
        Event(() => PartyFaulted, e => e.CorrelateById(ctx => ctx.Message.SeparationRecordId));
        Event(() => Resume, e => e.CorrelateById(ctx => ctx.Message.SeparationRecordId));
        Event(() => Cancel, e => e.CorrelateById(ctx => ctx.Message.SeparationRecordId));

        Schedule(() => TimeoutSchedule, instance => instance.TimeoutTokenId, s =>
        {
            s.Delay = timeout;
            s.Received = r =>
            {
                r.CorrelateById(ctx => ctx.Message.SeparationRecordId);
                r.ConfigureConsumeTopology = false;
            };
        });

        Initially(
            When(Started)
                .Then(ctx =>
                {
                    ctx.Saga.SeparationRecordId = ctx.Message.SeparationRecordId;
                    ctx.Saga.EmployeeId = ctx.Message.EmployeeId;
                    ctx.Saga.SeparationType = ctx.Message.SeparationType;
                    ctx.Saga.EffectiveDate = ctx.Message.EffectiveDate;
                    ctx.Saga.ContractId = ctx.Message.ContractId;
                    ctx.Saga.RemainingLeaveDays = ctx.Message.RemainingLeaveDays;
                    ctx.Saga.BusinessCorrelationId = ctx.Message.CorrelationId;
                    ctx.Saga.TenantId = ctx.Message.TenantId ?? Guid.Empty;
                    ctx.Saga.PendingParties = string.Join(',', opts.MandatoryParties);
                    ctx.Saga.CompletedParties = "";
                    ctx.Saga.CreatedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    metrics.Started.Add(1);
                    logger.LogInformation(
                        "Separation saga started {SeparationRecordId} parties={Parties}",
                        ctx.Saga.SeparationRecordId, ctx.Saga.PendingParties);
                })
                .ThenAsync(DispatchPendingParties)
                .Schedule(TimeoutSchedule, ctx => new SeparationPartyTimeoutMsg
                {
                    SeparationRecordId = ctx.Saga.SeparationRecordId,
                    Party = "*"
                })
                .TransitionTo(AwaitingParties));

        During(AwaitingParties,
            When(PartyCompleted)
                .Then(ctx =>
                {
                    MarkCompleted(ctx.Saga, ctx.Message.Party);
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    logger.LogInformation(
                        "Separation party completed {Party} for {Id}; pending={Pending}",
                        ctx.Message.Party, ctx.Saga.SeparationRecordId, ctx.Saga.PendingParties);
                })
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    completed => completed
                        .Unschedule(TimeoutSchedule)
                        .ThenAsync(PublishFinalCompleted)
                        .Then(_ => metrics.Completed.Add(1))
                        .TransitionTo(Completed),
                    stillWaiting => stillWaiting),
            When(PartyFaulted)
                .Then(ctx =>
                {
                    ctx.Saga.LastFaultParty = ctx.Message.Party;
                    ctx.Saga.LastFaultReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    metrics.InterventionOpen.Add(1);
                })
                .Unschedule(TimeoutSchedule)
                .ThenAsync(OpenIntervention)
                .TransitionTo(AwaitingIntervention),
            When(TimeoutSchedule.Received)
                .Then(ctx =>
                {
                    ctx.Saga.LastFaultParty = ctx.Message.Party;
                    ctx.Saga.LastFaultReason = "انتهت مهلة انتظار إقرار الطرف";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    metrics.TimedOut.Add(1);
                    metrics.InterventionOpen.Add(1);
                })
                .ThenAsync(OpenIntervention)
                .TransitionTo(AwaitingIntervention),
            When(Cancel)
                .Unschedule(TimeoutSchedule)
                .ThenAsync(StartCompensation)
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    none => none.Then(_ => metrics.Compensated.Add(1)).TransitionTo(Cancelled),
                    some => some.TransitionTo(Compensating)));

        During(AwaitingIntervention,
            When(Resume)
                .Then(ctx =>
                {
                    ctx.Saga.LastFaultReason = null;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .ThenAsync(DispatchPendingParties)
                .Schedule(TimeoutSchedule, ctx => new SeparationPartyTimeoutMsg
                {
                    SeparationRecordId = ctx.Saga.SeparationRecordId,
                    Party = "*"
                })
                .TransitionTo(AwaitingParties),
            When(Cancel)
                .ThenAsync(StartCompensation)
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    none => none.Then(_ => metrics.Compensated.Add(1)).TransitionTo(Cancelled),
                    some => some.TransitionTo(Compensating)),
            Ignore(PartyCompleted),
            Ignore(PartyFaulted));

        During(Compensating,
            When(PartyCompleted)
                .Then(ctx =>
                {
                    MarkCompleted(ctx.Saga, ctx.Message.Party);
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .If(ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    then => then
                        .Then(_ => metrics.Compensated.Add(1))
                        .TransitionTo(Cancelled)),
            When(PartyFaulted)
                .Then(ctx =>
                {
                    ctx.Saga.LastFaultReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .ThenAsync(OpenIntervention)
                .TransitionTo(AwaitingIntervention));

        During(Completed, Ignore(PartyCompleted), Ignore(Resume));
        During(Cancelled, Ignore(PartyCompleted), Ignore(Resume));
    }

    public State AwaitingParties { get; private set; } = null!;
    public State AwaitingIntervention { get; private set; } = null!;
    public State Compensating { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;
    public State Faulted { get; private set; } = null!;

    public Event<IStartSeparationSaga> Started { get; private set; } = null!;
    public Event<ISeparationPartyCompleted> PartyCompleted { get; private set; } = null!;
    public Event<ISeparationPartyFaulted> PartyFaulted { get; private set; } = null!;
    public Event<IResumeSeparationSaga> Resume { get; private set; } = null!;
    public Event<ICancelSeparationSaga> Cancel { get; private set; } = null!;
    public Schedule<SeparationSagaState, SeparationPartyTimeoutMsg> TimeoutSchedule { get; private set; } = null!;

    private static void MarkCompleted(SeparationSagaState saga, string party)
    {
        var pending = Split(saga.PendingParties).Where(p => !string.Equals(p, party, StringComparison.OrdinalIgnoreCase)).ToList();
        var completed = Split(saga.CompletedParties);
        if (!completed.Contains(party, StringComparer.OrdinalIgnoreCase))
            completed.Add(party);
        saga.PendingParties = string.Join(',', pending);
        saga.CompletedParties = string.Join(',', completed);
    }

    private static List<string> Split(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task DispatchPendingParties(BehaviorContext<SeparationSagaState> ctx)
    {
        foreach (var party in Split(ctx.Saga.PendingParties))
        {
            await ctx.Publish<IExecuteSeparationParty>(new
            {
                ctx.Saga.SeparationRecordId,
                ctx.Saga.EmployeeId,
                Party = party,
                ctx.Saga.SeparationType,
                ctx.Saga.EffectiveDate,
                ctx.Saga.ContractId,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = false,
                RemainingLeaveDays = ctx.Saga.RemainingLeaveDays
            });
        }
    }

    private static async Task PublishFinalCompleted(BehaviorContext<SeparationSagaState> ctx)
    {
        await ctx.Publish<ISeparationCompleted>(new
        {
            ctx.Saga.EmployeeId,
            SeparationType = ctx.Saga.SeparationType,
            EffectiveDate = ctx.Saga.EffectiveDate,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = ctx.Saga.BusinessCorrelationId,
            SourceSystem = "SeparationSaga",
            ctx.Saga.ContractId,
            SeparationRecordId = ctx.Saga.SeparationRecordId,
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });

        await ctx.Publish<ISeparationSagaFinalized>(new
        {
            SeparationRecordId = ctx.Saga.SeparationRecordId,
            Success = true
        });
    }

    private static async Task OpenIntervention(BehaviorContext<SeparationSagaState> ctx)
    {
        await ctx.Publish<IProcessInterventionRequested>(new
        {
            SagaType = SagaTypeNames.Separation,
            CorrelationId = ctx.Saga.SeparationRecordId,
            Reason = ctx.Saga.LastFaultReason ?? "تعثر رحلة الفصل",
            FailedParty = ctx.Saga.LastFaultParty,
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
    }

    private static async Task StartCompensation(BehaviorContext<SeparationSagaState> ctx)
    {
        // Compensate parties that already completed (reverse order).
        var completed = Split(ctx.Saga.CompletedParties);
        completed.Reverse();
        ctx.Saga.PendingParties = string.Join(',', completed);
        ctx.Saga.CompletedParties = "";
        foreach (var party in completed)
        {
            await ctx.Publish<IExecuteSeparationParty>(new
            {
                ctx.Saga.SeparationRecordId,
                ctx.Saga.EmployeeId,
                Party = party,
                ctx.Saga.SeparationType,
                ctx.Saga.EffectiveDate,
                ctx.Saga.ContractId,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = true,
                RemainingLeaveDays = ctx.Saga.RemainingLeaveDays
            });
        }
    }
}

public record SeparationPartyTimeoutMsg
{
    public Guid SeparationRecordId { get; init; }
    public string Party { get; init; } = "*";
}

public interface ISeparationSagaFinalized
{
    Guid SeparationRecordId { get; }
    bool Success { get; }
}
