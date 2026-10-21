using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Domain.Services;

public static class SupplyOrderWorkflowRules
{
    public static bool CanApprove(SupplyOrderStatus status) =>
        status == SupplyOrderStatus.Pending;

    public static bool CanApprove(SupplyOrder order, Guid? approverUserId) =>
        CanApprove(order.Status)
        && ApproverIsDistinctFromRequester(order.RequestedBy, approverUserId);

    public static bool CanEditContent(SupplyOrderStatus status) =>
        status is SupplyOrderStatus.Pending or SupplyOrderStatus.Approved;

    public static bool HasFulfillableContent(SupplyOrder order) =>
        order.Lines.Any(l => l.QtyRequested > 0)
        || !string.IsNullOrWhiteSpace(order.Items);

    public static bool CanFulfill(SupplyOrder order) =>
        order.Status == SupplyOrderStatus.Approved && HasFulfillableContent(order);

    public static bool CanCancel(SupplyOrderStatus status) =>
        status is SupplyOrderStatus.Pending or SupplyOrderStatus.Approved;

    public static bool CanClose(SupplyOrder order, int receiptCount) =>
        order.Status == SupplyOrderStatus.Fulfilled && (
            SupplyReceiptRules.IsFullyReceived(order)
            || (order.Lines.Count == 0 && receiptCount > 0 && !string.IsNullOrWhiteSpace(order.Items)));

    /// <summary>SOD-017: requester cannot approve own supply order.</summary>
    public static bool ApproverIsDistinctFromRequester(Guid? requestedBy, Guid? approverUserId) =>
        approverUserId.HasValue && (!requestedBy.HasValue || requestedBy.Value != approverUserId.Value);
}
