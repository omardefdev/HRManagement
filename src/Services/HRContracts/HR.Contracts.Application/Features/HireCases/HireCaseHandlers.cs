using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.HireCases;

public record GetHireChecklistQuery(Guid ServiceTransactionId) : IRequest<IReadOnlyList<HireChecklist>>;

public class GetHireChecklistHandler(IContractsRepository repository) : IRequestHandler<GetHireChecklistQuery, IReadOnlyList<HireChecklist>>
{
    public async Task<IReadOnlyList<HireChecklist>> Handle(GetHireChecklistQuery request, CancellationToken cancellationToken) =>
        await repository.HireChecklists
            .Where(c => c.ServiceTransactionId == request.ServiceTransactionId)
            .OrderBy(c => c.Step)
            .ToListAsync(cancellationToken);
}

public record CompleteHireChecklistStepCommand(
    Guid ServiceTransactionId,
    HireStep Step,
    string? Notes,
    string? EvidenceRef = null,
    Guid? CompletedBy = null,
    string? CompletedByName = null) : IRequest<(bool Success, string? Error)>;

public class CompleteHireChecklistStepHandler(IContractsRepository repository)
    : IRequestHandler<CompleteHireChecklistStepCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        CompleteHireChecklistStepCommand request,
        CancellationToken cancellationToken)
    {
        var item = await repository.HireChecklists.FirstOrDefaultAsync(
            c => c.ServiceTransactionId == request.ServiceTransactionId && c.Step == request.Step,
            cancellationToken);
        if (item is null)
            return (false, "الخطوة غير موجودة");

        try
        {
            HireWorkflowRules.MarkStepCompleted(
                item, request.Notes, request.EvidenceRef, request.CompletedBy, request.CompletedByName);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public record PublishHireCaseCommand(Guid ServiceTransactionId) : IRequest<(bool Success, string? Error)>;

public class PublishHireCaseHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<PublishHireCaseCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(PublishHireCaseCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.ServiceTransactions
            .FirstOrDefaultAsync(x => x.Id == request.ServiceTransactionId, cancellationToken);
        if (transaction is null)
            return (false, "المعاملة غير موجودة");

        if (transaction.TransactionType != ServiceTransactionType.Appointment)
            return (false, "ليست معاملة تعيين");

        if (HireWorkflowRules.IsRejectedOrCancelled(transaction))
            return (false, "المعاملة مرفوضة أو ملغاة — لا يمكن بدء رحلة التعيين");

        var checklist = await repository.HireChecklists
            .Where(c => c.ServiceTransactionId == request.ServiceTransactionId)
            .ToListAsync(cancellationToken);

        if (!HireWorkflowRules.CanPublishHireCompleted(transaction, checklist))
            return (false, "قائمة التحقق غير مكتملة أو حالة المعاملة غير مناسبة");

        var contract = await repository.Contracts
            .AsNoTracking()
            .Where(c => c.EmployeeId == transaction.EmployeeId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (transaction.State == ServiceTransactionState.Executing)
        {
            await repository.ServiceTransactions
                .Where(t => t.Id == transaction.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(t => t.State, ServiceTransactionState.PostProcessing),
                    cancellationToken);
        }

        await repository.ServiceTransactions
            .Where(t => t.Id == transaction.Id
                        && (t.State == ServiceTransactionState.PostProcessing
                            || t.State == ServiceTransactionState.Executing))
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.State, ServiceTransactionState.Completed),
                cancellationToken);

        await eventPublisher.StartHireSagaAsync(
            transaction.Id,
            transaction.EmployeeId,
            contract?.Id,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return (true, null);
    }
}
