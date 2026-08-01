using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

public static class RecruitmentWorkflowRules
{
    private static readonly Dictionary<RecruitmentApplicationStatus, HashSet<RecruitmentApplicationStatus>> AllowedTransitions = new()
    {
        [RecruitmentApplicationStatus.Submitted] = [RecruitmentApplicationStatus.UnderReview, RecruitmentApplicationStatus.Rejected],
        [RecruitmentApplicationStatus.UnderReview] = [RecruitmentApplicationStatus.Shortlisted, RecruitmentApplicationStatus.Rejected],
        [RecruitmentApplicationStatus.Shortlisted] = [RecruitmentApplicationStatus.Hired, RecruitmentApplicationStatus.Rejected],
        [RecruitmentApplicationStatus.Rejected] = [],
        [RecruitmentApplicationStatus.Hired] = []
    };

    public static bool CanTransition(RecruitmentApplicationStatus from, RecruitmentApplicationStatus to)
    {
        if (from == to) return true;
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    public static bool CanHire(RecruitmentApplicationStatus status) =>
        status == RecruitmentApplicationStatus.Shortlisted;
}
