using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Infrastructure.Sagas;

public class HireCaseSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid TenantId { get; set; }
    public Guid ServiceTransactionId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? RecruitmentApplicationId { get; set; }
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

public sealed class HireSagaOptions
{
    public const string SectionName = "Sagas:Hire";
    public string[] MandatoryParties { get; set; } =
    [
        ProcessParties.HrCore,
        ProcessParties.Payroll,
        ProcessParties.AuthDirectory,
        ProcessParties.IdCard,
        ProcessParties.Archive,
        ProcessParties.ItInfra
    ];
    public int PartyTimeoutMinutes { get; set; } = 30;
}

public class HireCaseStateMachine : MassTransitStateMachine<HireCaseSagaState>
{
    public HireCaseStateMachine(IOptions<HireSagaOptions> options, ILogger<HireCaseStateMachine> logger, SagaMetrics metrics)
    {
        var opts = options.Value;
        var timeout = TimeSpan.FromMinutes(Math.Max(1, opts.PartyTimeoutMinutes));

        InstanceState(x => x.CurrentState);
        Event(() => Started, e =>
        {
            e.CorrelateById(ctx => ctx.Message.ServiceTransactionId);
            e.SelectId(ctx => ctx.Message.ServiceTransactionId);
        });
        Event(() => PartyCompleted, e => e.CorrelateById(ctx => ctx.Message.ServiceTransactionId));
        Event(() => PartyFaulted, e => e.CorrelateById(ctx => ctx.Message.ServiceTransactionId));
        Event(() => Resume, e => e.CorrelateById(ctx => ctx.Message.ServiceTransactionId));
        Event(() => Cancel, e => e.CorrelateById(ctx => ctx.Message.ServiceTransactionId));

        Schedule(() => TimeoutSchedule, instance => instance.TimeoutTokenId, s =>
        {
            s.Delay = timeout;
            s.Received = r =>
            {
                r.CorrelateById(ctx => ctx.Message.ServiceTransactionId);
                r.ConfigureConsumeTopology = false;
            };
        });

        Initially(
            When(Started)
                .Then(ctx =>
                {
                    ctx.Saga.ServiceTransactionId = ctx.Message.ServiceTransactionId;
                    ctx.Saga.EmployeeId = ctx.Message.EmployeeId;
                    ctx.Saga.ContractId = ctx.Message.ContractId;
                    ctx.Saga.RecruitmentApplicationId = ctx.Message.RecruitmentApplicationId;
                    ctx.Saga.BusinessCorrelationId = ctx.Message.CorrelationId;
                    ctx.Saga.TenantId = ctx.Message.TenantId ?? Guid.Empty;
                    ctx.Saga.PendingParties = string.Join(',', opts.MandatoryParties);
                    ctx.Saga.CompletedParties = "";
                    ctx.Saga.CreatedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    metrics.Started.Add(1);
                })
                .ThenAsync(DispatchPending)
                .Schedule(TimeoutSchedule, ctx => new HirePartyTimeoutMsg
                {
                    ServiceTransactionId = ctx.Saga.ServiceTransactionId,
                    Party = "*"
                })
                .TransitionTo(AwaitingParties));

        During(AwaitingParties,
            When(PartyCompleted)
                .Then(ctx =>
                {
                    MarkCompleted(ctx.Saga, ctx.Message.Party);
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    done => done
                        .Unschedule(TimeoutSchedule)
                        .ThenAsync(PublishFinal)
                        .Then(_ => metrics.Completed.Add(1))
                        .TransitionTo(Completed),
                    wait => wait),
            When(PartyFaulted)
                .Then(ctx =>
                {
                    ctx.Saga.LastFaultParty = ctx.Message.Party;
                    ctx.Saga.LastFaultReason = ctx.Message.Reason;
                    metrics.InterventionOpen.Add(1);
                })
                .Unschedule(TimeoutSchedule)
                .ThenAsync(OpenIntervention)
                .TransitionTo(AwaitingIntervention),
            When(TimeoutSchedule.Received)
                .Then(ctx =>
                {
                    ctx.Saga.LastFaultReason = "انتهت مهلة انتظار إقرار طرف التعيين";
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
                .ThenAsync(DispatchPending)
                .Schedule(TimeoutSchedule, ctx => new HirePartyTimeoutMsg
                {
                    ServiceTransactionId = ctx.Saga.ServiceTransactionId,
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

        During(Completed, Ignore(PartyCompleted), Ignore(Resume), Ignore(Cancel));
        During(Cancelled, Ignore(PartyCompleted), Ignore(Resume), Ignore(Cancel));
    }

    public State AwaitingParties { get; private set; } = null!;
    public State AwaitingIntervention { get; private set; } = null!;
    public State Compensating { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<IStartHireSaga> Started { get; private set; } = null!;
    public Event<IHirePartyCompleted> PartyCompleted { get; private set; } = null!;
    public Event<IHirePartyFaulted> PartyFaulted { get; private set; } = null!;
    public Event<IResumeHireSaga> Resume { get; private set; } = null!;
    public Event<ICancelHireSaga> Cancel { get; private set; } = null!;
    public Schedule<HireCaseSagaState, HirePartyTimeoutMsg> TimeoutSchedule { get; private set; } = null!;

    private static void MarkCompleted(HireCaseSagaState saga, string party)
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

    private static async Task DispatchPending(BehaviorContext<HireCaseSagaState> ctx)
    {
        foreach (var party in Split(ctx.Saga.PendingParties))
        {
            await ctx.Publish<IExecuteHireParty>(new
            {
                ctx.Saga.ServiceTransactionId,
                ctx.Saga.EmployeeId,
                Party = party,
                ctx.Saga.ContractId,
                ctx.Saga.RecruitmentApplicationId,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = false
            });
        }
    }

    private static async Task StartCompensation(BehaviorContext<HireCaseSagaState> ctx)
    {
        var completed = Split(ctx.Saga.CompletedParties);
        completed.Reverse();
        ctx.Saga.PendingParties = string.Join(',', completed);
        ctx.Saga.CompletedParties = "";
        ctx.Saga.UpdatedAt = DateTime.UtcNow;
        foreach (var party in completed)
        {
            await ctx.Publish<IExecuteHireParty>(new
            {
                ctx.Saga.ServiceTransactionId,
                ctx.Saga.EmployeeId,
                Party = party,
                ctx.Saga.ContractId,
                ctx.Saga.RecruitmentApplicationId,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = true
            });
        }
    }

    private static async Task PublishFinal(BehaviorContext<HireCaseSagaState> ctx)
    {
        await ctx.Publish<IHireCaseCompleted>(new
        {
            ctx.Saga.EmployeeId,
            ctx.Saga.ServiceTransactionId,
            CompletedAt = DateTime.UtcNow,
            CorrelationId = ctx.Saga.BusinessCorrelationId,
            SourceSystem = "HireCaseSaga",
            ctx.Saga.ContractId,
            ctx.Saga.RecruitmentApplicationId,
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
    }

    private static async Task OpenIntervention(BehaviorContext<HireCaseSagaState> ctx)
    {
        await ctx.Publish<IProcessInterventionRequested>(new
        {
            SagaType = SagaTypeNames.Hire,
            CorrelationId = ctx.Saga.ServiceTransactionId,
            Reason = ctx.Saga.LastFaultReason ?? "تعثر رحلة التعيين",
            FailedParty = ctx.Saga.LastFaultParty,
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
    }
}

public record HirePartyTimeoutMsg
{
    public Guid ServiceTransactionId { get; init; }
    public string Party { get; init; } = "*";
}
