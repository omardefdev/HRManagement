using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Context;
using HRManagement.MessageContracts.Events;
using MassTransit;
using MediatR;

namespace HR.Contracts.Infrastructure.Consumers;

internal static class ConsumeContextExtensions
{
    public static void ApplyExecutionContext<T>(this ConsumeContext<T> context, string sourceSystem) where T : class
    {
        var correlationId = context.MessageId?.ToString();
        if (context.Headers.TryGetHeader("X-Correlation-ID", out var headerValue))
        {
            correlationId = headerValue?.ToString();
        }
        HrContractsExecutionContextFlow.Set(correlationId, sourceSystem);
    }
}

public class CandidateAcceptedConsumer(IMediator mediator) : IConsumer<ICandidateAccepted>
{
    public async Task Consume(ConsumeContext<ICandidateAccepted> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        // FUN-029: v4 members read as null/false on v3 messages (default interface members).
        var m = context.Message;
        await mediator.Send(new ProcessCandidateAcceptedCommand(
            m.EmployeeId,
            m.EmploymentCategory,
            DateOnly.FromDateTime(m.ExpectedStartDate),
            m.FullName,
            m.RecruitmentApplicationId,
            RequisitionId: m.RequisitionId,
            OfferLetterId: m.OfferLetterId,
            OfferedSalary: m.OfferedSalary,
            GradeCode: m.GradeCode,
            LegalRegimeId: m.LegalRegimeId,
            EmploymentType: m.EmploymentType,
            IsInternalMobility: m.IsInternalMobility));
    }
}

public class EmployeeOfferAcceptedConsumer(IMediator mediator) : IConsumer<IEmployeeOfferAccepted>
{
    public async Task Consume(ConsumeContext<IEmployeeOfferAccepted> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessCandidateAcceptedCommand(
            context.Message.EmployeeId,
            context.Message.EmploymentCategory,
            DateOnly.FromDateTime(context.Message.ExpectedStartDate)));
    }
}

public class ProbationEvaluationCompletedConsumer(IMediator mediator) : IConsumer<IProbationEvaluationCompleted>
{
    public async Task Consume(ConsumeContext<IProbationEvaluationCompleted> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        var result = Enum.TryParse<ProbationResult>(context.Message.Result, true, out var parsed)
            ? parsed
            : ProbationResult.Failed;
        await mediator.Send(new ProcessProbationEvaluationCompletedCommand(
            context.Message.EmployeeId,
            context.Message.ContractId,
            result,
            context.Message.EvaluatedBy,
            DateOnly.FromDateTime(context.Message.EvaluatedAt),
            context.Message.Notes));
    }
}

public class LeaveRequestedConsumer(IMediator mediator) : IConsumer<ILeaveRequested>
{
    public async Task Consume(ConsumeContext<ILeaveRequested> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        var leaveType = Enum.TryParse<LeaveType>(context.Message.LeaveType, true, out var parsed)
            ? parsed
            : LeaveType.Other;
        await mediator.Send(new ProcessLeaveRequestedCommand(
            context.Message.RequestId,
            context.Message.EmployeeId,
            leaveType,
            DateOnly.FromDateTime(context.Message.StartDate),
            DateOnly.FromDateTime(context.Message.EndDate),
            context.Message.DurationDays,
            context.Message.Notes,
            context.Message.OrgUnitId));
    }
}

public class AdvanceRequestedConsumer(IMediator mediator) : IConsumer<IAdvanceRequested>
{
    public async Task Consume(ConsumeContext<IAdvanceRequested> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessAdvanceRequestedCommand(
            context.Message.RequestId,
            context.Message.EmployeeId,
            context.Message.Amount,
            context.Message.Reason,
            context.Message.OrgUnitId));
    }
}

public class JobChangeRequestedConsumer(IMediator mediator) : IConsumer<IJobChangeRequested>
{
    public async Task Consume(ConsumeContext<IJobChangeRequested> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessJobChangeRequestedCommand(
            context.Message.RequestId,
            context.Message.EmployeeId,
            context.Message.CurrentTitle,
            context.Message.NewTitle,
            DateOnly.FromDateTime(context.Message.EffectiveDate),
            context.Message.Reason));
    }
}

public class LegalJudgmentIssuedConsumer(IMediator mediator) : IConsumer<ILegalJudgmentIssued>
{
    public async Task Consume(ConsumeContext<ILegalJudgmentIssued> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessSeparationRequestCommand(
            context.Message.EmployeeId,
            SeparationType.Judicial,
            DateOnly.FromDateTime(context.Message.EffectiveDate),
            context.Message.Notes,
            context.Message.JudgmentRef));
    }
}

public class MedicalDecisionIssuedConsumer(IMediator mediator) : IConsumer<IMedicalDecisionIssued>
{
    public async Task Consume(ConsumeContext<IMedicalDecisionIssued> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessSeparationRequestCommand(
            context.Message.EmployeeId,
            SeparationType.Medical,
            DateOnly.FromDateTime(context.Message.EffectiveDate),
            context.Message.Notes,
            context.Message.MedicalRef));
    }
}

public class ResignationSubmittedConsumer(IMediator mediator) : IConsumer<IResignationSubmitted>
{
    public async Task Consume(ConsumeContext<IResignationSubmitted> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessSeparationRequestCommand(
            context.Message.EmployeeId,
            SeparationType.Resignation,
            DateOnly.FromDateTime(context.Message.EffectiveDate),
            context.Message.Reason,
            null));
    }
}

public class TransactionRegisteredInDiwanConsumer(IMediator mediator) : IConsumer<ITransactionRegisteredInDiwan>
{
    public async Task Consume(ConsumeContext<ITransactionRegisteredInDiwan> context)
    {
        context.ApplyExecutionContext(context.Message.SourceSystem);
        await mediator.Send(new ProcessTransactionRegisteredInDiwanCommand(
            context.Message.TransactionId,
            context.Message.DiwanNumber));
    }
}
