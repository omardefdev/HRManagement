using Compliance.Domain.Enums;

namespace Compliance.Domain.Services;

public static class DisciplinaryWorkflowRules
{
    public static bool CanIssue(AppealStatus status) =>
        status == AppealStatus.None;

    public static bool CanAppeal(AppealStatus status) =>
        status == AppealStatus.None;

    public static bool CanDecide(AppealStatus status) =>
        status == AppealStatus.Pending;

    public static bool IsSuspension(ActionType actionType) =>
        actionType == ActionType.Suspension;

    public static bool IsDismissal(ActionType actionType) =>
        actionType == ActionType.Dismissal;

    public static bool HasPayrollImpact(ActionType actionType) =>
        actionType is ActionType.Suspension or ActionType.Dismissal;
}
