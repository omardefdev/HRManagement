using FluentValidation;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.SharedKernel.SeparationOfDuties;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Application.Features.LeaveRequests;

public record GetLeaveRequestsQuery : IRequest<IReadOnlyList<LeaveRequest>>;
public record GetLeaveRequestByIdQuery(Guid Id) : IRequest<LeaveRequest?>;
public record CreateLeaveRequestCommand(LeaveRequestDto Model) : IRequest<Guid>;
public record UpdateLeaveRequestCommand(Guid Id, LeaveRequestDto Model) : IRequest<bool>;
public record DeleteLeaveRequestCommand(Guid Id) : IRequest<bool>;
public record CancelLeaveRequestCommand(Guid Id, Guid? CancelledBy) : IRequest<(bool Success, string? Error)>;
public record ApproveLeaveRequestCommand(Guid Id, Guid? ApproverId) : IRequest<(bool Success, string? Error)>;
public record RejectLeaveRequestCommand(Guid Id, Guid? ApproverId) : IRequest<(bool Success, string? Error)>;

public class LeaveRequestDtoValidator : AbstractValidator<LeaveRequestDto>
{
    public LeaveRequestDtoValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
    }
}

public class GetLeaveRequestsHandler(IContractsRepository repository) : IRequestHandler<GetLeaveRequestsQuery, IReadOnlyList<LeaveRequest>>
{
    public async Task<IReadOnlyList<LeaveRequest>> Handle(GetLeaveRequestsQuery request, CancellationToken cancellationToken) =>
        await repository.LeaveRequests.AsNoTracking().OrderByDescending(x => x.StartDate).Take(SearchFilterBase.MaxPageSize).ToListAsync(cancellationToken);
}

public class GetLeaveRequestByIdHandler(IContractsRepository repository) : IRequestHandler<GetLeaveRequestByIdQuery, LeaveRequest?>
{
    public Task<LeaveRequest?> Handle(GetLeaveRequestByIdQuery request, CancellationToken cancellationToken) =>
        repository.GetLeaveRequestAsync(request.Id, cancellationToken);
}

public class CreateLeaveRequestHandler(IContractsRepository repository) : IRequestHandler<CreateLeaveRequestCommand, Guid>
{
    public async Task<Guid> Handle(CreateLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        var durationDays = LeaveAndAdvanceRules.ComputeLeaveDurationDays(vm.StartDate, vm.EndDate);
        var peerLeaves = await repository.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == vm.EmployeeId).ToListAsync(cancellationToken);
        var leaveError = LeaveAndAdvanceRules.ValidateLeaveRequest(vm.EmployeeId, vm.StartDate, vm.EndDate, durationDays, peerLeaves, null);
        if (leaveError is not null)
        {
            throw new ValidationException(leaveError);
        }

        // A new request is always Pending with no approver, whatever the caller supplied.
        // Taking these from the request allowed a self-approved leave that debited no balance.
        var entity = new LeaveRequest
        {
            EmployeeId = vm.EmployeeId,
            LeaveType = vm.LeaveType,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            DurationDays = durationDays,
            Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Pending,
            ApprovedBy = null,
            Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim()
        };

        var transaction = new ServiceTransaction
        {
            EmployeeId = vm.EmployeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance,
            Notes = "تسجيل طلب إجازة"
        };
        await repository.AddAsync(entity, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        entity.ServiceTransactionId = transaction.Id;
        await repository.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public class UpdateLeaveRequestHandler(IContractsRepository repository) : IRequestHandler<UpdateLeaveRequestCommand, bool>
{
    public async Task<bool> Handle(UpdateLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetLeaveRequestAsync(request.Id, cancellationToken);
        if (entity is null) return false;

        var vm = request.Model;
        var durationDays = LeaveAndAdvanceRules.ComputeLeaveDurationDays(vm.StartDate, vm.EndDate);
        var peerLeaves = await repository.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == vm.EmployeeId).ToListAsync(cancellationToken);
        var leaveError = LeaveAndAdvanceRules.ValidateLeaveRequest(vm.EmployeeId, vm.StartDate, vm.EndDate, durationDays, peerLeaves, request.Id);
        if (leaveError is not null)
        {
            throw new ValidationException(leaveError);
        }

        // Editing details of a decided request would desynchronise the leave balance that the
        // approval already debited, so detail edits are confined to Pending requests.
        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Pending)
            throw new ValidationException("لا يمكن تعديل طلب إجازة بعد البتّ فيه.");

        entity.EmployeeId = vm.EmployeeId;
        entity.LeaveType = vm.LeaveType;
        entity.StartDate = vm.StartDate;
        entity.EndDate = vm.EndDate;
        entity.DurationDays = durationDays;
        // Status and ApprovedBy stay under the approval command's control.
        entity.Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim();

        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteLeaveRequestHandler(IContractsRepository repository) : IRequestHandler<DeleteLeaveRequestCommand, bool>
{
    public async Task<bool> Handle(DeleteLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetLeaveRequestAsync(request.Id, cancellationToken);
        if (entity is null) return false;

        // Deleting an approved leave silently stranded the balance days it had already consumed.
        // Reversing an approved leave has to go through a cancellation that credits the balance.
        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Pending)
            throw new ValidationException("لا يمكن حذف طلب إجازة بعد البتّ فيه؛ استخدم الإلغاء لإعادة الرصيد.");

        repository.Remove(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class CancelLeaveRequestHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<CancelLeaveRequestCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(CancelLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetLeaveRequestAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Approved)
            return (false, "يمكن إلغاء طلبات الإجازة المعتمدة فقط.");
        if (request.CancelledBy is null)
            return (false, "تعذر تحديد المستخدم المنفّذ للإلغاء.");

        var balance = await repository.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == entity.EmployeeId
                && b.LeaveType == entity.LeaveType
                && b.Year == entity.StartDate.Year, cancellationToken);

        var claimed = await repository.LeaveRequests
            .Where(x => x.Id == entity.Id && x.Status == HRManagement.SharedKernel.Enums.ApprovalStatus.Approved)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, HRManagement.SharedKernel.Enums.ApprovalStatus.Cancelled)
                    .SetProperty(x => x.ApprovedBy, request.CancelledBy)
                    .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken);
        if (claimed == 0)
            return (false, "الطلب ليس معتمداً أو تم إلغاؤه بالفعل.");

        entity.Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Cancelled;
        entity.ApprovedBy = request.CancelledBy;

        await CreditBalanceBackAsync(repository, balance, entity.DurationDays, cancellationToken);

        if (entity.ServiceTransactionId.HasValue)
        {
            var transaction = await repository.GetServiceTransactionAsync(entity.ServiceTransactionId.Value, cancellationToken);
            if (transaction is not null)
                ServiceTransactionWorkflowHelper.AdvanceToCancelled(transaction, request.CancelledBy, "إلغاء طلب إجازة معتمد");
        }

        await repository.SaveChangesAsync(cancellationToken);

        var transactionId = entity.ServiceTransactionId ?? Guid.Empty;
        await eventPublisher.PublishLeaveCancelledAsync(
            transactionId,
            entity.Id,
            entity.EmployeeId,
            request.CancelledBy.Value,
            correlationId: null,
            cancellationToken,
            leaveType: entity.LeaveType.ToString(),
            startDate: entity.StartDate,
            endDate: entity.EndDate,
            durationDays: entity.DurationDays);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    private static async Task CreditBalanceBackAsync(
        IContractsRepository repository,
        LeaveBalance? balance,
        int days,
        CancellationToken cancellationToken)
    {
        if (balance is null || days <= 0) return;

        await repository.LeaveBalances
            .Where(b => b.Id == balance.Id)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(b => b.Used, b => Math.Max(0, b.Used - days))
                    .SetProperty(b => b.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken);
    }
}

public class ApproveLeaveRequestHandler(IContractsRepository repository, IEventPublisher eventPublisher) : IRequestHandler<ApproveLeaveRequestCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(ApproveLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetLeaveRequestAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (entity.Status != HRManagement.SharedKernel.Enums.ApprovalStatus.Pending) return (false, "الطلب ليس قيد الانتظار");
        if (request.ApproverId is null) return (false, "تعذر تحديد المعتمد");

        var sod = new SeparationOfDutiesService();
        var sodResult = sod.ValidateAction(
            request.ApproverId.Value,
            null,
            "LeaveRequest",
            entity.Id,
            SodAction.Check,
            entity.CreatedBy,
            null);
        if (!sodResult.Allowed)
            return (false, sodResult.DenialReason);

        var maxDurError = LeaveAndAdvanceRules.ValidateMaxDuration(entity.LeaveType, entity.DurationDays);
        if (maxDurError is not null) return (false, maxDurError);

        var policy = await repository.LeavePolicies
            .FirstOrDefaultAsync(p => p.LeaveType == entity.LeaveType, cancellationToken);
        var policyError = LeaveAndAdvanceRules.ValidateLeavePolicy(policy, entity.LeaveType, DateOnly.FromDateTime(DateTime.UtcNow), entity.StartDate);
        if (policyError is not null) return (false, policyError);

        var balance = await repository.LeaveBalances
            .FirstOrDefaultAsync(b => b.EmployeeId == entity.EmployeeId
                && b.LeaveType == entity.LeaveType
                && b.Year == entity.StartDate.Year, cancellationToken);

        var balanceError = LeaveAndAdvanceRules.ValidateLeaveBalance(balance, entity.DurationDays);
        if (balanceError is not null) return (false, balanceError);

        // DATA-001: claim Pending→Approved atomically before debiting balance.
        var claimed = await repository.LeaveRequests
            .Where(x => x.Id == entity.Id && x.Status == HRManagement.SharedKernel.Enums.ApprovalStatus.Pending)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, HRManagement.SharedKernel.Enums.ApprovalStatus.Approved)
                    .SetProperty(x => x.ApprovedBy, request.ApproverId)
                    .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken);
        if (claimed == 0)
            return (false, "الطلب ليس قيد الانتظار");

        entity.Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Approved;
        entity.ApprovedBy = request.ApproverId;

        if (balance is not null)
        {
            // DATA-001: atomic debit — concurrent approvals cannot overdraw.
            var rows = await repository.LeaveBalances
                .Where(b => b.Id == balance.Id && b.Used + entity.DurationDays <= b.TotalEntitlement)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(b => b.Used, b => b.Used + entity.DurationDays)
                        .SetProperty(b => b.UpdatedAt, _ => DateTime.UtcNow),
                    cancellationToken);
            if (rows == 0)
            {
                // Approval and debit have to succeed or fail together. Returning here without
                // undoing the claim left the request Approved while no balance had been consumed.
                await RevertApprovalClaimAsync(repository, entity, cancellationToken);
                return (false, "الرصيد أو الكمية غير كافية أو حدث تعارض تزامن.");
            }
        }

        var transaction = entity.ServiceTransactionId.HasValue
            ? await repository.GetServiceTransactionAsync(entity.ServiceTransactionId.Value, cancellationToken)
            : await repository.ServiceTransactions
                .Where(x => x.EmployeeId == entity.EmployeeId && x.TransactionType == ServiceTransactionType.LeaveAdvance)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

        if (transaction is null)
        {
            // Undo both the claim and the debit; otherwise the employee loses balance days for a
            // leave the workflow never completed.
            await CreditBalanceBackAsync(repository, balance, entity.DurationDays, cancellationToken);
            await RevertApprovalClaimAsync(repository, entity, cancellationToken);
            return (false, "لم يُعثر على معاملة الخدمة المرتبطة بطلب الإجازة.");
        }

        ServiceTransactionWorkflowHelper.AdvanceToCompleted(transaction, request.ApproverId, "اعتماد طلب إجازة");
        await repository.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishLeaveApprovedAsync(
            transaction.Id,
            entity.Id,
            entity.EmployeeId,
            request.ApproverId.Value,
            correlationId: null,
            cancellationToken,
            leaveType: entity.LeaveType.ToString(),
            startDate: entity.StartDate,
            endDate: entity.EndDate,
            durationDays: entity.DurationDays);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    /// <summary>Puts a claimed request back to Pending after a later step in the approval failed.</summary>
    private static async Task RevertApprovalClaimAsync(
        IContractsRepository repository,
        LeaveRequest entity,
        CancellationToken cancellationToken)
    {
        await repository.LeaveRequests
            .Where(x => x.Id == entity.Id && x.Status == HRManagement.SharedKernel.Enums.ApprovalStatus.Approved)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Status, HRManagement.SharedKernel.Enums.ApprovalStatus.Pending)
                    .SetProperty(x => x.ApprovedBy, (Guid?)null)
                    .SetProperty(x => x.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken);

        entity.Status = HRManagement.SharedKernel.Enums.ApprovalStatus.Pending;
        entity.ApprovedBy = null;
    }

    private static async Task CreditBalanceBackAsync(
        IContractsRepository repository,
        LeaveBalance? balance,
        int days,
        CancellationToken cancellationToken)
    {
        if (balance is null || days <= 0) return;

        await repository.LeaveBalances
            .Where(b => b.Id == balance.Id && b.Used >= days)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(b => b.Used, b => b.Used - days)
                    .SetProperty(b => b.UpdatedAt, _ => DateTime.UtcNow),
                cancellationToken);
    }
}

public class RejectLeaveRequestHandler(IContractsRepository repository) : IRequestHandler<RejectLeaveRequestCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(RejectLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetLeaveRequestAsync(request.Id, cancellationToken);
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
                    ServiceTransactionWorkflowHelper.AdvanceToRejected(transaction, request.ApproverId, "رفض طلب إجازة");
                else
                    ServiceTransactionWorkflowHelper.AdvanceToCancelled(transaction, request.ApproverId, "رفض طلب إجازة");
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
