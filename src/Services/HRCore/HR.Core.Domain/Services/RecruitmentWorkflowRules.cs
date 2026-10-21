using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

public static class RecruitmentWorkflowRules
{
    public static bool CanTransition(RecruitmentApplication application, RecruitmentApplicationStatus to, bool legacyHireWithoutOffer = false) =>
        SelectionWorkflowRules.CanTransition(application, to, legacyHireWithoutOffer);

    public static bool CanTransition(RecruitmentApplicationStatus from, RecruitmentApplicationStatus to) =>
        SelectionWorkflowRules.CanTransition(
            new RecruitmentApplication { Status = from },
            to,
            legacyHireWithoutOffer: true);

    public static bool CanHire(RecruitmentApplication application, bool legacyHireWithoutOffer = false) =>
        CanHire(application.Status, application.RequisitionId, application.ActiveOfferLetter, legacyHireWithoutOffer);

    public static bool CanHire(RecruitmentApplicationStatus status) =>
        CanHire(status, requisitionId: null, activeOffer: null, legacyHireWithoutOffer: true);

    public static bool CanHire(
        RecruitmentApplicationStatus status,
        Guid? requisitionId,
        bool allowLegacyWithoutRequisition = true) =>
        CanHire(status, requisitionId, activeOffer: null, legacyHireWithoutOffer: allowLegacyWithoutRequisition);

    public static bool CanHire(
        RecruitmentApplicationStatus status,
        Guid? requisitionId,
        OfferLetter? activeOffer,
        bool legacyHireWithoutOffer = false)
    {
        if (status == RecruitmentApplicationStatus.Hired)
            return false;

        if (legacyHireWithoutOffer && requisitionId is null && status == RecruitmentApplicationStatus.Shortlisted)
            return true;

        if (status != RecruitmentApplicationStatus.OfferAccepted)
            return false;

        return activeOffer is { Status: OfferStatus.Accepted };
    }
}
