using System.Globalization;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Services;

/// <summary>HCM-030 (PLAN-30 I): outcome of a <c>/careers/my</c> action; errors are keyed by form field ("" = whole form).</summary>
public sealed record CandidatePortalActionResult(bool Success, IReadOnlyList<(string Field, string Error)> Errors)
{
    public static readonly CandidatePortalActionResult Ok = new(true, []);

    public static CandidatePortalActionResult Fail(string field, string error) => new(false, [(field, error)]);
}

/// <summary>
/// HCM-030 (PLAN-30 I): «طلبي» on <c>/careers/my</c> — the page and the candidate's actions (accept/decline the offer,
/// offer letter PDF, withdraw, appeal, answer an information request, upload missing documents).
/// <list type="bullet">
/// <item>The application and its offer/appeal come only from <see cref="CandidatePortalContext"/> (the session), never from
/// a route, query or form id.</item>
/// <item>Every call into the recruitment services runs with the session's tenant as the ambient tenant
/// (<see cref="TenantIdFlow"/>, restored afterwards — DECISIONS H.1), so tenant filters, tenant stamping and RLS apply to
/// that tenant only, whatever <c>X-Tenant-ID</c> a client sent.</item>
/// <item>Actions go through the existing services, so the application events (session 1) and the candidate notifications
/// (session 2) follow. Actor <c>Candidate</c>, no user id: the services audit accept/decline/appeal/withdraw with the
/// channel (<c>AcceptedBy</c>/<c>DeclinedBy</c>/<c>FiledBy</c> = Candidate); the portal adds <c>CandidatePortal*</c> rows
/// for the actions the services do not attribute (information answer, documents, offer letter download).</item>
/// </list>
/// </summary>
public sealed class CandidatePortalService(
    HRCoreDbContext db,
    CandidatePublicStatusService publicStatus,
    RecruitmentOfferService offers,
    RecruitmentSelectionService selection,
    RecruitmentAppealService appeals,
    OfferLetterDocumentService offerDocuments,
    CandidateDocumentService candidateDocuments,
    UnifiedIntakeService unifiedIntake,
    IOptions<RecruitmentOptions> options,
    ILogger<CandidatePortalService> logger,
    // HCM-031 (PLAN-31): optional so the existing manual constructions (tests) keep compiling; DI always supplies it.
    HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionReserveService? competitionReserve = null,
    HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionObjectionService? competitionObjections = null,
    HR.Core.Infrastructure.Services.Recruitment.Competitions.ExamAdmissionCardDocumentService? admissionCards = null)
{
    public const string Actor = "Candidate";

    public const string AcknowledgeRequiredMessage = "يجب الإقرار بالاطلاع على شروط العرض وقبولها.";
    public const string FullNameRequiredMessage = "يرجى كتابة اسمك الكامل كما ورد في الطلب.";
    public const string NameMismatchMessage = "الاسم المكتوب لا يطابق الاسم الوارد في الطلب.";
    public const string NoOfferMessage = "لا يوجد عرض بانتظار ردك.";
    public const string ExpiredOfferReplyMessage = "انتهت صلاحية العرض ولا يمكن الرد عليه.";
    public const string WithdrawConfirmRequiredMessage = "يرجى تأكيد رغبتك في الانسحاب من الطلب.";
    public const string NoAppealInfoRequestMessage = "لا يوجد طلب معلومات بانتظار ردك.";
    public const string NoApplicationMessage = "لا يمكن تنفيذ هذا الإجراء على هذا الطلب.";
    public const string DocumentsClosedMessage = "انتهت فترة استقبال المستندات لهذا الطلب.";
    public const string NoMissingDocumentsMessage = "لا توجد مستندات مطلوبة ناقصة.";
    public const string NoFilesMessage = "يرجى اختيار ملف واحد على الأقل.";
    public const string NotMissingTypeMessage = "هذا المستند غير مطلوب أو مُقدَّم مسبقاً.";
    public const string ActionFailedMessage = "تعذّر تنفيذ الطلب حالياً، يرجى المحاولة لاحقاً.";

    private RecruitmentOptions Settings => options.Value;

    // ------------------------------------------------------------------ page

    /// <summary>The «طلبي» page of the session's application.</summary>
    public Task<CandidatePortalPageViewModel> GetPageAsync(CandidatePortalContext session, CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            var citizen = await db.CitizenApplications
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .AsNoTracking()
                .Where(c => c.Id == session.CitizenApplicationId && c.TenantId == session.TenantId)
                .Select(c => new
                {
                    c.Status,
                    c.SubmittedAt,
                    PostingTitle = c.PublicJobPosting != null ? c.PublicJobPosting.Title : null,
                    RequisitionTitle = c.Requisition != null ? c.Requisition.JobTitle : null
                })
                .FirstAsync(ct);

            var app = session.RecruitmentApplicationId is Guid appId ? await LoadApplicationAsync(session, appId, ct) : null;
            if (app is null)
            {
                // Legacy citizen row (no recruitment application): the status only, no action.
                return new CandidatePortalPageViewModel
                {
                    ReferenceNumber = session.ReferenceNumber,
                    JobTitle = citizen.RequisitionTitle ?? citizen.PostingTitle,
                    SubmittedAtUtc = citizen.SubmittedAt,
                    SessionExpiresAtUtc = session.ExpiresAt,
                    LegacyStatusLabel = LegacyStatusLabel(citizen.Status)
                };
            }

            var now = DateTime.UtcNow;
            var view = await publicStatus.GetViewAsync(app.Id, forPublicPortal: true, ct);
            var interviews = await db.RecruitmentInterviewSlots.AsNoTracking()
                .Where(s => s.RecruitmentApplicationId == app.Id && s.Status != InterviewSlotStatus.Rescheduled)
                .OrderBy(s => s.ScheduledAt)
                .Select(s => new { s.ScheduledAt, s.Mode, s.Location, s.Status })
                .ToListAsync(ct);
            var documents = await candidateDocuments.GetRowsAsync(app, ct);
            var missing = await MissingDocumentTypesAsync(app, documents.Select(d => d.DocumentType), ct);
            var intakeError = await DocumentIntakeErrorAsync(session, app, documents.Count, ct);

            return new CandidatePortalPageViewModel
            {
                ReferenceNumber = session.ReferenceNumber,
                JobTitle = app.Requisition?.JobTitle ?? citizen.RequisitionTitle ?? citizen.PostingTitle ?? app.AppliedPosition,
                SubmittedAtUtc = citizen.SubmittedAt,
                SessionExpiresAtUtc = session.ExpiresAt,
                Status = view,
                Interviews = interviews
                    .Select(i => new CandidatePortalInterviewRow(
                        FormatDate(i.ScheduledAt),
                        FormatTime(i.ScheduledAt),
                        PlaceText(i.Mode, i.Location),
                        InterviewStatusLabel(i.Status),
                        i.Status == InterviewSlotStatus.Scheduled && i.ScheduledAt >= now))
                    .ToList(),
                Documents = documents
                    .Select(d => new CandidatePortalDocumentRow(
                        d.TypeLabel, d.FileName, FormatSize(d.SizeBytes), FormatDate(d.UploadedAt), d.ScanLabel))
                    .ToList(),
                MissingDocumentTypes = missing.Select(code => (code, CandidateDocumentTypes.LabelAr(code))).ToList(),
                CanUploadDocuments = missing.Count > 0 && intakeError is null,
                DocumentsClosedReason = missing.Count > 0 ? intakeError : null,
                MaxDocumentBytes = CandidateDocumentFileValidator.Constraints(Settings.CandidateDocuments).MaxBytes,
                MaxTotalDocumentBytes = Settings.CandidateDocuments.MaxTotalBytes,
                Offer = await OfferCardAsync(app, now, ct),
                Appeal = await AppealCardAsync(app, now, ct),
                Competition = await CompetitionCardAsync(app, now, ct),
                CanWithdraw = view?.CanWithdraw == true,
                // Not for a terminal application (Hired keeps its accepted offer): nothing to withdraw from there.
                WithdrawBlockedByAcceptance = !SelectionWorkflowRules.TerminalStatuses.Contains(app.Status)
                    && (app.Status == RecruitmentApplicationStatus.OfferAccepted
                        || app.ActiveOfferLetter?.Status == OfferStatus.Accepted)
            };
        });

    // ------------------------------------------------------------------ competition (HCM-031)

    /// <summary>
    /// HCM-031 (PLAN-31, P6): «بطاقة الامتحان» — the issued admission card of the session's application for an exam type
    /// (the type, never an id, comes from the page). Null when not issued.
    /// </summary>
    public Task<(byte[] Pdf, string FileName)?> GetAdmissionCardAsync(CandidatePortalContext session, string? examType, CancellationToken ct = default) =>
        InSessionTenantAsync<(byte[] Pdf, string FileName)?>(session, async () =>
        {
            if (session.RecruitmentApplicationId is not Guid appId || admissionCards is null)
                return null;
            var card = (await admissionCards.GetIssuedForApplicationAsync(session.TenantId, appId, ct))
                .FirstOrDefault(c => string.Equals(TypeOf(c), examType, StringComparison.Ordinal));
            if (card is null)
                return null;
            db.AuditLogs.Add(new HRManagement.SharedKernel.Entities.EntityAuditLog
            {
                TenantId = session.TenantId,
                EntityType = nameof(RecruitmentApplication),
                EntityId = appId,
                Action = "CandidatePortalAdmissionCardDownloaded",
                ChangedFields = System.Text.Json.JsonSerializer.Serialize(new { actor = Actor, examType })
            });
            await db.SaveChangesAsync(ct);
            return (ExamAdmissionCardDocument.Generate(card), $"admission-card-{card.Data.ExamNumber}.pdf");
        });

    /// <summary>
    /// HCM-031 (PLAN-31, P6): files a result objection on the session's application (window, one pending objection and
    /// the paper are checked again by <c>CompetitionObjectionService</c>).
    /// </summary>
    public Task<CandidatePortalActionResult> FileObjectionAsync(CandidatePortalContext session, string? reason, string? examType, CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            if (session.RecruitmentApplicationId is not Guid appId || competitionObjections is null)
                return CandidatePortalActionResult.Fail(string.Empty, NoApplicationMessage);
            Guid? paperId = null;
            if (!string.IsNullOrEmpty(examType) && Enum.TryParse<ExamSessionType>(examType, out var type))
                paperId = await db.ExamCandidates
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                    .Where(c => c.TenantId == session.TenantId && c.RecruitmentApplicationId == appId && c.ExamType == type)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefaultAsync(ct);
            var (appeal, error) = await competitionObjections.FileAsync(appId, paperId, reason, RecruitmentAppealFiledBy.Candidate, actorUserId: null, ct: ct);
            return appeal is null
                ? CandidatePortalActionResult.Fail(error is not null && error.StartsWith("سبب", StringComparison.Ordinal) ? "Text" : string.Empty, error ?? ActionFailedMessage)
                : CandidatePortalActionResult.Ok;
        });

    private async Task<CandidatePortalCompetitionCard?> CompetitionCardAsync(RecruitmentApplication app, DateTime now, CancellationToken ct)
    {
        if (app.CompetitionId is not Guid competitionId)
            return null;
        var competition = await db.RecruitmentCompetitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .FirstAsync(c => c.Id == competitionId && c.TenantId == app.TenantId, ct);
        var preferences = await db.CompetitionPreferences
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .Where(p => p.TenantId == app.TenantId && p.RecruitmentApplicationId == app.Id)
            .OrderBy(p => p.Order)
            .Select(p => $"{p.Order}. {p.CompetitionLine!.Title} ({p.CompetitionLine.GradeCode})")
            .ToListAsync(ct);
        var papers = await db.ExamCandidates
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .Where(c => c.TenantId == app.TenantId && c.RecruitmentApplicationId == app.Id)
            .OrderBy(c => c.ExamSession!.ScheduledAt)
            .Select(c => new { c.ExamType, c.ExamSession!.Title, c.ExamSession.ScheduledAt, c.ExamSession.Venue, c.SeatNumber, c.ExamNumber, Issued = c.AdmissionCardIssuedAt != null })
            .ToListAsync(ct);

        // The score, rank and outcome are shown only once the results are published (never in a message).
        CandidatePortalCompetitionResult? result = null;
        if (competition.CurrentResultVersionId is Guid versionId)
        {
            var row = await db.CompetitionResults
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .AsNoTracking()
                .Where(r => r.TenantId == app.TenantId && r.ResultVersionId == versionId && r.RecruitmentApplicationId == app.Id)
                .Select(r => new { r.FinalScore, r.OverallRank, r.Outcome, r.ResultVersion!.PublishedAt })
                .FirstOrDefaultAsync(ct);
            if (row is not null)
                result = new CandidatePortalCompetitionResult(
                    row.FinalScore?.ToString("0.##", CultureInfo.InvariantCulture),
                    row.OverallRank,
                    CompetitionResultsDocument.OutcomeAr(row.Outcome),
                    row.PublishedAt is DateTime published ? FormatDate(published) : "—");
        }

        var deadline = CompetitionWorkflowRules.ObjectionDeadlineUtc(competition.ResultsFirstPublishedAt, competition.ObjectionWindowDays);
        var objection = await db.RecruitmentAppeals
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .Where(a => a.TenantId == app.TenantId && a.RecruitmentApplicationId == app.Id && a.Kind == RecruitmentAppealKind.ResultObjection)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Status, a.Outcome })
            .FirstOrDefaultAsync(ct);
        var canObject = result is not null && competitionObjections is not null
            && await competitionObjections.GetFilingErrorAsync(app, now, ct) is null;
        var objectionStatus = objection switch
        {
            null => null,
            { Status: RecruitmentAppealStatus.Pending } => "اعتراضك قيد المراجعة",
            { Outcome: RecruitmentAppealOutcome.ObjectionUpheld } => "قُبل اعتراضك وصُحّحت العلامة؛ ستُنشر النتائج المعدّلة بعد اعتمادها.",
            _ => "رُفض اعتراضك."
        };

        string? allocation = null;
        if (app.CompetitionLineId is not null && app.Status is RecruitmentApplicationStatus.Ranked or RecruitmentApplicationStatus.OfferExtended
            or RecruitmentApplicationStatus.OfferAccepted or RecruitmentApplicationStatus.Hired)
        {
            var lineTitle = await db.CompetitionLines.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .Where(l => l.Id == app.CompetitionLineId && l.TenantId == app.TenantId).Select(l => l.Title).FirstOrDefaultAsync(ct);
            allocation = $"مرشح للتعيين على {lineTitle}";
        }
        var reserves = app.Status == RecruitmentApplicationStatus.ReserveListed
            ? await db.CompetitionReserveEntries
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .AsNoTracking()
                .Where(e => e.TenantId == app.TenantId && e.RecruitmentApplicationId == app.Id && e.Status == CompetitionReserveEntryStatus.Active)
                .Select(e => new { e.CompetitionLine!.Title, e.ReserveUntil })
                .ToListAsync(ct)
            : [];

        return new CandidatePortalCompetitionCard(
            competition.Number,
            competition.Title,
            preferences,
            papers.Select(p => new CandidatePortalExamRow(
                p.ExamType.ToString(),
                ExamAdmissionCardDocumentServiceLabel(p.ExamType),
                p.Title,
                $"{FormatDate(p.ScheduledAt)} {FormatTime(p.ScheduledAt)}",
                p.Venue,
                p.SeatNumber,
                p.ExamNumber,
                p.Issued)).ToList(),
            result,
            canObject,
            deadline is DateTime d ? FormatDate(d) : null,
            deadline is DateTime closed && now > closed && objection is null ? $"انتهت مدة الاعتراض في {FormatDate(closed)}" : null,
            objectionStatus,
            papers.Count > 1 ? papers.Select(p => (p.ExamType.ToString(), ExamAdmissionCardDocumentServiceLabel(p.ExamType))).ToList() : [],
            allocation,
            reserves.Select(r => $"في قائمة الاحتياط على {r.Title} حتى {r.ReserveUntil.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}").ToList());
    }

    private static string ExamAdmissionCardDocumentServiceLabel(ExamSessionType type) =>
        HR.Core.Infrastructure.Services.Recruitment.Competitions.ExamAdmissionCardDocumentService.ExamTypeLabel(type);

    private static string TypeOf(HR.Core.Infrastructure.Services.Recruitment.Competitions.ExamAdmissionCardView card) =>
        card.Data.ExamTypeLabel switch { "كتابي" => nameof(ExamSessionType.Written), "عملي" => nameof(ExamSessionType.Practical), _ => nameof(ExamSessionType.Oral) };

    // ------------------------------------------------------------------ offer

    /// <summary>
    /// Accepts the session's issued offer: the checkbox and the full name typed to match the application are required;
    /// an expired offer is refused by <see cref="RecruitmentOfferService.AcceptAsync(Guid, string, string?, Guid?, Guid?, CancellationToken)"/>.
    /// </summary>
    public Task<CandidatePortalActionResult> AcceptOfferAsync(
        CandidatePortalContext session,
        bool acknowledged,
        string? typedFullName,
        CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            if (!acknowledged)
                return CandidatePortalActionResult.Fail(nameof(CandidatePortalAcceptForm.Acknowledge), AcknowledgeRequiredMessage);
            if (string.IsNullOrWhiteSpace(typedFullName))
                return CandidatePortalActionResult.Fail(nameof(CandidatePortalAcceptForm.FullName), FullNameRequiredMessage);

            var app = await LoadSessionApplicationAsync(session, ct);
            if (app?.ActiveOfferLetter is not { Status: OfferStatus.Issued } offer)
                return CandidatePortalActionResult.Fail(string.Empty, NoOfferMessage);

            var citizenName = await db.CitizenApplications
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .Where(c => c.Id == session.CitizenApplicationId && c.TenantId == session.TenantId)
                .Select(c => c.FullName)
                .FirstOrDefaultAsync(ct);
            if (!NameMatches(typedFullName, app.ApplicantFullName) && !NameMatches(typedFullName, citizenName))
                return CandidatePortalActionResult.Fail(nameof(CandidatePortalAcceptForm.FullName), NameMismatchMessage);

            var (success, error) = await offers.AcceptAsync(
                offer.Id, OfferAcceptedBy.Candidate, note: null, evidenceFileId: null, actorUserId: null, ct);
            return success ? CandidatePortalActionResult.Ok : CandidatePortalActionResult.Fail(string.Empty, error ?? ActionFailedMessage);
        });

    /// <summary>
    /// Declines the session's issued, unexpired offer with a reason (<see cref="OfferDeclinedBy.Candidate"/>, no user), then
    /// the same HCM-027 next-candidate draft as an HR decline (<see cref="RecruitmentOfferService.DeclineAndDraftNextAsync"/>).
    /// </summary>
    public Task<CandidatePortalActionResult> DeclineOfferAsync(
        CandidatePortalContext session,
        string? reason,
        CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            var app = await LoadSessionApplicationAsync(session, ct);
            if (app?.ActiveOfferLetter is not { Status: OfferStatus.Issued } offer)
                return CandidatePortalActionResult.Fail(string.Empty, NoOfferMessage);
            if (!OfferWorkflowRules.CanAccept(offer))
                return CandidatePortalActionResult.Fail(string.Empty, ExpiredOfferReplyMessage);

            var result = await offers.DeclineAndDraftNextAsync(offer.Id, reason, OfferDeclinedBy.Candidate, actorUserId: null, ct: ct);
            if (!result.Success)
                return CandidatePortalActionResult.Fail(
                    result.Error is not null && result.Error.StartsWith("سبب", StringComparison.Ordinal) ? nameof(CandidatePortalTextForm.Text) : string.Empty,
                    result.Error ?? ActionFailedMessage);
            LogNextDraft("decline", result.NextDraft, result.NextDraftError);
            return CandidatePortalActionResult.Ok;
        });

    /// <summary>The offer letter PDF of the session's Issued/Accepted offer (with its verification code), or null.</summary>
    public Task<(byte[] Pdf, string FileName)?> GetOfferLetterAsync(CandidatePortalContext session, CancellationToken ct = default) =>
        InSessionTenantAsync<(byte[], string)?>(session, async () =>
        {
            var app = await LoadSessionApplicationAsync(session, ct);
            if (app?.ActiveOfferLetter is not { } offer || !OfferLetterDocumentService.CanDownload(offer.Status))
                return null;
            var document = await offerDocuments.GetAsync(offer.Id, ct);
            if (document is null || !document.CanDownload)
                return null;

            var pdf = OfferLetterDocument.Generate(document);
            AddPortalAudit(session, app.Id, "CandidatePortalOfferLetterDownloaded", new
            {
                actor = Actor,
                offerId = offer.Id,
                verificationCode = document.VerificationCode
            });
            await db.SaveChangesAsync(ct);
            return (pdf, $"offer-letter-{document.Data.OfferNumber}.pdf");
        });

    // ------------------------------------------------------------------ withdraw

    /// <summary>
    /// Withdraws the session's application after the in-page confirmation (DECISIONS I: before OfferAccepted). The active
    /// offer is settled first; when one was, the same next-candidate draft as a decline follows.
    /// </summary>
    public Task<CandidatePortalActionResult> WithdrawAsync(
        CandidatePortalContext session,
        string? reason,
        bool confirmed,
        CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            if (session.RecruitmentApplicationId is not Guid appId)
                return CandidatePortalActionResult.Fail(string.Empty, NoApplicationMessage);
            if (!confirmed)
                return CandidatePortalActionResult.Fail(nameof(CandidatePortalWithdrawForm.Confirm), WithdrawConfirmRequiredMessage);

            var (success, error, settledOfferId) = await selection.WithdrawByCandidateAsync(appId, reason, offers, ct);
            if (!success)
            {
                var field = error is not null && error.StartsWith("سبب", StringComparison.Ordinal)
                    ? nameof(CandidatePortalWithdrawForm.Reason)
                    : string.Empty;
                return CandidatePortalActionResult.Fail(field, error ?? ActionFailedMessage);
            }

            if (settledOfferId is Guid offerId)
            {
                var (draft, draftError) = await offers.DraftNextOfferAfterSettlementAsync(offerId, actorUserId: null, ct);
                LogNextDraft("withdrawal", draft, draftError);
            }
            else if (competitionReserve is not null)
            {
                // HCM-031 (decision 8): an allocated competition candidate withdrew before any offer — recall the line reserve.
                await competitionReserve.RecallAfterWithdrawalAsync(appId, actorUserId: null, ct);
            }
            return CandidatePortalActionResult.Ok;
        });

    // ------------------------------------------------------------------ appeal

    /// <summary>
    /// Files the appeal (<see cref="RecruitmentAppealFiledBy.Candidate"/>, no user). The window and "no earlier appeal" are
    /// checked again by the service, never taken from the page.
    /// </summary>
    public Task<CandidatePortalActionResult> FileAppealAsync(
        CandidatePortalContext session,
        string? reason,
        CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            if (session.RecruitmentApplicationId is not Guid appId)
                return CandidatePortalActionResult.Fail(string.Empty, NoApplicationMessage);

            var (appeal, error) = await appeals.FileAppealAsync(
                appId, reason, RecruitmentAppealFiledBy.Candidate, actorUserId: null, ct: ct);
            if (appeal is not null)
                return CandidatePortalActionResult.Ok;
            var field = error is not null && error.StartsWith("سبب", StringComparison.Ordinal)
                ? nameof(CandidatePortalTextForm.Text)
                : string.Empty;
            return CandidatePortalActionResult.Fail(field, error ?? ActionFailedMessage);
        });

    /// <summary>Answers the reviewer's information request on the session application's pending appeal.</summary>
    public Task<CandidatePortalActionResult> AnswerAppealInfoRequestAsync(
        CandidatePortalContext session,
        string? info,
        CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            if (session.RecruitmentApplicationId is not Guid appId)
                return CandidatePortalActionResult.Fail(string.Empty, NoApplicationMessage);

            var pending = await db.RecruitmentAppeals.AsNoTracking()
                .Where(a => a.RecruitmentApplicationId == appId
                    && a.TenantId == session.TenantId
                    && a.Status == RecruitmentAppealStatus.Pending)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (pending is null || !pending.IsAwaitingInfo)
                return CandidatePortalActionResult.Fail(string.Empty, NoAppealInfoRequestMessage);

            var (success, error) = await appeals.ResubmitAsync(pending.Id, info, actorUserId: null, actorUserName: Actor, ct);
            if (!success)
            {
                var field = error is not null && error.StartsWith("المعلومات", StringComparison.Ordinal)
                    ? nameof(CandidatePortalTextForm.Text)
                    : string.Empty;
                return CandidatePortalActionResult.Fail(field, error ?? ActionFailedMessage);
            }

            AddPortalAudit(session, appId, "CandidatePortalAppealInfoSubmitted", new { actor = Actor, appealId = pending.Id });
            await db.SaveChangesAsync(ct);
            return CandidatePortalActionResult.Ok;
        });

    // ------------------------------------------------------------------ documents

    /// <summary>
    /// Uploads missing required documents while intake is open — session 2's path and limits: local checks
    /// (<see cref="CandidateDocumentFileValidator"/>), relay to File.Storage, staging with the explicit tenant
    /// (<see cref="CandidateDocumentService.Stage"/>), <see cref="UnifiedIntakeService.ReevaluateDocumentFlagAsync"/> and one save.
    /// </summary>
    public Task<CandidatePortalActionResult> UploadDocumentsAsync(
        CandidatePortalContext session,
        IReadOnlyList<(string DocumentType, IFormFile File, string FieldName)> attached,
        CancellationToken ct = default) =>
        InSessionTenantAsync(session, async () =>
        {
            if (session.RecruitmentApplicationId is not Guid appId)
                return CandidatePortalActionResult.Fail(string.Empty, NoApplicationMessage);
            var app = await db.RecruitmentApplications
                .Include(a => a.Requisition)
                    .ThenInclude(r => r!.FundedPosition)
                .FirstOrDefaultAsync(a => a.Id == appId && a.TenantId == session.TenantId, ct);
            if (app is null)
                return CandidatePortalActionResult.Fail(string.Empty, NoApplicationMessage);

            var existing = await db.CandidateDocuments.AsNoTracking()
                .Where(d => d.RecruitmentApplicationId == app.Id)
                .Select(d => d.DocumentType)
                .ToListAsync(ct);
            var intakeError = await DocumentIntakeErrorAsync(session, app, existing.Count, ct);
            if (intakeError is not null)
                return CandidatePortalActionResult.Fail(string.Empty, intakeError);
            var missing = await MissingDocumentTypesAsync(app, existing, ct);
            if (missing.Count == 0)
                return CandidatePortalActionResult.Fail(string.Empty, NoMissingDocumentsMessage);
            if (attached.Count == 0)
                return CandidatePortalActionResult.Fail(string.Empty, NoFilesMessage);

            var notMissing = attached.Where(a => !missing.Contains(a.DocumentType)).Select(a => a.FieldName).Distinct().ToList();
            if (notMissing.Count > 0)
                return new CandidatePortalActionResult(false, notMissing.Select(f => (f, NotMissingTypeMessage)).ToList());
            var limits = Settings.CandidateDocuments;
            if (existing.Count + attached.Count > limits.MaxFiles)
                return CandidatePortalActionResult.Fail(string.Empty, CandidateDocumentFileValidator.TooManyFilesMessage(limits.MaxFiles));

            var outcome = await CandidateDocumentFileValidator.PrepareAsync(
                attached.Select(a => new CandidateDocumentFileValidator.Input(a.DocumentType, a.File, a.FieldName)).ToList(),
                limits,
                ct);
            if (!outcome.IsValid)
                return new CandidatePortalActionResult(false, outcome.Errors);

            var uploads = await candidateDocuments.UploadAllAsync(session.TenantId, outcome.Files, ct);
            if (uploads is null)
                return CandidatePortalActionResult.Fail(string.Empty, CandidateDocumentService.UploadFailedMessage);

            candidateDocuments.Stage(session.TenantId, app.Id, session.CitizenApplicationId, uploads, CandidateDocument.UploadedByCandidate);
            await unifiedIntake.ReevaluateDocumentFlagAsync(app, ct);
            AddPortalAudit(session, app.Id, "CandidatePortalDocumentsUploaded", new
            {
                actor = Actor,
                count = uploads.Count,
                types = uploads.Select(u => u.File.DocumentType).Distinct().ToList()
            });
            await db.SaveChangesAsync(ct);
            return CandidatePortalActionResult.Ok;
        });

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Runs <paramref name="action"/> with the session's tenant as the ambient tenant and restores the previous one
    /// (RecruitmentOfferExpirer precedent, DECISIONS H.1).
    /// </summary>
    internal static async Task<T> InSessionTenantAsync<T>(CandidatePortalContext session, Func<Task<T>> action)
    {
        var previous = TenantIdFlow.Get();
        TenantIdFlow.Set(session.TenantId);
        try
        {
            return await action();
        }
        finally
        {
            TenantIdFlow.Set(previous);
        }
    }

    /// <summary>
    /// The typed name matches the application's name: case, spacing, diacritics, tatweel and the common Arabic letter
    /// variants (أ/إ/آ → ا, ة → ه, ى → ي) are ignored.
    /// </summary>
    public static bool NameMatches(string? typed, string? expected)
    {
        if (string.IsNullOrWhiteSpace(typed) || string.IsNullOrWhiteSpace(expected))
            return false;
        return string.Equals(NormalizeName(typed), NormalizeName(expected), StringComparison.Ordinal);
    }

    internal static string NormalizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var ch in value.Normalize(NormalizationForm.FormC))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark || ch == 'ـ')
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                    builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                _ => char.ToLowerInvariant(ch)
            });
            lastWasSpace = false;
        }
        return builder.ToString().TrimEnd();
    }

    private Task<RecruitmentApplication?> LoadSessionApplicationAsync(CandidatePortalContext session, CancellationToken ct) =>
        session.RecruitmentApplicationId is Guid appId ? LoadApplicationAsync(session, appId, ct) : Task.FromResult<RecruitmentApplication?>(null);

    private Task<RecruitmentApplication?> LoadApplicationAsync(CandidatePortalContext session, Guid appId, CancellationToken ct) =>
        db.RecruitmentApplications.AsNoTracking()
            .Include(a => a.ActiveOfferLetter)
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(a => a.Id == appId && a.TenantId == session.TenantId, ct);

    /// <summary>HCM-031 (PLAN-31): a competition application asks for the union of its requisitions' required types.</summary>
    private async Task<IReadOnlyList<string>> MissingDocumentTypesAsync(RecruitmentApplication app, IEnumerable<string> provided, CancellationToken ct)
    {
        var required = app.CompetitionId is Guid competitionId && app.RequisitionId is null
            ? await HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionIntakeService.RequiredDocumentTypesAsync(db, app.TenantId, competitionId, ct)
            : app.Requisition?.RequiredDocumentTypes;
        return CandidateDocumentTypes.Missing(CandidateDocumentTypes.RequiredFor(app.IntakeChannel, required), provided);
    }

    /// <summary>
    /// Null while the candidate may still add documents: the application is open and the requisition still takes
    /// applications (same rule as the apply form: <see cref="RecruitmentApplicationIntakeRules.ValidatePublishedRequisition"/>
    /// with the posting's closing date), within <c>Recruitment:CandidateDocuments:MaxFiles</c>.
    /// </summary>
    private async Task<string?> DocumentIntakeErrorAsync(
        CandidatePortalContext session,
        RecruitmentApplication app,
        int existingCount,
        CancellationToken ct)
    {
        if (SelectionWorkflowRules.TerminalStatuses.Contains(app.Status))
            return DocumentsClosedMessage;
        if (app.CompetitionId is Guid competitionId && app.RequisitionId is null)
        {
            // HCM-031 (PLAN-31): documents can be added while the competition still takes applications.
            var competition = await db.RecruitmentCompetitions
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .AsNoTracking()
                .FirstAsync(c => c.Id == competitionId && c.TenantId == app.TenantId, ct);
            if (HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionIntakeService.IntakeError(
                    competition, DateOnly.FromDateTime(DateTime.UtcNow)) is not null)
                return DocumentsClosedMessage;
            return existingCount >= Settings.CandidateDocuments.MaxFiles
                ? CandidateDocumentFileValidator.TooManyFilesMessage(Settings.CandidateDocuments.MaxFiles)
                : null;
        }
        if (app.Requisition is null)
            return DocumentsClosedMessage;
        var closingDate = await db.CitizenApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(c => c.Id == session.CitizenApplicationId && c.TenantId == session.TenantId)
            .Select(c => c.PublicJobPosting != null ? c.PublicJobPosting.ClosingDate : null)
            .FirstOrDefaultAsync(ct);
        if (RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(
                app.Requisition, app.Requisition.FundedPosition, DateOnly.FromDateTime(DateTime.UtcNow), closingDate) is not null)
            return DocumentsClosedMessage;
        if (existingCount >= Settings.CandidateDocuments.MaxFiles)
            return CandidateDocumentFileValidator.TooManyFilesMessage(Settings.CandidateDocuments.MaxFiles);
        return null;
    }

    private async Task<CandidatePortalOfferCard?> OfferCardAsync(RecruitmentApplication app, DateTime now, CancellationToken ct)
    {
        if (app.ActiveOfferLetter is not { Status: OfferStatus.Issued or OfferStatus.Accepted } offer)
            return null;
        var document = await offerDocuments.GetAsync(offer.Id, ct);
        if (document is null)
            return null;

        var data = document.Data;
        var expired = offer.Status == OfferStatus.Issued && offer.ExpiryDate is DateTime expiry && expiry < now;
        return new CandidatePortalOfferCard(
            data.OfferNumber,
            data.PositionTitle,
            data.GradeCode,
            OfferLetterDocumentData.FormatSalary(data.OfferedSalary),
            OfferLetterDocumentData.FormatDate(data.ProposedStartDate),
            data.ExpiresOn is null ? null : OfferLetterDocumentData.FormatDate(data.ExpiresOn),
            document.VerificationCode,
            IsAccepted: offer.Status == OfferStatus.Accepted,
            IsExpired: expired,
            CanRespond: OfferWorkflowRules.CanAccept(offer) && app.Status == RecruitmentApplicationStatus.OfferExtended,
            CanDownload: document.CanDownload);
    }

    private async Task<CandidatePortalAppealCard?> AppealCardAsync(RecruitmentApplication app, DateTime now, CancellationToken ct)
    {
        var appeal = await db.RecruitmentAppeals.AsNoTracking()
            .Where(a => a.RecruitmentApplicationId == app.Id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (appeal is { Status: RecruitmentAppealStatus.Pending })
            return appeal.IsAwaitingInfo
                ? new CandidatePortalAppealCard(CandidatePortalAppealState.AwaitingInfo, null, appeal.InfoRequest)
                : new CandidatePortalAppealCard(CandidatePortalAppealState.Pending, null, null);
        if (appeal is not null || app.AppealStatus == RecruitmentAppealStatus.Decided)
            return new CandidatePortalAppealCard(CandidatePortalAppealState.Decided, null, null);
        if (app.Status != RecruitmentApplicationStatus.Rejected)
            return null;

        var rejectedAt = app.RejectedAt ?? app.ReviewedAt;
        var windowEnd = rejectedAt?.AddDays(Settings.AppealWindowDays);
        var error = appeals.GetFilingError(app, now);
        if (error is null)
            return new CandidatePortalAppealCard(
                CandidatePortalAppealState.CanFile, windowEnd is DateTime open ? FormatDate(open) : null, null);
        if (error == RecruitmentAppealWorkflowRules.WindowExpiredMessage && windowEnd is DateTime closed)
            return new CandidatePortalAppealCard(CandidatePortalAppealState.WindowClosed, FormatDate(closed), null);
        return null;
    }

    private void AddPortalAudit(CandidatePortalContext session, Guid recruitmentApplicationId, string action, object changes) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = session.TenantId,
            EntityType = nameof(RecruitmentApplication),
            EntityId = recruitmentApplicationId,
            Action = action,
            UserId = null,
            ChangedFields = JsonSerializer.Serialize(changes)
        });

    private void LogNextDraft(string trigger, OfferLetter? draft, string? error)
    {
        if (draft is not null)
            logger.LogInformation("HCM-030 portal {Trigger}: next-candidate offer {OfferNumber} drafted.", trigger, draft.OfferNumber);
        else if (error is not null)
            logger.LogInformation("HCM-030 portal {Trigger}: no next-candidate draft ({Reason}).", trigger, error);
    }

    private static string LegacyStatusLabel(CitizenApplicationStatus status) => status switch
    {
        CitizenApplicationStatus.Accepted => "مقبول",
        CitizenApplicationStatus.Rejected => "مرفوض",
        CitizenApplicationStatus.Shortlisted => "في القائمة المختصرة",
        CitizenApplicationStatus.UnderReview => "قيد المراجعة",
        _ => "مستلَم"
    };

    private static string PlaceText(InterviewMode mode, string? location) =>
        mode == InterviewMode.Remote
            ? string.IsNullOrWhiteSpace(location) ? "عن بُعد" : $"عن بُعد — {location}"
            : string.IsNullOrWhiteSpace(location) ? "حضوري" : location!;

    private static string InterviewStatusLabel(InterviewSlotStatus status) => status switch
    {
        InterviewSlotStatus.Completed => "مكتملة",
        InterviewSlotStatus.Cancelled => "ملغاة",
        _ => "مجدولة"
    };

    private static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);

    /// <summary>Invariant culture: the dev/host locale may be Hijri (ar-SA).</summary>
    public static string FormatDate(DateTime utc) => Local(utc).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    internal static string FormatTime(DateTime utc) => Local(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " ميغابايت"
            : Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " كيلوبايت";
}
