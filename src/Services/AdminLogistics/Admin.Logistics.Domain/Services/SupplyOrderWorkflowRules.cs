using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Domain.Services;

public static class SupplyOrderWorkflowRules
{
    public static bool CanApprove(SupplyOrderStatus status) =>
        status == SupplyOrderStatus.Pending;

    public static bool CanFulfill(SupplyOrderStatus status) =>
        status == SupplyOrderStatus.Approved;

    public static bool CanCancel(SupplyOrderStatus status) =>
        status is SupplyOrderStatus.Pending or SupplyOrderStatus.Approved;
}
