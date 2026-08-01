using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.InboundEvents;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Consumers;

// INT-002: inbox dedupe is handled by InboxConsumeFilter (same TX as business SaveChanges).

public class ContractActivatedConsumer(IMediator mediator) : IConsumer<IContractActivated>
{
    public async Task Consume(ConsumeContext<IContractActivated> context)
    {
        var m = context.Message;
        await mediator.Send(new HandleContractActivatedCommand(
            m.ContractId, m.EmployeeId, m.ContractType, m.StartDate, m.BaseSalary, m.PayGradeCode),
            context.CancellationToken);
    }
}

public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>
{
    public Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        // Settlement creation runs only via IExecuteSeparationParty to avoid duplicate EOS rows.
        return Task.CompletedTask;
    }
}

/// <summary>INT-003 / FUN-002: Payroll party for separation saga.</summary>
public class SeparationPartyConsumer(IMediator mediator, PayrollDbContext db) : IConsumer<IExecuteSeparationParty>
{
    public async Task Consume(ConsumeContext<IExecuteSeparationParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string evidence;
            if (m.Compensate)
            {
                evidence = await VoidDraftEosAsync(m.SeparationRecordId, m.EmployeeId, context.CancellationToken);
            }
            else
            {
                await mediator.Send(new HandleSeparationCompletedCommand(
                    m.EmployeeId, m.SeparationType, m.EffectiveDate, m.SeparationRecordId, m.RemainingLeaveDays),
                    context.CancellationToken);
                var eosId = await db.EndOfServiceSettlements.AsNoTracking()
                    .Where(e => e.SeparationRecordId == m.SeparationRecordId)
                    .OrderByDescending(e => e.CreatedAt)
                    .Select(e => (Guid?)e.Id)
                    .FirstOrDefaultAsync(context.CancellationToken);
                evidence = eosId.HasValue ? $"eos:{eosId}" : "payroll:settled";
            }

            await context.Publish<ISeparationPartyCompleted>(new
            {
                m.SeparationRecordId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = evidence,
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<ISeparationPartyFaulted>(new
            {
                m.SeparationRecordId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }

    private async Task<string> VoidDraftEosAsync(Guid separationRecordId, Guid employeeId, CancellationToken ct)
    {
        var drafts = await db.EndOfServiceSettlements
            .Where(e => e.SeparationRecordId == separationRecordId
                        && e.EmployeeId == employeeId
                        && e.Status == Payroll.Domain.Enums.EosStatus.Draft)
            .ToListAsync(ct);
        foreach (var d in drafts)
            d.Status = Payroll.Domain.Enums.EosStatus.Cancelled;
        if (drafts.Count > 0)
            await db.SaveChangesAsync(ct);
        return drafts.Count > 0 ? $"payroll:eos-cancelled:{drafts.Count}" : "payroll:compensate";
    }
}

/// <summary>INT-003: Payroll party for transfer saga.</summary>
public class TransferPartyConsumer(IMediator mediator) : IConsumer<IExecuteTransferParty>
{
    public async Task Consume(ConsumeContext<IExecuteTransferParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (!m.Compensate)
            {
                await mediator.Send(new HandleTransferExecutedCommand(
                    m.EmployeeId, m.FromOrgUnitId, m.ToOrgUnitId, m.ExecutedAt, m.SalaryChange, m.AllowanceChange),
                    context.CancellationToken);
            }

            await context.Publish<ITransferPartyCompleted>(new
            {
                m.TransferRequestId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? "payroll:transfer-compensate" : "payroll:transfer",
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<ITransferPartyFaulted>(new
            {
                m.TransferRequestId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }
}

/// <summary>INT-003: Payroll party for hire saga (profile already created via contract activate).</summary>
public class HirePartyConsumer : IConsumer<IExecuteHireParty>
{
    public async Task Consume(ConsumeContext<IExecuteHireParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
            return;

        await context.Publish<IHirePartyCompleted>(new
        {
            m.ServiceTransactionId,
            m.EmployeeId,
            m.Party,
            EvidenceRef = m.Compensate ? "payroll:hire-compensate" : "payroll:hire-ack",
            m.CorrelationId,
            m.TenantId
        });
    }
}

/// <summary>INT-003: Payroll party for secondment saga.</summary>
public class SecondmentPartyConsumer(IMediator mediator) : IConsumer<IExecuteSecondmentParty>
{
    public async Task Consume(ConsumeContext<IExecuteSecondmentParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.Payroll, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (!m.Compensate)
            {
                await mediator.Send(new HandleSecondmentExecutedCommand(m.SecondmentRequestId, m.EmployeeId, ""),
                    context.CancellationToken);
            }

            await context.Publish<ISecondmentPartyCompleted>(new
            {
                m.SecondmentRequestId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? "payroll:secondment-compensate" : "payroll:secondment",
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<ISecondmentPartyFaulted>(new
            {
                m.SecondmentRequestId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }
}

public class TransferExecutedConsumer(IMediator mediator) : IConsumer<ITransferExecuted>
{
    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        await mediator.Send(new HandleTransferExecutedCommand(
            m.EmployeeId, m.FromOrgUnitId, m.ToOrgUnitId, m.ExecutedAt, m.SalaryChange, m.AllowanceChange),
            context.CancellationToken);
    }
}

public class LeaveApprovedConsumer(IMediator mediator, PayrollDbContext db) : IConsumer<ILeaveApproved>
{
    public async Task Consume(ConsumeContext<ILeaveApproved> context)
    {
        if (await db.ShouldSkipAsync(context, nameof(LeaveApprovedConsumer), context.CancellationToken))
            return;

        var m = context.Message;
        await mediator.Send(new HandleLeaveApprovedCommand(
            m.LeaveRequestId, m.EmployeeId, m.LeaveType, m.DurationDays,
            DateOnly.FromDateTime(m.StartDate), DateOnly.FromDateTime(m.EndDate)),
            context.CancellationToken);

        await db.TryRecordAsync(context, nameof(LeaveApprovedConsumer), context.CancellationToken);
    }
}

public class LeaveCancelledConsumer(IMediator mediator, PayrollDbContext db) : IConsumer<ILeaveCancelled>
{
    public async Task Consume(ConsumeContext<ILeaveCancelled> context)
    {
        if (await db.ShouldSkipAsync(context, nameof(LeaveCancelledConsumer), context.CancellationToken))
            return;

        var m = context.Message;
        await mediator.Send(new HandleLeaveCancelledCommand(
            m.LeaveRequestId, m.EmployeeId, m.LeaveType, m.DurationDays,
            DateOnly.FromDateTime(m.StartDate), DateOnly.FromDateTime(m.EndDate)),
            context.CancellationToken);

        await db.TryRecordAsync(context, nameof(LeaveCancelledConsumer), context.CancellationToken);
    }
}

public class AdvanceApprovedConsumer(IMediator mediator, PayrollDbContext db) : IConsumer<IAdvanceApproved>
{
    public async Task Consume(ConsumeContext<IAdvanceApproved> context)
    {
        if (await db.ShouldSkipAsync(context, nameof(AdvanceApprovedConsumer), context.CancellationToken))
            return;

        var m = context.Message;
        await mediator.Send(new HandleAdvanceApprovedCommand(
            m.AdvanceRequestId, m.EmployeeId, m.Amount, m.MonthlyInstallment, m.NumberOfInstallments),
            context.CancellationToken);

        await db.TryRecordAsync(context, nameof(AdvanceApprovedConsumer), context.CancellationToken);
    }
}

public class EmployeeCreatedConsumer(IMediator mediator) : IConsumer<IEmployeeCreated>
{
    public async Task Consume(ConsumeContext<IEmployeeCreated> context) =>
        await mediator.Send(new HandleEmployeeCreatedCommand(context.Message.EmployeeId, context.Message.PrimaryOrgUnitId), context.CancellationToken);
}

public class SecondmentExecutedConsumer(IMediator mediator) : IConsumer<ISecondmentExecuted>
{
    public async Task Consume(ConsumeContext<ISecondmentExecuted> context)
    {
        var m = context.Message;
        await mediator.Send(new HandleSecondmentExecutedCommand(m.SecondmentRequestId, m.EmployeeId, m.HostOrg),
            context.CancellationToken);
    }
}

public class MissionApprovedConsumer(IMediator mediator) : IConsumer<IMissionApproved>
{
    public async Task Consume(ConsumeContext<IMissionApproved> context)
    {
        var m = context.Message;
        await mediator.Send(new HandleMissionApprovedCommand(m.MissionFileId, m.EmployeeId, m.Destination),
            context.CancellationToken);
    }
}

public class DisciplinaryActionTakenConsumer(IMediator mediator, PayrollDbContext db) : IConsumer<IDisciplinaryActionTaken>
{
    public async Task Consume(ConsumeContext<IDisciplinaryActionTaken> context)
    {
        if (await db.ShouldSkipAsync(context, nameof(DisciplinaryActionTakenConsumer), context.CancellationToken))
            return;

        var m = context.Message;
        await mediator.Send(new HandleDisciplinaryActionCommand(
            m.ActionId, m.EmployeeId, m.ActionType, m.PenaltyDays, m.Notes),
            context.CancellationToken);

        await db.TryRecordAsync(context, nameof(DisciplinaryActionTakenConsumer), context.CancellationToken);
    }
}

public class TrainingCompletedConsumer(IMediator mediator) : IConsumer<ITrainingCompleted>
{
    public async Task Consume(ConsumeContext<ITrainingCompleted> context)
    {
        var m = context.Message;
        await mediator.Send(new HandleTrainingCompletedCommand(m.EmployeeId, m.CourseId, m.Score),
            context.CancellationToken);
    }
}

public class MedicalRecommendationIssuedConsumer(IMediator mediator) : IConsumer<IMedicalRecommendationIssued>
{
    public async Task Consume(ConsumeContext<IMedicalRecommendationIssued> context)
    {
        var m = context.Message;
        await mediator.Send(new HandleMedicalRecommendationIssuedCommand(
            m.RecommendationId, m.EmployeeId, m.RecommendationType, m.Details),
            context.CancellationToken);
    }
}

public class AttendancePeriodClosedConsumer(PayrollDbContext db) : IConsumer<IAttendancePeriodClosed>
{
    public async Task Consume(ConsumeContext<IAttendancePeriodClosed> context)
    {
        var m = context.Message;
        if (m.AbsentDays <= 0)
            return;

        var exists = await db.AbsenceDeductions.AnyAsync(x =>
            x.EmployeeId == m.EmployeeId
            && x.PeriodStart == m.PeriodStart
            && x.PeriodEnd == m.PeriodEnd
            && x.Reason != null
            && x.Reason.Contains("AttendancePeriodClosed"),
            context.CancellationToken);

        if (!exists)
        {
            db.AbsenceDeductions.Add(new Payroll.Domain.Entities.AbsenceDeduction
            {
                EmployeeId = m.EmployeeId,
                PeriodStart = m.PeriodStart,
                PeriodEnd = m.PeriodEnd,
                AbsentDays = m.AbsentDays,
                Amount = 0,
                Reason = $"AttendancePeriodClosed:{m.TransactionId}"
            });
            await db.SaveChangesAsync(context.CancellationToken);
        }
    }
}
