using HR.Movements.Domain.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Movements.Domain.Services;

public static class TransferWorkflowRules
{
    public static bool CanApprove(TransferRequest request) =>
        !request.IsCancelled && request.Status == ApprovalStatus.Pending;

    public static bool CanReject(TransferRequest request) =>
        !request.IsCancelled && request.Status == ApprovalStatus.Pending;

    public static bool CanExecute(TransferRequest request) =>
        !request.IsCancelled
        && request.Status == ApprovalStatus.Approved
        && MovementWorkflowRules.IsEffectiveDateReached(request.EffectiveDate);

    public static bool CanEdit(TransferRequest request) =>
        !request.IsCancelled && request.Status == ApprovalStatus.Pending;

    public static bool IsExecuted(TransferRequest request) =>
        request.Status == ApprovalStatus.Executed;
}
