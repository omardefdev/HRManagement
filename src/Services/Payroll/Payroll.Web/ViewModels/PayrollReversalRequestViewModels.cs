using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>PLAN-56 (G-PAY-06): a reversal request as «طلبات العكس» shows it: the run by its period, people by their names.</summary>
public sealed record PayrollReversalRequestRow(
    Guid Id,
    Guid PayrollRunId,
    DateTime PeriodStart,
    PayrollRunType RunType,
    PayrollRunStatus RunStatus,
    decimal TotalNetPay,
    Guid? PostedBy,
    Guid RequestedBy,
    DateTime RequestedAt,
    string Reason,
    PayrollReversalRequestStatus Status,
    Guid? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNotes);

public sealed record PayrollReversalRequestListViewModel(
    IReadOnlyList<PayrollReversalRequestRow> Items,
    PayrollReversalRequestStatus? Status,
    bool CanDecide,
    Guid? CurrentUserId)
{
    /// <summary>
    /// Whether this user is shown the decision for a row: he holds the approval policy, the request waits, and he is neither its
    /// requester nor the run's poster (the handler refuses those anyway; the page does not offer what it would refuse).
    /// </summary>
    public bool MayDecide(PayrollReversalRequestRow row) =>
        CanDecide && row.Status == PayrollReversalRequestStatus.Pending
        && CurrentUserId is Guid me && me != row.RequestedBy && me != row.PostedBy;
}
