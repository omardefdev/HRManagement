using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-005: decrement vacancy count and close requisition when exhausted.</summary>
public class RequisitionCloseService
{
    public void DecrementVacancy(RecruitmentRequisition requisition)
    {
        requisition.VacancyCount = Math.Max(0, requisition.VacancyCount - 1);
    }

    public bool TryClose(RecruitmentRequisition requisition)
    {
        if (requisition.VacancyCount > 0)
            return false;

        if (requisition.Status == RequisitionStatus.Closed)
            return false;

        requisition.Status = RequisitionStatus.Closed;
        requisition.ClosedDate = DateTime.UtcNow;
        return true;
    }

    public void UnpublishPosting(PublicJobPosting? posting)
    {
        if (posting is null)
            return;

        posting.IsPublished = false;
    }

    public void CloseInternalVacancy(InternalVacancy? vacancy)
    {
        if (vacancy is null)
            return;

        vacancy.IsOpen = false;
    }
}
