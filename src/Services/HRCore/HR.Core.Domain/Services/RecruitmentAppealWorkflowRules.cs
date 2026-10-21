using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-006: appeal and committee gates for recruitment selection.</summary>
public static class RecruitmentAppealWorkflowRules
{
    public static bool HasPendingAppeal(RecruitmentApplication application) =>
        application.AppealStatus == RecruitmentAppealStatus.Pending;

    public static bool CanRank(RecruitmentApplication application) =>
        !HasPendingAppeal(application);

    public static bool CanOffer(RecruitmentApplication application) =>
        !HasPendingAppeal(application);

    public static bool CanAppeal(
        RecruitmentApplication application,
        DateTime utcNow,
        int appealWindowDays = 14)
    {
        if (application.Status != RecruitmentApplicationStatus.Rejected)
            return false;

        if (application.AppealStatus == RecruitmentAppealStatus.Pending)
            return false;

        if (application.AppealStatus == RecruitmentAppealStatus.Decided)
            return false;

        if (application.ReviewedAt is null)
            return true;

        return utcNow <= application.ReviewedAt.Value.AddDays(appealWindowDays);
    }

    public static bool CanDecideAppeal(RecruitmentAppeal appeal, Guid? actorUserId) =>
        appeal.Status == RecruitmentAppealStatus.Pending
        && actorUserId.HasValue
        && appeal.IndependentReviewerId == actorUserId;

    public static bool IsIndependentReviewer(
        IEnumerable<SelectionCommitteeMember> sessionMembers,
        Guid reviewerId) =>
        sessionMembers.All(m => m.EmployeeId != reviewerId || m.Recused);

    public static bool HasQuorum(IEnumerable<SelectionCommitteeMember> members, int minimum = 3)
    {
        var active = members.Count(m => !m.Recused);
        return active >= minimum;
    }
}
