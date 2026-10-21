using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-004: citizen application status rules before internal promote.</summary>
public static class CitizenSelectionWorkflowRules
{
    private static readonly Dictionary<CitizenApplicationStatus, HashSet<CitizenApplicationStatus>> AllowedTransitions = new()
    {
        [CitizenApplicationStatus.Received] = [CitizenApplicationStatus.UnderReview, CitizenApplicationStatus.Rejected],
        [CitizenApplicationStatus.UnderReview] = [CitizenApplicationStatus.Shortlisted, CitizenApplicationStatus.Rejected],
        [CitizenApplicationStatus.Shortlisted] = [CitizenApplicationStatus.Accepted, CitizenApplicationStatus.Rejected],
        [CitizenApplicationStatus.Accepted] = [],
        [CitizenApplicationStatus.Rejected] = []
    };

    public static bool CanTransition(CitizenApplication application, CitizenApplicationStatus to)
    {
        if (!AllowedTransitions.TryGetValue(application.Status, out var targets) || !targets.Contains(to))
            return false;

        if (to == CitizenApplicationStatus.Shortlisted
            && application.RequisitionId.HasValue
            && !application.ConflictOfInterestDeclared)
            return false;

        return true;
    }

    public static string? ValidateTransition(CitizenApplication application, CitizenApplicationStatus to)
    {
        if (!AllowedTransitions.TryGetValue(application.Status, out var targets) || !targets.Contains(to))
            return "انتقال الحالة غير مسموح.";

        if (to == CitizenApplicationStatus.Shortlisted
            && application.RequisitionId.HasValue
            && !application.ConflictOfInterestDeclared)
            return "يجب إقرار عدم تعارض المصالح قبل الإدراج في القائمة المختصرة.";

        return null;
    }
}
