using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-001: workflow rules for recruitment requisitions (Plan-1: Draft CRUD only).</summary>
public static class RequisitionWorkflowRules
{
    public static readonly RequisitionStatus[] ActiveStatuses =
    [
        RequisitionStatus.Draft,
        RequisitionStatus.PendingApproval,
        RequisitionStatus.Approved
    ];

    public static bool CanCreate(FundedPosition position) =>
        position.Status == FundedPositionStatus.Vacant;

    public static bool CanEdit(RequisitionStatus status) =>
        status == RequisitionStatus.Draft;

    public static bool CanDelete(RequisitionStatus status) =>
        status == RequisitionStatus.Draft;

    public static bool ValidateVacancyCount(int vacancyCount) =>
        vacancyCount == 1;

    public static bool CanTransition(RequisitionStatus from, RequisitionStatus to) =>
        (from, to) switch
        {
            (RequisitionStatus.Draft, RequisitionStatus.PendingApproval) => true,
            (RequisitionStatus.Draft, RequisitionStatus.Cancelled) => true,
            (RequisitionStatus.PendingApproval, RequisitionStatus.Approved) => true,
            (RequisitionStatus.PendingApproval, RequisitionStatus.Draft) => true,
            (RequisitionStatus.PendingApproval, RequisitionStatus.Cancelled) => true,
            (RequisitionStatus.Approved, RequisitionStatus.Published) => true,
            (RequisitionStatus.Approved, RequisitionStatus.Cancelled) => true,
            (RequisitionStatus.Published, RequisitionStatus.Closed) => true,
            (RequisitionStatus.Published, RequisitionStatus.Cancelled) => true,
            _ => false
        };

    public static bool IsActiveStatus(RequisitionStatus status) =>
        ActiveStatuses.Contains(status);

    public static bool CanSubmit(RequisitionStatus status, FundedPosition position) =>
        status == RequisitionStatus.Draft && CanCreate(position);

    public static bool CanPublish(RequisitionStatus status, FundedPosition position, bool hasPublishedPosting) =>
        CanPublish(status, position, hasPublishedPosting, RecruitmentPublishingChannel.PublicOnly);

    public static bool CanPublish(
        RequisitionStatus status,
        FundedPosition position,
        bool hasPublishedPosting,
        RecruitmentPublishingChannel channel) =>
        status == RequisitionStatus.Approved
        && position.Status == FundedPositionStatus.Vacant
        && !hasPublishedPosting
        && AllowsPublicChannel(channel);

    public static bool CanPublishInternal(
        RequisitionStatus status,
        FundedPosition position,
        bool hasInternalVacancy,
        RecruitmentPublishingChannel channel) =>
        status is RequisitionStatus.Approved or RequisitionStatus.Published
        && position.Status == FundedPositionStatus.Vacant
        && !hasInternalVacancy
        && AllowsInternalChannel(channel);

    public static bool AllowsPublicChannel(RecruitmentPublishingChannel channel) =>
        channel is RecruitmentPublishingChannel.PublicOnly or RecruitmentPublishingChannel.Both;

    public static bool AllowsInternalChannel(RecruitmentPublishingChannel channel) =>
        channel is RecruitmentPublishingChannel.InternalOnly or RecruitmentPublishingChannel.Both;

    public static bool CanClose(RequisitionStatus status) =>
        status == RequisitionStatus.Published;

    public static bool CanCancel(RequisitionStatus status) =>
        status is RequisitionStatus.Draft or RequisitionStatus.PendingApproval;
}
