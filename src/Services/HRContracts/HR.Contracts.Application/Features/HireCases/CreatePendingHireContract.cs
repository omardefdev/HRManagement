using System.Text.Json;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.HireCases;

/// <summary>
/// FUN-029 (F3): HR creates the pending contract of an unresolved hire with a chosen contract type. Uses the offer
/// terms kept on the transaction, then the hire continues normally (the banner disappears).
/// </summary>
public record CreatePendingHireContractCommand(Guid ServiceTransactionId, Guid ContractTypeId, Guid? ActorUserId)
    : IRequest<(bool Success, string? Error)>;

public class CreatePendingHireContractHandler(IContractsRepository repository)
    : IRequestHandler<CreatePendingHireContractCommand, (bool Success, string? Error)>
{
    public const string AlreadyHasContractMessage = "يوجد عقد مسبقاً لهذا الموظف.";
    public const string ContractTypeNotFoundMessage = "نوع العقد المختار غير موجود.";

    public async Task<(bool Success, string? Error)> Handle(
        CreatePendingHireContractCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.ServiceTransactions
            .FirstOrDefaultAsync(t => t.Id == request.ServiceTransactionId, cancellationToken);
        if (transaction is null || transaction.TransactionType != ServiceTransactionType.Appointment)
            return (false, "معاملة التعيين غير موجودة.");
        if (HireWorkflowRules.IsRejectedOrCancelled(transaction))
            return (false, "المعاملة مرفوضة أو ملغاة.");
        if (await repository.Contracts.AnyAsync(c => c.EmployeeId == transaction.EmployeeId, cancellationToken))
            return (false, AlreadyHasContractMessage);
        if (!await repository.ContractTypes.AnyAsync(t => t.Id == request.ContractTypeId, cancellationToken))
            return (false, ContractTypeNotFoundMessage);

        var terms = string.IsNullOrWhiteSpace(transaction.PendingContractTerms)
            ? null
            : JsonSerializer.Deserialize<ContractOfferTerms>(transaction.PendingContractTerms);
        var startDate = transaction.ExpectedStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        await repository.AddAsync(
            PendingContractProvisioning.CreatePending(
                transaction.EmployeeId,
                request.ContractTypeId,
                startDate,
                ProcessCandidateAcceptedHandler.PendingContractTermsText,
                terms),
            cancellationToken);
        transaction.PendingContractTerms = null;

        var item = await repository.HireChecklists.FirstOrDefaultAsync(
            c => c.ServiceTransactionId == transaction.Id && c.Step == HireStep.ContractClassified,
            cancellationToken);
        if (item is not null && !item.IsCompleted)
            item.Notes = "أُنشئ العقد المعلّق يدوياً بنوع عقد مختار";

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
