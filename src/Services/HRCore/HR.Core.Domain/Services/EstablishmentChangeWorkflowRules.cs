using HR.Core.Domain.Entities;

namespace HR.Core.Domain.Services;

public static class EstablishmentChangeWorkflowRules
{
    public static bool CanSubmit(EstablishmentChangeStatus status) =>
        status == EstablishmentChangeStatus.Draft;

    public static bool CanEdit(EstablishmentChangeStatus status) =>
        status == EstablishmentChangeStatus.Draft;

    public static bool CanCancel(EstablishmentChangeStatus status) =>
        status is EstablishmentChangeStatus.Draft or EstablishmentChangeStatus.PendingApproval;

    public static bool CanApply(EstablishmentChangeStatus status) =>
        status is EstablishmentChangeStatus.Approved or EstablishmentChangeStatus.PendingApproval;
}
