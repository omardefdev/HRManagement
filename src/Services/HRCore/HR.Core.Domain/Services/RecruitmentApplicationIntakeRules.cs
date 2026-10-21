using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-003: shared validation rules for applications against published requisitions.</summary>
public static class RecruitmentApplicationIntakeRules
{
    public static string DenormalizeJobTitle(RecruitmentRequisition requisition) =>
        string.IsNullOrWhiteSpace(requisition.JobTitle) ? "—" : requisition.JobTitle.Trim();

    public static bool CanAcceptApplication(RecruitmentRequisition requisition) =>
        requisition.Status is RequisitionStatus.Published;

    public static string? ValidatePublishedRequisition(
        RecruitmentRequisition? requisition,
        FundedPosition? fundedPosition,
        DateOnly today,
        DateOnly? postingClosingDate)
    {
        if (requisition is null)
            return "طلب الشاغر غير موجود.";

        if (requisition.Status != RequisitionStatus.Published)
            return "لا يمكن التقديم إلا على طلب شاغر منشور.";

        if (requisition.ClosedDate.HasValue)
            return "طلب الشاغر مغلق.";

        if (postingClosingDate is DateOnly closing && closing < today)
            return "انتهى موعد التقديم على هذا الإعلان.";

        if (fundedPosition is null)
            return "المنصب الممول المرتبط بطلب الشاغر غير موجود.";

        if (fundedPosition.Status != FundedPositionStatus.Vacant)
            return "المنصب الممول لم يعد شاغراً.";

        return null;
    }

    public static string? ValidateInternalApplication(
        RecruitmentRequisition? requisition,
        FundedPosition? fundedPosition,
        InternalVacancy? vacancy,
        DateOnly today)
    {
        var baseError = ValidatePublishedRequisition(requisition, fundedPosition, today, vacancy?.ClosingDate);
        if (baseError is not null)
            return baseError;

        if (vacancy is null || !vacancy.IsOpen)
            return "الشاغر الداخلي غير متاح.";

        if (!RequisitionWorkflowRules.AllowsInternalChannel(requisition!.PublishingChannel))
            return "طلب الشاغر غير مفتوح للترشّح الداخلي.";

        return null;
    }
}
