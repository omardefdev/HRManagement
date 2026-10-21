using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Movements.Infrastructure.Sagas;

public class TransferSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid TenantId { get; set; }
    public Guid TransferRequestId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid FromOrgUnitId { get; set; }
    public Guid ToOrgUnitId { get; set; }
    public DateTime ExecutedAt { get; set; }
    public decimal? SalaryChange { get; set; }
    public decimal? AllowanceChange { get; set; }
    public string? SourceLegalRegimeId { get; set; }
    public string? TargetLegalRegimeId { get; set; }
    public string? TransitionDecisionRef { get; set; }
    public string? ConditionCode { get; set; }
    public string? ConflictNotes { get; set; }
    public string? BusinessCorrelationId { get; set; }
    public string PendingParties { get; set; } = "";
    public string CompletedParties { get; set; } = "";
    public string? LastFaultParty { get; set; }
    public string? LastFaultReason { get; set; }
    public Guid? TimeoutTokenId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TransferSagaOptions
{
    public const string SectionName = "Sagas:Transfer";
    public string[] MandatoryParties { get; set; } =
    [
        ProcessParties.Payroll,
        ProcessParties.HrCore,
        ProcessParties.AuthDirectory,
        ProcessParties.AdminLogistics
    ];
    public int PartyTimeoutMinutes { get; set; } = 30;
}

public class TransferStateMachine : MassTransitStateMachine<TransferSagaState>
{
    public TransferStateMachine(IOptions<TransferSagaOptions> options, ILogger<TransferStateMachine> logger, SagaMetrics metrics)
    {
        var opts = options.Value;
        var timeout = TimeSpan.FromMinutes(Math.Max(1, opts.PartyTimeoutMinutes));
        InstanceState(x => x.CurrentState);

        Event(() => Started, e =>
        {
            e.CorrelateById(ctx => ctx.Message.TransferRequestId);
            e.SelectId(ctx => ctx.Message.TransferRequestId);
        });
        Event(() => PartyCompleted, e => e.CorrelateById(ctx => ctx.Message.TransferRequestId));
        Event(() => PartyFaulted, e => e.CorrelateById(ctx => ctx.Message.TransferRequestId));
        Event(() => Resume, e => e.CorrelateById(ctx => ctx.Message.TransferRequestId));
        Event(() => Cancel, e => e.CorrelateById(ctx => ctx.Message.TransferRequestId));

        Schedule(() => TimeoutSchedule, i => i.TimeoutTokenId, s =>
        {
            s.Delay = timeout;
            s.Received = r =>
            {
                r.CorrelateById(ctx => ctx.Message.TransferRequestId);
                r.ConfigureConsumeTopology = false;
            };
        });

        Initially(
            When(Started)
                .Then(ctx =>
                {
                    ctx.Saga.TransferRequestId = ctx.Message.TransferRequestId;
                    ctx.Saga.EmployeeId = ctx.Message.EmployeeId;
                    ctx.Saga.FromOrgUnitId = ctx.Message.FromOrgUnitId;
                    ctx.Saga.ToOrgUnitId = ctx.Message.ToOrgUnitId;
                    ctx.Saga.ExecutedAt = ctx.Message.ExecutedAt;
                    ctx.Saga.SalaryChange = ctx.Message.SalaryChange;
                    ctx.Saga.AllowanceChange = ctx.Message.AllowanceChange;
                    ctx.Saga.SourceLegalRegimeId = ctx.Message.SourceLegalRegimeId;
                    ctx.Saga.TargetLegalRegimeId = ctx.Message.TargetLegalRegimeId;
                    ctx.Saga.TransitionDecisionRef = ctx.Message.TransitionDecisionRef;
                    ctx.Saga.ConditionCode = ctx.Message.ConditionCode;
                    ctx.Saga.ConflictNotes = ctx.Message.ConflictNotes;
                    ctx.Saga.BusinessCorrelationId = ctx.Message.CorrelationId;
                    ctx.Saga.TenantId = ctx.Message.TenantId ?? Guid.Empty;
                    ctx.Saga.PendingParties = string.Join(',', opts.MandatoryParties);
                    ctx.Saga.CreatedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    metrics.Started.Add(1);
                })
                .ThenAsync(Dispatch)
                .Schedule(TimeoutSchedule, ctx => new TransferPartyTimeoutMsg
                {
                    TransferRequestId = ctx.Saga.TransferRequestId,
                    Party = "*"
                })
                .TransitionTo(AwaitingParties));

        During(AwaitingParties,
            When(PartyCompleted)
                .Then(ctx =>
                {
                    Mark(ctx.Saga, ctx.Message.Party);
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    done => done
                        .Unschedule(TimeoutSchedule)
                        .ThenAsync(PublishFinal)
                        .Then(_ => metrics.Completed.Add(1))
                        .TransitionTo(Completed),
                    _ => _),
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
                    ctx.Saga.LastFaultReason = "انتهت مهلة انتظار إقرار طرف النقل";
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
                .ThenAsync(Dispatch)
                .Schedule(TimeoutSchedule, ctx => new TransferPartyTimeoutMsg
                {
                    TransferRequestId = ctx.Saga.TransferRequestId,
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
                    Mark(ctx.Saga, ctx.Message.Party);
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
    public Event<IStartTransferSaga> Started { get; private set; } = null!;
    public Event<ITransferPartyCompleted> PartyCompleted { get; private set; } = null!;
    public Event<ITransferPartyFaulted> PartyFaulted { get; private set; } = null!;
    public Event<IResumeTransferSaga> Resume { get; private set; } = null!;
    public Event<ICancelTransferSaga> Cancel { get; private set; } = null!;
    public Schedule<TransferSagaState, TransferPartyTimeoutMsg> TimeoutSchedule { get; private set; } = null!;

    private static void Mark(TransferSagaState saga, string party)
    {
        var pending = Split(saga.PendingParties).Where(p => !string.Equals(p, party, StringComparison.OrdinalIgnoreCase)).ToList();
        var completed = Split(saga.CompletedParties);
        if (!completed.Contains(party, StringComparer.OrdinalIgnoreCase))
            completed.Add(party);
        saga.PendingParties = string.Join(',', pending);
        saga.CompletedParties = string.Join(',', completed);
    }

    private static List<string> Split(string v) =>
        string.IsNullOrWhiteSpace(v) ? [] : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task Dispatch(BehaviorContext<TransferSagaState> ctx)
    {
        var toRegime = ctx.Saga.TargetLegalRegimeId ?? ctx.Saga.SourceLegalRegimeId;
        var payrollRegimeCode = ServiceRegimeToPayrollMapper.MapToPayrollCode(toRegime);
        foreach (var party in Split(ctx.Saga.PendingParties))
        {
            await ctx.Publish<IExecuteTransferParty>(new
            {
                ctx.Saga.TransferRequestId,
                ctx.Saga.EmployeeId,
                ctx.Saga.FromOrgUnitId,
                ctx.Saga.ToOrgUnitId,
                Party = party,
                ctx.Saga.ExecutedAt,
                ctx.Saga.SalaryChange,
                ctx.Saga.AllowanceChange,
                PayrollRegimeCode = payrollRegimeCode,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = false
            });
        }
    }

    private static async Task StartCompensation(BehaviorContext<TransferSagaState> ctx)
    {
        var completed = Split(ctx.Saga.CompletedParties);
        completed.Reverse();
        ctx.Saga.PendingParties = string.Join(',', completed);
        ctx.Saga.CompletedParties = "";
        ctx.Saga.UpdatedAt = DateTime.UtcNow;
        foreach (var party in completed)
        {
            await ctx.Publish<IExecuteTransferParty>(new
            {
                ctx.Saga.TransferRequestId,
                ctx.Saga.EmployeeId,
                ctx.Saga.FromOrgUnitId,
                ctx.Saga.ToOrgUnitId,
                Party = party,
                ctx.Saga.ExecutedAt,
                ctx.Saga.SalaryChange,
                ctx.Saga.AllowanceChange,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = true
            });
        }
    }

    private static async Task PublishFinal(BehaviorContext<TransferSagaState> ctx)
    {
        var toRegime = ctx.Saga.TargetLegalRegimeId ?? ctx.Saga.SourceLegalRegimeId;
        await ctx.Publish<ITransferExecuted>(new
        {
            ctx.Saga.EmployeeId,
            ctx.Saga.FromOrgUnitId,
            ctx.Saga.ToOrgUnitId,
            ctx.Saga.ExecutedAt,
            TransferRequestId = ctx.Saga.TransferRequestId,
            ctx.Saga.SalaryChange,
            ctx.Saga.AllowanceChange,
            FromLegalRegimeId = ctx.Saga.SourceLegalRegimeId,
            ToLegalRegimeId = toRegime,
            PayrollRegimeCode = ServiceRegimeToPayrollMapper.MapToPayrollCode(toRegime),
            TransitionDecisionRef = ctx.Saga.TransitionDecisionRef,
            ConditionCode = ctx.Saga.ConditionCode,
            ConflictNotes = ctx.Saga.ConflictNotes,
            CorrelationId = Guid.TryParse(ctx.Saga.BusinessCorrelationId, out var c) ? c : (Guid?)null,
            SourceSystem = "TransferSaga",
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
    }

    private static Task OpenIntervention(BehaviorContext<TransferSagaState> ctx) =>
        ctx.Publish<IProcessInterventionRequested>(new
        {
            SagaType = SagaTypeNames.Transfer,
            CorrelationId = ctx.Saga.TransferRequestId,
            Reason = ctx.Saga.LastFaultReason ?? "تعثر رحلة النقل",
            FailedParty = ctx.Saga.LastFaultParty,
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
}

public record TransferPartyTimeoutMsg
{
    public Guid TransferRequestId { get; init; }
    public string Party { get; init; } = "*";
}

public class SecondmentSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid TenantId { get; set; }
    public Guid SecondmentRequestId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime ActivatedAt { get; set; }
    public string HostOrg { get; set; } = "";
    public DateTime EndDate { get; set; }
    public string? BusinessCorrelationId { get; set; }
    public string PendingParties { get; set; } = "";
    public string CompletedParties { get; set; } = "";
    public string? LastFaultParty { get; set; }
    public string? LastFaultReason { get; set; }
    public Guid? TimeoutTokenId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SecondmentSagaOptions
{
    public const string SectionName = "Sagas:Secondment";
    public string[] MandatoryParties { get; set; } = [ProcessParties.Payroll, ProcessParties.AuthDirectory];
    public int PartyTimeoutMinutes { get; set; } = 30;
}

public class SecondmentStateMachine : MassTransitStateMachine<SecondmentSagaState>
{
    public SecondmentStateMachine(IOptions<SecondmentSagaOptions> options, SagaMetrics metrics)
    {
        var opts = options.Value;
        var timeout = TimeSpan.FromMinutes(Math.Max(1, opts.PartyTimeoutMinutes));
        InstanceState(x => x.CurrentState);

        Event(() => Started, e =>
        {
            e.CorrelateById(ctx => ctx.Message.SecondmentRequestId);
            e.SelectId(ctx => ctx.Message.SecondmentRequestId);
        });
        Event(() => PartyCompleted, e => e.CorrelateById(ctx => ctx.Message.SecondmentRequestId));
        Event(() => PartyFaulted, e => e.CorrelateById(ctx => ctx.Message.SecondmentRequestId));
        Event(() => Resume, e => e.CorrelateById(ctx => ctx.Message.SecondmentRequestId));
        Event(() => Cancel, e => e.CorrelateById(ctx => ctx.Message.SecondmentRequestId));

        Schedule(() => TimeoutSchedule, i => i.TimeoutTokenId, s =>
        {
            s.Delay = timeout;
            s.Received = r =>
            {
                r.CorrelateById(ctx => ctx.Message.SecondmentRequestId);
                r.ConfigureConsumeTopology = false;
            };
        });

        Initially(
            When(Started)
                .Then(ctx =>
                {
                    ctx.Saga.SecondmentRequestId = ctx.Message.SecondmentRequestId;
                    ctx.Saga.EmployeeId = ctx.Message.EmployeeId;
                    ctx.Saga.ActivatedAt = ctx.Message.ActivatedAt;
                    ctx.Saga.HostOrg = ctx.Message.HostOrg ?? "";
                    ctx.Saga.EndDate = ctx.Message.EndDate;
                    ctx.Saga.BusinessCorrelationId = ctx.Message.CorrelationId;
                    ctx.Saga.TenantId = ctx.Message.TenantId ?? Guid.Empty;
                    ctx.Saga.PendingParties = string.Join(',', opts.MandatoryParties);
                    ctx.Saga.CreatedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                    metrics.Started.Add(1);
                })
                .ThenAsync(Dispatch)
                .Schedule(TimeoutSchedule, ctx => new SecondmentPartyTimeoutMsg
                {
                    SecondmentRequestId = ctx.Saga.SecondmentRequestId,
                    Party = "*"
                })
                .TransitionTo(AwaitingParties));

        During(AwaitingParties,
            When(PartyCompleted)
                .Then(ctx => Mark(ctx.Saga, ctx.Message.Party))
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    done => done
                        .Unschedule(TimeoutSchedule)
                        .ThenAsync(PublishFinal)
                        .Then(_ => metrics.Completed.Add(1))
                        .TransitionTo(Completed),
                    _ => _),
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
                    ctx.Saga.LastFaultReason = "انتهت مهلة انتظار إقرار طرف الانتداب";
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
                .ThenAsync(Dispatch)
                .Schedule(TimeoutSchedule, ctx => new SecondmentPartyTimeoutMsg
                {
                    SecondmentRequestId = ctx.Saga.SecondmentRequestId,
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
                .Then(ctx => Mark(ctx.Saga, ctx.Message.Party))
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

        During(Completed,
            When(Cancel)
                .ThenAsync(StartCompensation)
                .IfElse(
                    ctx => string.IsNullOrWhiteSpace(ctx.Saga.PendingParties),
                    none => none.Then(_ => metrics.Compensated.Add(1)).TransitionTo(Cancelled),
                    some => some.TransitionTo(Compensating)),
            Ignore(PartyCompleted),
            Ignore(Resume));
        During(Cancelled, Ignore(PartyCompleted), Ignore(Resume), Ignore(Cancel));
    }

    public State AwaitingParties { get; private set; } = null!;
    public State AwaitingIntervention { get; private set; } = null!;
    public State Compensating { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;
    public Event<IStartSecondmentSaga> Started { get; private set; } = null!;
    public Event<ISecondmentPartyCompleted> PartyCompleted { get; private set; } = null!;
    public Event<ISecondmentPartyFaulted> PartyFaulted { get; private set; } = null!;
    public Event<IResumeSecondmentSaga> Resume { get; private set; } = null!;
    public Event<ICancelSecondmentSaga> Cancel { get; private set; } = null!;
    public Schedule<SecondmentSagaState, SecondmentPartyTimeoutMsg> TimeoutSchedule { get; private set; } = null!;

    private static void Mark(SecondmentSagaState saga, string party)
    {
        var pending = Split(saga.PendingParties).Where(p => !string.Equals(p, party, StringComparison.OrdinalIgnoreCase)).ToList();
        var completed = Split(saga.CompletedParties);
        if (!completed.Contains(party, StringComparer.OrdinalIgnoreCase))
            completed.Add(party);
        saga.PendingParties = string.Join(',', pending);
        saga.CompletedParties = string.Join(',', completed);
    }

    private static List<string> Split(string v) =>
        string.IsNullOrWhiteSpace(v) ? [] : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task Dispatch(BehaviorContext<SecondmentSagaState> ctx)
    {
        foreach (var party in Split(ctx.Saga.PendingParties))
        {
            await ctx.Publish<IExecuteSecondmentParty>(new
            {
                ctx.Saga.SecondmentRequestId,
                ctx.Saga.EmployeeId,
                Party = party,
                ctx.Saga.ActivatedAt,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = false
            });
        }
    }

    private static async Task StartCompensation(BehaviorContext<SecondmentSagaState> ctx)
    {
        var completed = Split(ctx.Saga.CompletedParties);
        completed.Reverse();
        ctx.Saga.PendingParties = string.Join(',', completed);
        ctx.Saga.CompletedParties = "";
        ctx.Saga.UpdatedAt = DateTime.UtcNow;
        foreach (var party in completed)
        {
            await ctx.Publish<IExecuteSecondmentParty>(new
            {
                ctx.Saga.SecondmentRequestId,
                ctx.Saga.EmployeeId,
                Party = party,
                ctx.Saga.ActivatedAt,
                CorrelationId = ctx.Saga.BusinessCorrelationId,
                TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId,
                Compensate = true
            });
        }
    }

    private static async Task PublishFinal(BehaviorContext<SecondmentSagaState> ctx)
    {
        await ctx.Publish<ISecondmentExecuted>(new
        {
            SecondmentRequestId = ctx.Saga.SecondmentRequestId,
            ctx.Saga.EmployeeId,
            HostOrg = ctx.Saga.HostOrg,
            StartDate = ctx.Saga.ActivatedAt,
            EndDate = ctx.Saga.EndDate == default ? ctx.Saga.ActivatedAt.AddMonths(6) : ctx.Saga.EndDate,
            CorrelationId = Guid.TryParse(ctx.Saga.BusinessCorrelationId, out var c) ? c : Guid.NewGuid(),
            SourceSystem = "SecondmentSaga",
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
    }

    private static Task OpenIntervention(BehaviorContext<SecondmentSagaState> ctx) =>
        ctx.Publish<IProcessInterventionRequested>(new
        {
            SagaType = SagaTypeNames.Secondment,
            CorrelationId = ctx.Saga.SecondmentRequestId,
            Reason = ctx.Saga.LastFaultReason ?? "تعثر رحلة الانتداب",
            FailedParty = ctx.Saga.LastFaultParty,
            TenantId = ctx.Saga.TenantId == Guid.Empty ? (Guid?)null : ctx.Saga.TenantId
        });
}

public record SecondmentPartyTimeoutMsg
{
    public Guid SecondmentRequestId { get; init; }
    public string Party { get; init; } = "*";
}
