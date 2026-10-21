using FluentValidation;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.SharedKernel.SeparationOfDuties;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Application.Features.AdvanceRequests;

public record GetAdvanceRequestsQuery : IRequest<IReadOnlyList<AdvanceRequest>>;
public record GetAdvanceRequestByIdQuery(Guid Id) : IRequest<AdvanceRequest?>;
public record CreateAdvanceRequestCommand(AdvanceRequestDto Model) : IRequest<Guid>;
public record UpdateAdvanceRequestCommand(Guid Id, AdvanceRequestDto Model) : IRequest<bool>;
public record DeleteAdvanceRequestCommand(Guid Id) : IRequest<bool>;
public record ApproveAdvanceRequestCommand(Guid Id, Guid? ApproverId) : IRequest<(bool Success, string? Error)>;
public record RejectAdvanceRequestCommand(Guid Id, Guid? ApproverId) : IRequest<(bool Success, string? Error)>;

public class AdvanceRequestDtoValidator : AbstractValidator<AdvanceRequestDto>
{
    public AdvanceRequestDtoValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public class GetAdvanceRequestsHandler(IContractsRepository repository) : IRequestHandler<GetAdvanceRequestsQuery, IReadOnlyList<AdvanceRequest>>
{
    public async Task<IReadOnlyList<AdvanceRequest>> Handle(GetAdvanceRequestsQuery request, CancellationToken cancellationToken) =>
        await repository.AdvanceRequests.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync(cancellationToken);
}

public class GetAdvanceRequestByIdHandler(IContractsRepository repository) : IRequestHandler<GetAdvanceRequestByIdQuery, AdvanceRequest?>
{
    public Task<AdvanceRequest?> Handle(GetAdvanceRequestByIdQuery request, CancellationToken cancellationToken) =>
        repository.GetAdvanceRequestAsync(request.Id, cancellationToken);
}

public class CreateAdvanceRequestHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient) : IRequestHandler<CreateAdvanceRequestCommand, Guid>
{
    public async Task<Guid> Handle(CreateAdvanceRequestCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        var advances = await repository.AdvanceRequests.AsNoTracking().Where(x => x.EmployeeId == vm.EmployeeId).ToListAsync(cancellationToken);
        var error = LeaveAndAdvanceRules.ValidateAdvanceRequest(vm.Amount, LeaveAndAdvanceRules.DefaultMaxSingleAdvance, advances, null);
        if (error is not null) throw new ValidationException(error);

        // Always Pending with no approver, whatever the caller supplied — otherwise a requester
        // could file an already-approved advance and trigger disbursement without review.
        var entity = new AdvanceRequest
        {
            EmployeeId = vm.EmployeeId,
            Amount = vm.Amount,
            Reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim(),
            Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Pending,
            ApprovedBy = null
        };

        var transaction = new ServiceTransaction
        {
            EmployeeId = vm.EmployeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance,
            Notes = "تسجيل طلب سلفة"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
        await repository.AddAsync(entity, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        entity.ServiceTransactionId = transaction.Id;
        await repository.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public class UpdateAdvanceRequestHandler(IContractsRepository repository) : IRequestHandler<UpdateAdvanceRequestCommand, bool>
{
    public async Task<bool> Handle(UpdateAdvanceRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetAdvanceRequestAsync(request.Id, cancellationToken);
        if (entity is null) return false;

        var vm = request.Model;
        var advances = await repository.AdvanceRequests.AsNoTracking().Where(x => x.EmployeeId == vm.EmployeeId).ToListAsync(cancellationToken);
        var error = LeaveAndAdvanceRules.ValidateAdvanceRequest(vm.Amount, LeaveAndAdvanceRules.DefaultMaxSingleAdvance, advances, request.Id);
        if (error is not null) throw new ValidationException(error);

        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Pending)
            throw new ValidationException("لا يمكن تعديل طلب سلفة بعد البتّ فيه.");

        entity.EmployeeId = vm.EmployeeId;
        entity.Amount = vm.Amount;
        entity.Reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim();
        // Status and ApprovedBy stay under the approval command's control.
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteAdvanceRequestHandler(IContractsRepository repository) : IRequestHandler<DeleteAdvanceRequestCommand, bool>
{
    public async Task<bool> Handle(DeleteAdvanceRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetAdvanceRequestAsync(request.Id, cancellationToken);
        if (entity is null) return false;
        repository.Remove(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class ApproveAdvanceRequestHandler(IContractsRepository repository, IEventPublisher eventPublisher) : IRequestHandler<ApproveAdvanceRequestCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(ApproveAdvanceRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetAdvanceRequestAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Pending) return (false, "الطلب ليس قيد الانتظار");
        if (request.ApproverId is null) return (false, "تعذر تحديد المعتمد");

        var sod = new SeparationOfDutiesService();
        var sodResult = sod.ValidateAction(
            request.ApproverId.Value,
            null,
            "AdvanceRequest",
            entity.Id,
            SodAction.Check,
            entity.CreatedBy,
            null,
            entity.Amount);
        if (!sodResult.Allowed)
            return (false, sodResult.DenialReason);

        var advances = await repository.AdvanceRequests.AsNoTracking()
            .Where(x => x.EmployeeId == entity.EmployeeId)
            .ToListAsync(cancellationToken);
        var plans = await repository.AdvanceInstallmentPlans.AsNoTracking()
            .Where(p => advances.Select(a => a.Id).Contains(p.AdvanceRequestId))
            .ToListAsync(cancellationToken);

        var openError = LeaveAndAdvanceRules.ValidateNoOpenAdvance(advances, plans, entity.Id);
        if (openError is not null) return (false, openError);

        entity.Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Approved;
        entity.ApprovedBy = request.ApproverId;

        var installmentPlan = LeaveAndAdvanceRules.CreateInstallmentPlan(entity.Id, entity.Amount);
        await repository.AddAsync(installmentPlan, cancellationToken);

        var transaction = entity.ServiceTransactionId.HasValue
            ? await repository.GetServiceTransactionAsync(entity.ServiceTransactionId.Value, cancellationToken)
            : await repository.ServiceTransactions
                .Where(x => x.EmployeeId == entity.EmployeeId && x.TransactionType == ServiceTransactionType.LeaveAdvance)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

        if (transaction is null)
            return (false, "لم يُعثر على معاملة الخدمة المرتبطة بطلب السلفة.");

        ServiceTransactionWorkflowHelper.AdvanceToCompleted(transaction, request.ApproverId, "اعتماد طلب سلفة");
        await repository.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAdvanceApprovedAsync(
            transaction.Id,
            entity.Id,
            entity.EmployeeId,
            entity.Amount,
            request.ApproverId.Value,
            correlationId: null,
            cancellationToken,
            monthlyInstallment: installmentPlan.MonthlyInstallment,
            numberOfInstallments: installmentPlan.NumberOfInstallments);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public class RejectAdvanceRequestHandler(IContractsRepository repository) : IRequestHandler<RejectAdvanceRequestCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(RejectAdvanceRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetAdvanceRequestAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Pending) return (false, "الطلب ليس قيد الانتظار");

        entity.Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Rejected;
        entity.ApprovedBy = request.ApproverId;

        if (entity.ServiceTransactionId.HasValue)
        {
            var transaction = await repository.GetServiceTransactionAsync(entity.ServiceTransactionId.Value, cancellationToken);
            if (transaction is not null)
            {
                if (transaction.State == ServiceTransactionState.PendingApproval)
                    ServiceTransactionWorkflowHelper.AdvanceToRejected(transaction, request.ApproverId, "رفض طلب سلفة");
                else
                    ServiceTransactionWorkflowHelper.AdvanceToCancelled(transaction, request.ApproverId, "رفض طلب سلفة");
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
