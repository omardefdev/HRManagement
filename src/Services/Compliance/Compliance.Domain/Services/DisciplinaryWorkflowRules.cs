using Compliance.Domain.Enums;

namespace Compliance.Domain.Services;

public static class DisciplinaryWorkflowRules
{
    public static bool CanIssue(AppealStatus status) =>
        status == AppealStatus.None;

    /// <summary>PLAN-46: only an approved action is appealed.</summary>
    public static bool CanAppeal(AppealStatus status, DisciplinaryApprovalStatus approval) =>
        approval == DisciplinaryApprovalStatus.Approved && status == AppealStatus.None;

    public static bool CanDecide(AppealStatus status, DisciplinaryApprovalStatus approval) =>
        approval == DisciplinaryApprovalStatus.Approved && status == AppealStatus.Pending;

    /// <summary>PLAN-46: the employee and the type change only before the approval, or after a rejection.</summary>
    public static bool CanEdit(DisciplinaryApprovalStatus approval) =>
        approval is DisciplinaryApprovalStatus.Draft or DisciplinaryApprovalStatus.Rejected;

    /// <summary>PLAN-46: a rejected action is corrected and submitted again under the next number.</summary>
    public static bool CanSubmit(DisciplinaryApprovalStatus approval) =>
        approval is DisciplinaryApprovalStatus.Draft or DisciplinaryApprovalStatus.Rejected;

    public static bool CanWithdraw(DisciplinaryApprovalStatus approval) =>
        approval == DisciplinaryApprovalStatus.PendingApproval;

    public static bool IsSuspension(ActionType actionType) =>
        actionType == ActionType.Suspension;

    public static bool IsDismissal(ActionType actionType) =>
        actionType == ActionType.Dismissal;

    public static bool HasPayrollImpact(ActionType actionType) =>
        actionType is ActionType.Suspension or ActionType.Dismissal;
}
