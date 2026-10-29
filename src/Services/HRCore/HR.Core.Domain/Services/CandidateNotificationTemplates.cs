using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Notifications;

namespace HR.Core.Domain.Services;

/// <summary>
/// COM-030 (PLAN-30 F6): what a candidate message may contain. Deliberately free of the candidate's name, national id,
/// birth date, salary/grade, scores, rank, committee data, panel members and notes (internal, outcome or cancellation
/// notes), so a template cannot leak them (<c>Hcm030NotificationPiiGuardTests</c>). Build it with
/// <see cref="CandidateNotificationTemplates.InputFor"/>.
/// </summary>
public sealed record CandidateNotificationTemplateInput
{
    /// <summary>The citizen application reference (e.g. <c>CIT-2026-000123</c>).</summary>
    public required string ReferenceNumber { get; init; }

    /// <summary><c>RecruitmentApplicationEvent.OccurredAt</c>; base of the expiry of most messages.</summary>
    public required DateTime OccurredAtUtc { get; init; }

    /// <summary>Candidate portal (<c>{Recruitment:PublicPortalBaseUrl}/careers/my</c>, HCM-030 I); no link when null.</summary>
    public string? PortalUrl { get; init; }

    public DateTime? InterviewAtUtc { get; init; }
    public InterviewMode? InterviewMode { get; init; }

    /// <summary>The slot's place (HR free text, shown in emails of in-person interviews only).</summary>
    public string? InterviewLocation { get; init; }

    /// <summary><c>OfferLetter.ExpiryDate</c> (the only offer field a message may use).</summary>
    public DateTime? OfferExpiryUtc { get; init; }

    /// <summary>Appeal deadline when the rejection is appealable (from <see cref="CandidatePublicStatusProjection.AppealWindowOf"/>); else null.</summary>
    public DateTime? AppealDeadlineUtc { get; init; }

    public DateOnly? ReserveUntil { get; init; }
    public RecruitmentAppealOutcome? AppealOutcome { get; init; }

    /// <summary>Zone of the dates in the text (default UTC); /careers/my uses <c>TimeZoneInfo.Local</c>.</summary>
    public TimeZoneInfo? DisplayZone { get; init; }
}

/// <summary>COM-030: a rendered candidate message (stored on <c>CandidateNotification</c> when planned).</summary>
public sealed record CandidateNotificationContent(
    string TemplateCode,
    NotificationChannel Channel,
    string? Subject,
    string Body,
    DateTime ExpiresAtUtc);

/// <summary>
/// COM-030 (PLAN-30 F6): Arabic candidate messages. HR.Core has no resource (.resx) convention for domain text — its
/// Arabic texts are C# literals (labels, enum displays) — so the templates live in this one pure class.
/// <list type="bullet">
/// <item>Hard rules: no national id, salary, birth date, scores, rank, committee data, internal notes, panel names or
/// candidate name (the input cannot carry them).</item>
/// <item>SMS: one UCS-2 segment (<see cref="SmsMaxLength"/> characters with a 15-character reference), no link.</item>
/// <item>Email: subject + short body + reference + <c>/careers/my</c> link (when configured) + automated-message footer.</item>
/// </list>
/// </summary>
public static class CandidateNotificationTemplates
{
    public const string AppReceived = "APP_RECEIVED";
    public const string InterviewScheduled = "INTERVIEW_SCHEDULED";
    public const string InterviewRescheduled = "INTERVIEW_RESCHEDULED";
    public const string InterviewCancelled = "INTERVIEW_CANCELLED";
    public const string OfferIssued = "OFFER_ISSUED";
    public const string OfferReminder = "OFFER_REMINDER";
    public const string AppRejected = "APP_REJECTED";
    public const string AppReserve = "APP_RESERVE";
    public const string OfferAccepted = "OFFER_ACCEPTED";
    public const string AppHired = "APP_HIRED";
    public const string AppWithdrawn = "APP_WITHDRAWN";
    public const string AppealDecided = "APPEAL_DECIDED";
    // HCM-031 (PLAN-31): competition messages — never a score, a rank or a national id (decision 4).
    public const string AdmissionCardReady = "ADMISSION_CARD_READY";
    public const string ResultsPublished = "RESULTS_PUBLISHED";
    public const string ObjectionDecided = "OBJECTION_DECIDED";
    public const string CompetitionAllocated = "COMPETITION_ALLOCATED";
    public const string CompetitionReserve = "COMPETITION_RESERVE";
    public const string CompetitionReserveRecalled = "COMPETITION_RESERVE_RECALLED";
    public const string CompetitionNotSuccessful = "COMPETITION_NOT_SUCCESSFUL";

    public static readonly IReadOnlyList<string> All =
    [
        AppReceived, InterviewScheduled, InterviewRescheduled, InterviewCancelled, OfferIssued, OfferReminder,
        AppRejected, AppReserve, OfferAccepted, AppHired, AppWithdrawn, AppealDecided,
        AdmissionCardReady, ResultsPublished, ObjectionDecided, CompetitionAllocated, CompetitionReserve,
        CompetitionReserveRecalled, CompetitionNotSuccessful
    ];

    /// <summary>One Arabic (UCS-2) SMS segment.</summary>
    public const int SmsMaxLength = 70;

    /// <summary>Longest template code (column <c>CandidateNotification.TemplateCode</c>).</summary>
    public const int TemplateCodeMaxLength = 40;

    private const string Footer = "هذه رسالة آلية من بوابة التوظيف، يرجى عدم الرد عليها.";

    /// <summary>
    /// Event → template (session 1 event contract, PLAN-30 F map). Null = the event sends no message (it is only marked
    /// planned): OfferDeclined/Expired/Revoked, AppealFiled, and status changes other than Rejected, ReserveListed,
    /// Hired and Withdrawn.
    /// </summary>
    public static string? TemplateCodeFor(string eventType, int? toStatus) => eventType switch
    {
        RecruitmentApplicationEventTypes.Received => AppReceived,
        RecruitmentApplicationEventTypes.InterviewScheduled => InterviewScheduled,
        RecruitmentApplicationEventTypes.InterviewRescheduled => InterviewRescheduled,
        RecruitmentApplicationEventTypes.InterviewCancelled => InterviewCancelled,
        RecruitmentApplicationEventTypes.OfferIssued => OfferIssued,
        RecruitmentApplicationEventTypes.OfferExpiryReminder => OfferReminder,
        RecruitmentApplicationEventTypes.OfferAccepted => OfferAccepted,
        RecruitmentApplicationEventTypes.AppealDecided => AppealDecided,
        RecruitmentApplicationEventTypes.AdmissionCardReady => AdmissionCardReady,
        RecruitmentApplicationEventTypes.ResultsPublished => ResultsPublished,
        RecruitmentApplicationEventTypes.ObjectionDecided => ObjectionDecided,
        RecruitmentApplicationEventTypes.CompetitionAllocated => CompetitionAllocated,
        RecruitmentApplicationEventTypes.CompetitionReserve => CompetitionReserve,
        RecruitmentApplicationEventTypes.CompetitionReserveRecalled => CompetitionReserveRecalled,
        RecruitmentApplicationEventTypes.CompetitionNotSuccessful => CompetitionNotSuccessful,
        RecruitmentApplicationEventTypes.StatusChanged => (RecruitmentApplicationStatus?)toStatus switch
        {
            RecruitmentApplicationStatus.Rejected => AppRejected,
            RecruitmentApplicationStatus.ReserveListed => AppReserve,
            RecruitmentApplicationStatus.Hired => AppHired,
            RecruitmentApplicationStatus.Withdrawn => AppWithdrawn,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// Builds the template input from the domain rows, reading only the allowed fields: the slot's time, mode and place;
    /// the offer's expiry; the reserve end; the appeal outcome; and — for a rejection — the appeal deadline when the
    /// rejection is appealable at <paramref name="occurredAtUtc"/> (<see cref="CandidatePublicStatusProjection.AppealWindowOf"/>).
    /// </summary>
    public static CandidateNotificationTemplateInput InputFor(
        string referenceNumber,
        DateTime occurredAtUtc,
        RecruitmentApplication application,
        RecruitmentInterviewSlot? slot = null,
        OfferLetter? offer = null,
        RecruitmentAppeal? appeal = null,
        string? portalUrl = null,
        int appealWindowDays = RecruitmentAppealWorkflowRules.DefaultAppealWindowDays,
        IReadOnlyCollection<RecruitmentRejectionKind>? appealableKinds = null,
        TimeZoneInfo? displayZone = null)
    {
        DateTime? appealDeadline = null;
        if (application.Status == RecruitmentApplicationStatus.Rejected)
        {
            var (open, deadline) = CandidatePublicStatusProjection.AppealWindowOf(
                application, occurredAtUtc, appealWindowDays, appealableKinds);
            appealDeadline = open ? deadline : null;
        }

        return new CandidateNotificationTemplateInput
        {
            ReferenceNumber = referenceNumber,
            OccurredAtUtc = occurredAtUtc,
            PortalUrl = portalUrl,
            InterviewAtUtc = slot?.ScheduledAt,
            InterviewMode = slot?.Mode,
            InterviewLocation = slot?.Location,
            OfferExpiryUtc = offer?.ExpiryDate,
            AppealDeadlineUtc = appealDeadline,
            ReserveUntil = application.ReserveUntil,
            AppealOutcome = appeal?.Outcome,
            DisplayZone = displayZone
        };
    }

    /// <summary>Renders <paramref name="templateCode"/> for <paramref name="channel"/> (Email or Sms).</summary>
    public static CandidateNotificationContent Render(
        string templateCode, NotificationChannel channel, CandidateNotificationTemplateInput input)
    {
        if (channel is not (NotificationChannel.Email or NotificationChannel.Sms))
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Candidate messages are Email or Sms.");

        var zone = input.DisplayZone ?? TimeZoneInfo.Utc;
        var reference = input.ReferenceNumber.Trim();
        string date(DateTime? utc) => utc is DateTime d ? FormatDate(d, zone) : string.Empty;
        string dateTime(DateTime? utc) => utc is DateTime d ? FormatDateTime(d, zone) : string.Empty;
        var mode = ModeAr(input.InterviewMode);

        (string Subject, string Email, string Sms) text = templateCode switch
        {
            AppReceived => ("تم استلام طلب التوظيف",
                "تم استلام طلبك بنجاح، وسيتم إعلامك بمستجداته.",
                $"استلمنا طلبك، المرجع {reference}. تابعه عبر بوابة التوظيف."),
            InterviewScheduled => ("موعد مقابلة",
                $"تمت دعوتك إلى مقابلة بتاريخ {dateTime(input.InterviewAtUtc)} ({mode})." + PlaceLine(input),
                $"مقابلتك {dateTime(input.InterviewAtUtc)} {mode}. المرجع {reference}"),
            InterviewRescheduled => ("تعديل موعد المقابلة",
                $"تم تعديل موعد مقابلتك إلى {dateTime(input.InterviewAtUtc)} ({mode})." + PlaceLine(input),
                $"موعد مقابلتك الجديد {dateTime(input.InterviewAtUtc)} {mode}. {reference}"),
            InterviewCancelled => ("إلغاء موعد المقابلة",
                input.InterviewAtUtc is null
                    ? "تم إلغاء موعد مقابلتك، وسيتم إعلامك بأي موعد جديد."
                    : $"تم إلغاء موعد مقابلتك المحدد بتاريخ {dateTime(input.InterviewAtUtc)}، وسيتم إعلامك بأي موعد جديد.",
                input.InterviewAtUtc is null
                    ? $"أُلغي موعد مقابلتك. المرجع {reference}"
                    : $"أُلغيت مقابلتك المحددة {date(input.InterviewAtUtc)}. المرجع {reference}"),
            OfferIssued => ("عرض وظيفي",
                input.OfferExpiryUtc is null
                    ? "صدر لك عرض وظيفي، يرجى الاطلاع والرد عبر البوابة."
                    : $"صدر لك عرض وظيفي، يرجى الاطلاع والرد عبر البوابة قبل {date(input.OfferExpiryUtc)}.",
                input.OfferExpiryUtc is null
                    ? $"لديك عرض وظيفي، يرجى الرد عبر البوابة. المرجع {reference}"
                    : $"لديك عرض وظيفي، يرجى الرد قبل {date(input.OfferExpiryUtc)}. المرجع {reference}"),
            OfferReminder => ("تذكير بالعرض الوظيفي",
                input.OfferExpiryUtc is null
                    ? "تذكير: العرض الوظيفي المقدّم لك بانتظار ردك عبر البوابة."
                    : $"تذكير: ينتهي العرض الوظيفي المقدّم لك بتاريخ {date(input.OfferExpiryUtc)}، يرجى الرد عبر البوابة قبل انتهائه.",
                input.OfferExpiryUtc is null
                    ? $"تذكير: عرضك الوظيفي بانتظار ردك. المرجع {reference}"
                    : $"تذكير: ينتهي عرضك الوظيفي {date(input.OfferExpiryUtc)}. المرجع {reference}"),
            AppRejected => ("نتيجة طلب التوظيف",
                "نأسف لإبلاغك بأنه لم يتم قبول طلبك."
                    + (input.AppealDeadlineUtc is null ? string.Empty
                        : $" يمكنك تقديم استئناف عبر الجهة حتى {date(input.AppealDeadlineUtc)}."),
                $"لم يتم قبول طلبك {reference}."
                    + (input.AppealDeadlineUtc is null ? string.Empty
                        : $" يمكنك الاستئناف حتى {date(input.AppealDeadlineUtc)}.")),
            AppReserve => ("قائمة الاحتياط",
                input.ReserveUntil is DateOnly until
                    ? $"تم إدراج طلبك في قائمة الاحتياط حتى {FormatDate(until)}."
                    : "تم إدراج طلبك في قائمة الاحتياط.",
                input.ReserveUntil is DateOnly smsUntil
                    ? $"طلبك {reference} في قائمة الاحتياط حتى {FormatDate(smsUntil)}."
                    : $"طلبك {reference} في قائمة الاحتياط."),
            OfferAccepted => ("تأكيد قبول العرض",
                "تم استلام قبولك للعرض الوظيفي، وستتواصل معك الجهة لاستكمال إجراءات التعيين.",
                $"استلمنا قبولك للعرض الوظيفي. المرجع {reference}"),
            AppHired => ("استكمال إجراءات التعيين",
                "تم استكمال إجراءات التعيين، ستتواصل معك الجهة بشأن المباشرة.",
                $"تم استكمال إجراءات تعيينك. المرجع {reference}"),
            AppWithdrawn => ("تأكيد الانسحاب",
                "تم تسجيل انسحابك من طلب التوظيف.",
                $"تم تسجيل انسحابك من الطلب {reference}."),
            AppealDecided => ("نتيجة الاستئناف",
                $"تم البت في استئنافك: {AppealOutcomeAr(input.AppealOutcome)}.",
                $"استئنافك على الطلب {reference}: {AppealOutcomeAr(input.AppealOutcome)}."),
            AdmissionCardReady => ("بطاقة الامتحان",
                "صدرت بطاقة دخول الامتحان الخاصة بك، ويمكنك تنزيلها من صفحة «طلبي».",
                $"صدرت بطاقة امتحانك، المرجع {reference}. نزّلها من صفحة طلبي."),
            ResultsPublished => ("نتائج المسابقة",
                "نُشرت نتائج المسابقة. اطّلع على نتيجتك من صفحة «طلبي».",
                $"نُشرت نتائج المسابقة، المرجع {reference}. راجع صفحة طلبي."),
            ObjectionDecided => ("قرار الاعتراض",
                "صدر قرار المراجع المستقل في اعتراضك على النتيجة، والتفاصيل في صفحة «طلبي».",
                $"صدر قرار اعتراضك، المرجع {reference}. راجع صفحة طلبي."),
            CompetitionAllocated => ("الترشيح للتعيين",
                "تم ترشيحك للتعيين على أحد شواغر المسابقة، وستتواصل معك الجهة بشأن العرض الوظيفي.",
                $"رُشّحت للتعيين في المسابقة، المرجع {reference}."),
            CompetitionReserve => ("قائمة احتياط المسابقة",
                input.ReserveUntil is DateOnly competitionUntil
                    ? $"أُدرج اسمك في قائمة احتياط المسابقة حتى {FormatDate(competitionUntil)}."
                    : "أُدرج اسمك في قائمة احتياط المسابقة.",
                $"أُدرجت في قائمة احتياط المسابقة، المرجع {reference}."),
            CompetitionReserveRecalled => ("الاستدعاء من قائمة الاحتياط",
                "تم استدعاؤك من قائمة الاحتياط للترشيح على شاغر، وستتواصل معك الجهة بشأن العرض الوظيفي.",
                $"استُدعيت من قائمة الاحتياط، المرجع {reference}."),
            CompetitionNotSuccessful => ("نتيجة المسابقة",
                "نأسف لإبلاغك بأنك لم تكن من المرشحين للتعيين في هذه المسابقة. شكراً لاهتمامك.",
                $"انتهت المسابقة دون ترشيحك، المرجع {reference}."),
            _ => throw new ArgumentOutOfRangeException(nameof(templateCode), templateCode, "Unknown candidate template.")
        };

        var expires = ExpiresAt(templateCode, input);
        if (channel == NotificationChannel.Sms)
            return new CandidateNotificationContent(templateCode, channel, null, text.Sms, expires);

        var lines = new List<string> { text.Email, $"رقم الطلب المرجعي: {reference}" };
        if (!string.IsNullOrWhiteSpace(input.PortalUrl))
            lines.Add($"تابع حالة طلبك عبر بوابة التوظيف: {input.PortalUrl}");
        lines.Add(string.Empty);
        lines.Add(Footer);
        return new CandidateNotificationContent(
            templateCode, channel, "بوابة التوظيف — " + text.Subject, string.Join("\n", lines), expires);
    }

    /// <summary>
    /// When an unsent message stops being useful (PLAN-30 F map): interview time, offer expiry, appeal deadline or reserve
    /// end where the message is about one; +7 days for a withdrawal; +30 days otherwise.
    /// </summary>
    public static DateTime ExpiresAt(string templateCode, CandidateNotificationTemplateInput input)
    {
        var occurred = DateTime.SpecifyKind(input.OccurredAtUtc, DateTimeKind.Utc);
        var standard = occurred.AddDays(30);
        return templateCode switch
        {
            // The interview time even when already past (a cancelled past slot): the row then expires unsent.
            InterviewScheduled or InterviewRescheduled or InterviewCancelled =>
                input.InterviewAtUtc is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : occurred.AddDays(1),
            OfferIssued or OfferReminder =>
                input.OfferExpiryUtc is DateTime expiry && expiry > occurred ? expiry : standard,
            AppRejected => input.AppealDeadlineUtc is DateTime deadline && deadline > occurred ? deadline : standard,
            AppReserve or CompetitionReserve => input.ReserveUntil is DateOnly until
                ? DateTime.SpecifyKind(until.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc)
                : standard,
            AppWithdrawn => occurred.AddDays(7),
            _ => standard
        };
    }

    /// <summary>COM-030 (F7): Arabic label of a template code for the HR «الإشعارات» card (the code itself when unknown).</summary>
    public static string LabelAr(string templateCode) => templateCode switch
    {
        AppReceived => "استلام الطلب",
        InterviewScheduled => "موعد مقابلة",
        InterviewRescheduled => "تعديل موعد المقابلة",
        InterviewCancelled => "إلغاء موعد المقابلة",
        OfferIssued => "صدور عرض وظيفي",
        OfferReminder => "تذكير بانتهاء العرض",
        AppRejected => "عدم قبول الطلب",
        AppReserve => "قائمة الاحتياط",
        OfferAccepted => "قبول العرض",
        AppHired => "استكمال التعيين",
        AppWithdrawn => "الانسحاب",
        AppealDecided => "نتيجة الاستئناف",
        AdmissionCardReady => "بطاقة الامتحان",
        ResultsPublished => "نشر نتائج المسابقة",
        ObjectionDecided => "قرار الاعتراض",
        CompetitionAllocated => "الترشيح للتعيين",
        CompetitionReserve => "قائمة احتياط المسابقة",
        CompetitionReserveRecalled => "الاستدعاء من الاحتياط",
        CompetitionNotSuccessful => "عدم الترشيح في المسابقة",
        _ => templateCode
    };

    /// <summary>Public appeal outcome label (no rank or committee wording).</summary>
    public static string AppealOutcomeAr(RecruitmentAppealOutcome? outcome) => outcome switch
    {
        RecruitmentAppealOutcome.Upheld or RecruitmentAppealOutcome.UpheldRankCorrection
            or RecruitmentAppealOutcome.UpheldNoRemedy => "قُبل الاستئناف",
        RecruitmentAppealOutcome.Dismissed => "رُفض الاستئناف",
        _ => "صدر القرار"
    };

    private static string PlaceLine(CandidateNotificationTemplateInput input)
    {
        if (input.InterviewMode == Enums.InterviewMode.Remote || string.IsNullOrWhiteSpace(input.InterviewLocation))
            return string.Empty;
        var place = string.Join(' ', input.InterviewLocation.Split(
            ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return " المكان: " + (place.Length > 200 ? place[..200] : place) + ".";
    }

    private static string ModeAr(InterviewMode? mode) => mode == Enums.InterviewMode.Remote ? "عن بُعد" : "حضوري";

    private static string FormatDate(DateOnly date) => date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
}
