using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-030 (B2): an interview as the candidate may see it (no panel, no notes).</summary>
public sealed record CandidateInterviewInput(DateTime ScheduledAtUtc, InterviewMode Mode, string? Location, InterviewSlotStatus Status);

/// <summary>HCM-030 (B2): an application event as the projection needs it (type, statuses, time).</summary>
public sealed record CandidateEventInput(string EventType, int? FromStatus, int? ToStatus, DateTime OccurredAtUtc);

/// <summary>
/// HCM-030 (B2): what the projection reads. Deliberately free of rank, scores, committee data, panel names and notes,
/// so the public view cannot leak them.
/// </summary>
public sealed record CandidatePublicStatusInput
{
    public required RecruitmentApplicationStatus Status { get; init; }
    public required DateTime AppliedAtUtc { get; init; }
    public RecruitmentAppealStatus AppealStatus { get; init; }
    public DateOnly? ReserveUntil { get; init; }

    /// <summary>Status of <c>RecruitmentApplication.ActiveOfferLetter</c> (null when none).</summary>
    public OfferStatus? ActiveOfferStatus { get; init; }
    public DateTime? ActiveOfferExpiryUtc { get; init; }

    public IReadOnlyList<CandidateInterviewInput> Interviews { get; init; } = [];

    /// <summary>From <see cref="CandidatePublicStatusProjection.AppealWindowOf"/>: the appeal can be filed now.</summary>
    public bool AppealOpen { get; init; }
    public DateTime? AppealDeadlineUtc { get; init; }

    public IReadOnlyList<CandidateEventInput> Events { get; init; } = [];
    public required DateTime NowUtc { get; init; }

    /// <summary>Zone used for the dates in the Arabic labels (default UTC).</summary>
    public TimeZoneInfo? DisplayZone { get; init; }
}

/// <summary>HCM-030 (B2): one public step of the candidate timeline.</summary>
public sealed record CandidatePublicTimelineStep(string Code, string LabelAr, bool Reached, DateTime? AtUtc, string? DetailAr);

/// <summary>HCM-030 (B2): next scheduled interview, as shown to the candidate.</summary>
public sealed record CandidatePublicInterview(DateTime ScheduledAtUtc, string ModeAr, string? Location);

/// <summary>
/// HCM-030 (B2): the candidate-facing status (session 3 contract). <see cref="Code"/> is stable; the Arabic texts may
/// change. <see cref="DeadlineUtc"/> is the offer expiry, reserve end or appeal deadline behind the label.
/// </summary>
public sealed record CandidatePublicView(
    string Code,
    string LabelAr,
    string? DetailAr,
    DateTime? DeadlineUtc,
    bool CanRespondToOffer,
    bool CanAppeal,
    DateTime? AppealOpenUntil,
    bool CanWithdraw,
    CandidatePublicInterview? NextInterview,
    IReadOnlyList<CandidatePublicTimelineStep> Timeline);

/// <summary>
/// HCM-030 (B2): maps the internal recruitment state to what the candidate sees on <c>/careers/track</c> and ESS.
/// An offer counts only once issued (<c>OfferExtended</c> is set when the draft is created). A timeline step is marked
/// reached only when it happened: from the application events, or — for rows without events — from the current status.
/// </summary>
public static class CandidatePublicStatusProjection
{
    public static class Codes
    {
        public const string UnderReview = "UnderReview";
        public const string InterviewInvited = "InterviewInvited";
        public const string FinalEvaluation = "FinalEvaluation";
        public const string OfferPending = "OfferPending";
        public const string Accepted = "Accepted";
        public const string ReserveListed = "ReserveListed";
        public const string OfferClosed = "OfferClosed";
        public const string NotAccepted = "NotAccepted";
        public const string Withdrawn = "Withdrawn";
    }

    public static class Steps
    {
        public const string Received = "Received";
        public const string Review = "Review";
        public const string Interview = "Interview";
        public const string FinalEvaluation = "FinalEvaluation";
        public const string Offer = "Offer";
        public const string Appeal = "Appeal";
        public const string Decision = "Decision";
    }

    /// <summary>Appeal window of a rejected application, from <see cref="RecruitmentAppealWorkflowRules.GetAppealError"/>.</summary>
    public static (bool Open, DateTime? DeadlineUtc) AppealWindowOf(
        RecruitmentApplication application,
        DateTime utcNow,
        int appealWindowDays = RecruitmentAppealWorkflowRules.DefaultAppealWindowDays,
        IReadOnlyCollection<RecruitmentRejectionKind>? appealableKinds = null)
    {
        if (RecruitmentAppealWorkflowRules.GetAppealError(application, utcNow, appealWindowDays, appealableKinds) is not null)
            return (false, null);
        var rejectedAt = application.RejectedAt ?? application.ReviewedAt;
        return (true, rejectedAt?.AddDays(appealWindowDays));
    }

    public static CandidatePublicView Project(CandidatePublicStatusInput input)
    {
        var zone = input.DisplayZone ?? TimeZoneInfo.Utc;
        var offerIssued = input.ActiveOfferStatus is OfferStatus.Issued;
        var nextInterview = input.Interviews
            .Where(i => i.Status == InterviewSlotStatus.Scheduled && i.ScheduledAtUtc >= input.NowUtc)
            .OrderBy(i => i.ScheduledAtUtc)
            .Select(i => new CandidatePublicInterview(i.ScheduledAtUtc, ModeAr(i.Mode), i.Location))
            .FirstOrDefault();

        string code, label;
        string? detail = null;
        DateTime? deadline = null;
        switch (input.Status)
        {
            case RecruitmentApplicationStatus.Submitted or RecruitmentApplicationStatus.UnderReview:
                (code, label) = (Codes.UnderReview, "قيد المراجعة");
                break;
            case RecruitmentApplicationStatus.Shortlisted or RecruitmentApplicationStatus.InterviewScheduled:
                (code, label) = (Codes.InterviewInvited, "مدعو للمقابلة");
                if (nextInterview is not null)
                {
                    detail = $"موعد المقابلة: {FormatDateTime(nextInterview.ScheduledAtUtc, zone)} — {nextInterview.ModeAr}"
                        + (string.IsNullOrWhiteSpace(nextInterview.Location) ? "" : $" — {nextInterview.Location}");
                    deadline = nextInterview.ScheduledAtUtc;
                }
                break;
            case RecruitmentApplicationStatus.OfferExtended when offerIssued:
                (code, label) = (Codes.OfferPending, input.ActiveOfferExpiryUtc is DateTime expiry
                    ? $"عرض بانتظار ردك حتى {FormatDate(expiry, zone)}"
                    : "عرض بانتظار ردك");
                deadline = input.ActiveOfferExpiryUtc;
                break;
            // A draft or pending-approval offer is not an offer yet for the candidate.
            case RecruitmentApplicationStatus.Assessed or RecruitmentApplicationStatus.Ranked
                or RecruitmentApplicationStatus.OfferExtended:
                (code, label) = (Codes.FinalEvaluation, "قيد التقييم النهائي");
                break;
            case RecruitmentApplicationStatus.OfferAccepted or RecruitmentApplicationStatus.Hired:
                (code, label) = (Codes.Accepted, "مقبول — بانتظار استكمال التعيين");
                break;
            case RecruitmentApplicationStatus.ReserveListed:
                (code, label) = (Codes.ReserveListed, input.ReserveUntil is DateOnly until
                    ? $"في قائمة الاحتياط حتى {until.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}"
                    : "في قائمة الاحتياط");
                deadline = input.ReserveUntil?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
                break;
            case RecruitmentApplicationStatus.OfferDeclined:
                (code, label) = (Codes.OfferClosed, "العرض منتهٍ/مرفوض");
                break;
            case RecruitmentApplicationStatus.Rejected:
                (code, label) = (Codes.NotAccepted, "غير مقبول");
                if (input.AppealStatus == RecruitmentAppealStatus.Pending)
                    detail = "استئنافك قيد المراجعة";
                else if (input.AppealOpen)
                {
                    detail = input.AppealDeadlineUtc is DateTime appealUntil
                        ? $"يمكنك الاستئناف حتى {FormatDate(appealUntil, zone)}"
                        : "يمكنك الاستئناف";
                    deadline = input.AppealDeadlineUtc;
                }
                break;
            default:
                (code, label) = (Codes.Withdrawn, "منسحب");
                break;
        }

        var canAppeal = input.Status == RecruitmentApplicationStatus.Rejected
            && input.AppealStatus != RecruitmentAppealStatus.Pending
            && input.AppealOpen;
        return new CandidatePublicView(
            code,
            label,
            detail,
            deadline,
            CanRespondToOffer: input.Status == RecruitmentApplicationStatus.OfferExtended && offerIssued
                && (input.ActiveOfferExpiryUtc is null || input.ActiveOfferExpiryUtc > input.NowUtc),
            CanAppeal: canAppeal,
            AppealOpenUntil: canAppeal ? input.AppealDeadlineUtc : null,
            CanWithdraw: !SelectionWorkflowRules.TerminalStatuses.Contains(input.Status),
            NextInterview: input.Status is RecruitmentApplicationStatus.Shortlisted or RecruitmentApplicationStatus.InterviewScheduled
                ? nextInterview
                : null,
            Timeline: BuildTimeline(input, code, label, offerIssued));
    }

    private static IReadOnlyList<CandidatePublicTimelineStep> BuildTimeline(
        CandidatePublicStatusInput input,
        string code,
        string label,
        bool offerIssued)
    {
        var reached = new Dictionary<string, DateTime?>();
        void Reach(string step, DateTime? at)
        {
            if (!reached.ContainsKey(step))
                reached[step] = at;
        }

        var events = input.Events.OrderBy(e => e.OccurredAtUtc).ToList();
        DateTime? decisionAt = null;
        if (events.Count > 0)
        {
            foreach (var e in events)
            {
                switch (e.EventType)
                {
                    case RecruitmentApplicationEventTypes.Received:
                        Reach(Steps.Received, e.OccurredAtUtc);
                        if (e.ToStatus is int initial)
                            ReachForStatus((RecruitmentApplicationStatus)initial, e.OccurredAtUtc, Reach);
                        break;
                    case RecruitmentApplicationEventTypes.StatusChanged when e.ToStatus is int to:
                        ReachForStatus((RecruitmentApplicationStatus)to, e.OccurredAtUtc, Reach);
                        if ((RecruitmentApplicationStatus)to == input.Status)
                            decisionAt = e.OccurredAtUtc;
                        break;
                    case RecruitmentApplicationEventTypes.InterviewScheduled
                        or RecruitmentApplicationEventTypes.InterviewRescheduled:
                        Reach(Steps.Interview, e.OccurredAtUtc);
                        break;
                    case RecruitmentApplicationEventTypes.OfferIssued:
                        Reach(Steps.Offer, e.OccurredAtUtc);
                        break;
                    case RecruitmentApplicationEventTypes.AppealFiled:
                        Reach(Steps.Appeal, e.OccurredAtUtc);
                        break;
                }
            }
        }
        else
        {
            // No events (rows from before the event log): only what the current status proves happened, undated.
            ReachForStatus(input.Status, null, Reach);
            if (input.AppealStatus != RecruitmentAppealStatus.None)
                Reach(Steps.Appeal, null);
        }

        // The current state proves these even when their event predates the log.
        Reach(Steps.Received, input.AppliedAtUtc);
        if (offerIssued || input.ActiveOfferStatus is OfferStatus.Accepted)
            Reach(Steps.Offer, null);

        var isDecision = code is Codes.Accepted or Codes.ReserveListed or Codes.OfferClosed or Codes.NotAccepted
            or Codes.Withdrawn;
        var steps = new List<CandidatePublicTimelineStep>
        {
            Step(Steps.Received, "تم استلام الطلب", reached),
            Step(Steps.Review, "قيد المراجعة", reached),
            Step(Steps.Interview, "مدعو للمقابلة", reached),
            Step(Steps.FinalEvaluation, "قيد التقييم النهائي", reached),
            Step(Steps.Offer, "عرض العمل", reached)
        };
        if (reached.ContainsKey(Steps.Appeal))
            steps.Add(Step(Steps.Appeal, "الاستئناف", reached));
        steps.Add(new CandidatePublicTimelineStep(
            Steps.Decision, "القرار", isDecision, isDecision ? decisionAt : null, isDecision ? label : null));
        return steps;
    }

    /// <summary>The public steps a status proves were passed (Submitted → none beyond reception).</summary>
    private static void ReachForStatus(RecruitmentApplicationStatus status, DateTime? at, Action<string, DateTime?> reach)
    {
        switch (status)
        {
            case RecruitmentApplicationStatus.UnderReview:
                reach(Steps.Review, at);
                break;
            case RecruitmentApplicationStatus.Shortlisted or RecruitmentApplicationStatus.InterviewScheduled:
                reach(Steps.Review, at);
                reach(Steps.Interview, at);
                break;
            case RecruitmentApplicationStatus.Assessed or RecruitmentApplicationStatus.Ranked
                or RecruitmentApplicationStatus.ReserveListed or RecruitmentApplicationStatus.OfferExtended:
                reach(Steps.Review, at);
                reach(Steps.Interview, at);
                reach(Steps.FinalEvaluation, at);
                // OfferExtended is set on the draft: the offer step comes from OfferIssued only.
                break;
            case RecruitmentApplicationStatus.OfferAccepted or RecruitmentApplicationStatus.Hired:
                reach(Steps.Review, at);
                reach(Steps.Interview, at);
                reach(Steps.FinalEvaluation, at);
                reach(Steps.Offer, at);
                break;
        }
    }

    private static CandidatePublicTimelineStep Step(string code, string label, Dictionary<string, DateTime?> reached) =>
        new(code, label, reached.ContainsKey(code), reached.GetValueOrDefault(code), null);

    private static string ModeAr(InterviewMode mode) => mode == InterviewMode.Remote ? "عن بُعد" : "حضوري";

    private static string FormatDate(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
}
