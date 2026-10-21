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

    /// <summary>
    /// HCM-026 (G-11): statuses that block a second requisition on the same funded position.
    /// Mirrored by the filtered unique index <c>IX_RecruitmentRequisitions_Tenant_Position_Blocking</c>.
    /// </summary>
    public static readonly RequisitionStatus[] BlockingStatuses =
    [
        RequisitionStatus.Draft,
        RequisitionStatus.PendingApproval,
        RequisitionStatus.Approved,
        RequisitionStatus.Published
    ];

    public static bool IsBlockingStatus(RequisitionStatus status) =>
        BlockingStatuses.Contains(status);

    /// <summary>HCM-026: cancelling an approved or published requisition needs HrCoreApprove and a reason.</summary>
    public static bool CancelRequiresApproval(RequisitionStatus status) =>
        status is RequisitionStatus.Approved or RequisitionStatus.Published;

    public const int MinCancellationReasonLength = 10;

    public const int MinClosingDateChangeReasonLength = 10;

    /// <summary>
    /// HCM-026 (E1): the application closing date is required to publish; at first publish it must leave at least
    /// <paramref name="minPostingDays"/> days of intake.
    /// </summary>
    public static string? ValidateClosingDateForPublish(
        DateOnly? closingDate,
        bool isFirstPublish,
        DateOnly todayUtc,
        int minPostingDays)
    {
        if (closingDate is null)
            return "يجب تحديد تاريخ إغلاق التقديم قبل النشر.";

        if (isFirstPublish && closingDate.Value < todayUtc.AddDays(Math.Max(0, minPostingDays)))
            return $"تاريخ إغلاق التقديم يجب ألا يقل عن {minPostingDays} أيام من اليوم.";

        return null;
    }

    /// <summary>HCM-026 (E3): extension only — a later date, not in the past, with a written reason.</summary>
    public static string? ValidateClosingDateExtension(
        DateOnly? currentClosingDate,
        DateOnly newClosingDate,
        DateOnly todayUtc,
        string? reason)
    {
        if (newClosingDate < todayUtc)
            return "تاريخ الإغلاق الجديد يجب ألا يكون في الماضي.";

        if (currentClosingDate is DateOnly current && newClosingDate <= current)
            return "يُسمح بتمديد تاريخ الإغلاق فقط (تاريخ لاحق للتاريخ الحالي).";

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinClosingDateChangeReasonLength)
            return $"سبب التمديد مطلوب ({MinClosingDateChangeReasonLength} أحرف على الأقل).";

        return null;
    }

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
        status is RequisitionStatus.Draft or RequisitionStatus.PendingApproval
            or RequisitionStatus.Approved or RequisitionStatus.Published;
}
