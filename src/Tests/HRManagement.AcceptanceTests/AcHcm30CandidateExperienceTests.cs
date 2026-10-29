using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-30a..j (PLAN-30): the citizen side through the gateway <c>/careers</c> (anonymous, manual cookie jar — no
/// CookieContainer, as the HR suites: <c>careers_session</c> + antiforgery), HR steps with the existing personas, candidate
/// messages and one-time codes read from the FileDrop folder (<c>artifacts/acceptance/candidate-notifications</c>,
/// docs/COM_001_NOTIFICATIONS.md). Fixtures come from <c>AcceptanceCandidateExperiencePatch</c> (requisition with criteria
/// and required documents, the backfill rows, the retention pair and its held twin); the offer journey creates its own
/// requisition through the UI (as AC-HCM-27..29). Ids and references the UI shows only through TempData are read by SQL.
/// Test-only SQL preconditions are documented at each use. One-time codes and national ids never reach the evidence.
/// </summary>
[RequiredAcceptance]
public class AcHcm30CandidateExperienceTests
{
    private const string Ac = "AC-HCM-30";
    private static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string CxRequisition = AcceptanceSeedIds.CandidateExperienceRequisitionId.ToString("D");
    private static readonly string CxPosting = AcceptanceSeedIds.CandidateExperiencePostingId.ToString("D");
    private static readonly string[] BlockingStatuses = ["Draft", "PendingApproval", "Approved", "Published"];

    private const int AppSubmitted = 0, AppUnderReview = 1, AppShortlisted = 2, AppRejected = 3, AppInterviewScheduled = 5,
        AppRanked = 7, AppOfferAccepted = 9, AppOfferDeclined = 11;
    private const int OfferIssued = 2, OfferAccepted = 3, OfferDeclined = 4;
    private const int AppealPending = 1;
    // Recruitment:AppealWindowDays is not overridden in acceptance (default 14; the 30h negative moves RejectedAt 15 days back).
    private const int AppealWindowDays = 14;

    // Arabic UI strings asserted as-is.
    private const string NeutralOtpMessage = "إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.";
    private const string LockedMessage = "تم إيقاف الدخول مؤقتاً بسبب تكرار إدخال رمز خاطئ. يرجى المحاولة لاحقاً.";
    private const string NotAcceptedLabel = "غير مقبول";
    private const string AppealOpenText = "يمكنك الاستئناف";
    private const string AppealClosedText = "انتهت مدة الاستئناف";
    private const string AppealButton = "تقديم الاستئناف";
    private const string InterviewLabel = "مدعو للمقابلة";
    private const string CleanLabel = "سليم";
    private const string QuarantinedLabel = "محجور — غير متاح";
    private const string BelowQualificationLabel = "دون المؤهل المطلوب";
    private const string NextCandidateBanner = "المرشح التالي بالجدارة";
    // CandidateDocumentService.UploadFailedMessage: the apply page when File.Storage cannot be reached.
    private const string UploadFailedMessage = "تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.";

    private const string AppealReason = "أعترض على قرار عدم القبول لأن مؤهلاتي تطابق شروط الإعلان (اختبار قبول)";
    private const string DeclineReason = "قبلت عرضاً من جهة أخرى، شكراً لكم (اختبار قبول آلي)";
    private const string OverrideReason = "خبرة عملية موثّقة تعوّض المؤهل حسب توصية لجنة الفرز";

    // Names of the AC-HCM-30i fixture pairs (AcceptanceCandidateExperiencePatch).
    private const string RetentionDueName = "مرشح احتفاظ مستحق";
    private const string RetentionHeldName = "مرشح احتفاظ محجوز قانونياً";

    private static readonly byte[] CleanPdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    // ── AC-HCM-30a/b/h: apply → HR list → reject → /careers/my and /track → appeal ─────────────────────────

    [SkippableFact]
    public async Task Ac30_a_b_h_public_apply_rejection_status_and_candidate_appeal()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("30a-b-h", "AC-HCM-30a/b/h public apply → HR → rejection → portal status → appeal");
        await RequireCandidateExperienceRequisitionAsync(s);
        WriteScenarioSteps(s, """
            1. AC-30a: a citizen applies on the seeded requisition REQ-2026-A-CX01 through the gateway (/careers/{posting}) with email +
               notification consent (documents: AC-30c) → RecruitmentApplication Submitted at once, IntakeChannel Public, linked to the
               citizen row; central-a sees it in RecruitmentApplications?RequisitionId=….
            2. AC-30b: central-a rejects with a note. The citizen signs in on /careers/my (reference + national id → one-time code read
               from the FileDrop file of key otp:{challengeId}:Email) → «غير مقبول» and «يمكنك الاستئناف حتى …»; /careers/track shows
               «غير مقبول».
            3. AC-30h: the candidate files the appeal from /careers/my → RecruitmentAppeals row FiledBy Candidate, Pending; listed for HR.
            """);

        // AC-30a
        var citizen = NewCitizen("a");
        var apply = await s.Citizen("30a").ApplyAsync("30a-apply", CxPosting, citizen, []);
        Assert.Equal("/careers/submitted", apply.FinalPath);
        var row = await RequireCitizenRowAsync(citizen);
        Assert.Equal($"{AppSubmitted}|Public|{CxRequisition}", await Sql(
            $"SELECT \"Status\"::text || '|' || \"IntakeChannel\" || '|' || \"RequisitionId\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{row.ApplicationId}';"));
        var list = await s.Central.GetAsync("30a-hr-list", $"ui/hr-core/RecruitmentApplications?RequisitionId={CxRequisition}&SearchTerm={Uri.EscapeDataString(citizen.FullName)}");
        Assert.Contains(citizen.FullName, list);
        Assert.Contains(row.ApplicationId, list);

        // AC-30b
        await s.Central.PostAsync("30b-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(row.ApplicationId),
            [new("id", row.ApplicationId), new("status", "Rejected"), new("rejectionNote", "AC-HCM-30b عدم استيفاء متطلبات المقابلة الأولية")]);
        Assert.Equal(AppRejected, await AppStatusAsync(row.ApplicationId));
        var portal = s.Citizen("30b");
        var page = await SignInAsync(s, portal, "30b", citizen, row);
        Assert.Contains(NotAcceptedLabel, page);
        Assert.Contains(AppealOpenText, page);
        Assert.Contains(AppealButton, page);
        // The card must name the deadline: RejectedAt + the appeal window (Damascus date, ±1 day for the time zone).
        var rejectedAt = await Sql($"SELECT to_char(\"RejectedAt\", 'YYYY-MM-DD') FROM \"RecruitmentApplications\" WHERE \"Id\"='{row.ApplicationId}';");
        Assert.False(string.IsNullOrEmpty(rejectedAt), "30b: RejectedAt was not set by the rejection.");
        var deadline = Regex.Match(page, AppealOpenText + @" على قرار عدم القبول حتى\s*<bdi[^>]*>(\d{4}/\d{2}/\d{2})</bdi>");
        Assert.True(deadline.Success, "30b: the appeal card shows no deadline («يمكنك الاستئناف … حتى <date>»).");
        var expectedDeadline = DateTime.ParseExact(rejectedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(AppealWindowDays);
        var shownDeadline = DateTime.ParseExact(deadline.Groups[1].Value, "yyyy/MM/dd", CultureInfo.InvariantCulture);
        Assert.InRange((shownDeadline - expectedDeadline).TotalDays, -1, 1);
        var track =await s.Citizen("30b-track").PostAsync("30b-track", "careers/track", "careers/track",
            [new("referenceNumber", row.Reference), new("nationalId", citizen.NationalId)]);
        Assert.Contains(NotAcceptedLabel, track.Body);

        // AC-30h (positive)
        var appealed = await portal.PostAsync("30h-appeal", "careers/my/appeal", "careers/my", [new("Text", AppealReason)]);
        Assert.Equal("/careers/my", appealed.FinalPath);
        var appeal = await Sql(
            $"SELECT \"Id\" || '|' || \"FiledBy\" || '|' || \"Status\"::text FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{row.ApplicationId}';");
        var appealParts = appeal.Split('|');
        Assert.Equal(["Candidate", AppealPending.ToString(CultureInfo.InvariantCulture)], appealParts[1..]);
        var hrAppeals = await s.Central.GetAsync("30h-hr-appeals", "ui/hr-core/RecruitmentAppeals");
        Assert.Contains(appealParts[0], hrAppeals);

        WriteDbChecks(s, new { citizenApplication = row.CitizenId, recruitmentApplication = row.ApplicationId, appeal });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30c: documents scanned and downloadable; refused files; EICAR quarantined ────────────────────

    [SkippableFact]
    public async Task Ac30_c_documents_are_scanned_refused_files_and_eicar_is_quarantined()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("30c", "AC-HCM-30c CV + certificate → Clean on HR Details; .exe and > 5 MB refused; EICAR quarantined, not downloadable");
        await RequireCandidateExperienceRequisitionAsync(s);
        WriteScenarioSteps(s, """
            1. AC-30c: an apply with a CV named malware.exe (MZ bytes) → the form is shown again, nothing is saved.
            2. AC-30c: an apply with a 5 MB + 1 byte PDF → refused, nothing is saved.
               (Steps 1–2 are refused before any upload, so they run without File.Storage.)
            3. AC-30c: a citizen applies with a CV and a certificate (PDF) → both listed on HR Details (CandidateDocuments Cv, Certificate)
               and «سليم» after the ClamAV scan; the CV downloads through the HR proxy (%PDF). Skipped when the apply page reports
               «تعذّر رفع المستندات حالياً…» (File.Storage not in the acceptance stack).
            4. AC-30c: an apply with eicar.pdf (minimal PDF + EICAR string, the AC-28 pattern) is accepted; File.Storage marks the file
               Infected/Quarantined; HR Details shows «محجور — غير متاح» without a download link and the HR download proxy refuses it.
            """);

        var exe = NewCitizen("c-exe");
        var exeResult = await s.Citizen("30c-exe").ApplyAsync("30c-apply-exe", CxPosting, exe,
            [("CvFile", "malware.exe", "application/octet-stream", Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0 not a document"))]);
        Assert.NotEqual("/careers/submitted", exeResult.FinalPath);
        Assert.Equal(0, await CitizenRowCountAsync(exe));

        var big = NewCitizen("c-big");
        var oversized = new byte[5 * 1024 * 1024 + 1];
        CleanPdf.CopyTo(oversized, 0);
        var bigResult = await s.Citizen("30c-big").ApplyAsync("30c-apply-oversized", CxPosting, big,
            [("CvFile", "big.pdf", "application/pdf", oversized)]);
        Assert.NotEqual("/careers/submitted", bigResult.FinalPath);
        Assert.Equal(0, await CitizenRowCountAsync(big));

        var withDocs = NewCitizen("c-docs");
        var docsApply = await s.Citizen("30c-docs").ApplyAsync("30c-apply-documents", CxPosting, withDocs,
        [
            ("CvFile", "cv.pdf", "application/pdf", CleanPdf),
            ("CertificateFiles", "certificate.pdf", "application/pdf", CleanPdf)
        ]);
        var documentUploadFailed = docsApply.FinalPath != "/careers/submitted"
                                   && docsApply.Body.Contains(UploadFailedMessage, StringComparison.Ordinal);
        if (documentUploadFailed)
        {
            WriteDbChecks(s, new { exeRows = 0, oversizedRows = 0, documentsApply = "upload failed (File.Storage unavailable)" });
            s.Recorder.FlushTiming();
        }
        AcceptanceGate.RequireData(s.Recorder, !documentUploadFailed,
            "خدمة تخزين الملفات (File.Storage) غير موجودة في بيئة القبول — خطوات المستند السليم وملف EICAR تحتاجها؛ نجح رفض ملف .exe والملف الأكبر من 5 ميغابايت",
            "File.Storage not in acceptance stack (MinIO image) — the clean/EICAR document steps need it; the .exe and > 5 MB refusals passed.");
        Assert.Equal("/careers/submitted", docsApply.FinalPath);
        var docsRow = await RequireCitizenRowAsync(withDocs);
        var docs = await Sql(
            $"SELECT string_agg(\"DocumentType\" || ':' || \"Id\" || ':' || \"FileId\", ',' ORDER BY \"DocumentType\") FROM \"CandidateDocuments\" WHERE \"RecruitmentApplicationId\"='{docsRow.ApplicationId}';");
        var docParts = docs.Split(',').Select(d => d.Split(':')).ToList();
        Assert.Equal(["Certificate", "Cv"], docParts.Select(d => d[0]).ToArray());
        await WaitForScanAsync(s, "30c-scan-clean", docParts.Select(d => d[2]), ["Clean"]);
        var cleanDetails = await WaitForPageAsync(s, s.Central, "30c-hr-details-clean", AppDetails(docsRow.ApplicationId),
            html => Regex.Matches(html, $">{CleanLabel}</span>").Count >= 2);
        Assert.Contains("cv.pdf", cleanDetails);
        Assert.Contains("certificate.pdf", cleanDetails);
        var cvId = docParts.Single(d => d[0] == "Cv")[1];
        var (cvStatus, cvType, cvHead) = await s.Central.GetRawAsync("30c-cv-download", $"ui/hr-core/RecruitmentApplications/CandidateDocument/{cvId}");
        Assert.Equal(HttpStatusCode.OK, cvStatus);
        Assert.Equal("application/pdf", cvType);
        Assert.Equal("%PDF", cvHead);

        var infected = NewCitizen("c-eicar");
        var eicar = CleanPdf.Concat(Encoding.ASCII.GetBytes(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*\n")).ToArray();
        var eicarResult = await s.Citizen("30c-eicar").ApplyAsync("30c-apply-eicar", CxPosting, infected,
            [("CvFile", "eicar.pdf", "application/pdf", eicar)]);
        Assert.Equal("/careers/submitted", eicarResult.FinalPath);
        var row = await RequireCitizenRowAsync(infected);
        var doc = (await Sql(
            $"SELECT \"Id\" || '|' || \"FileId\" FROM \"CandidateDocuments\" WHERE \"RecruitmentApplicationId\"='{row.ApplicationId}';")).Split('|');
        Assert.Equal(2, doc.Length);
        var scan = await WaitForScanAsync(s, "30c-scan-infected", [doc[1]], ["Infected", "Quarantined"]);
        var details = await WaitForPageAsync(s, s.Central, "30c-hr-details-quarantined", AppDetails(row.ApplicationId),
            html => html.Contains($">{QuarantinedLabel}</span>", StringComparison.Ordinal));
        Assert.DoesNotContain($"CandidateDocument/{doc[0]}", details);
        var (status, type, head) = await s.Central.GetRawAsync("30c-eicar-download", $"ui/hr-core/RecruitmentApplications/CandidateDocument/{doc[0]}");
        Assert.False(status == HttpStatusCode.OK && (type == "application/pdf" || head == "%PDF"),
            $"The quarantined document was served ({(int)status} {type}).");

        WriteDbChecks(s, new { documentsApplication = docsRow.ApplicationId, documents = docs, exeRows = 0, oversizedRows = 0, eicarApplication = row.ApplicationId, eicarFile = doc[1], scan, download = (int)status });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30d/e and the 30b negative: eligibility flags → screening → interview message once → no hire step ──

    [SkippableFact]
    public async Task Ac30_d_e_eligibility_flags_interview_message_once_and_no_hire_step()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("30d-e", "AC-HCM-30d/e flags + override reason; interview → one FileDrop message; 30b negative");
        await RequireCandidateExperienceRequisitionAsync(s);
        WriteScenarioSteps(s, """
            1. AC-30d: a citizen applies with a diploma (DIP < BACH) and 1 year of experience (< 2) → flags BelowMinQualification and
               BelowMinExperience; HR Details shows «دون المؤهل المطلوب»; the application is screened (UnderReview);
               shortlisting without a reason is refused; with a reason → Shortlisted, EligibilityOverrideReason + audit EligibilityOverride.
            2. AC-30e: central-a schedules an interview → event InterviewScheduled → exactly one FileDrop message (key {eventId}:Email)
               whose subject and body hold no national id.
            3. AC-30e (negative): test-only SQL clears NotificationsPlannedAt on that event (replan) and the dispatcher runs again →
               still one CandidateNotification for the key, the FileDrop file is not rewritten.
            4. AC-30b (negative): the shortlisted/interviewed candidate's /careers/my shows «مدعو للمقابلة» and no hire step («التعيين»).
            """);

        var citizen = NewCitizen("d", educationLevel: "DIP", yearsOfExperience: "1");
        var apply = await s.Citizen("30d").ApplyAsync("30d-apply", CxPosting, citizen, []);
        Assert.Equal("/careers/submitted", apply.FinalPath);
        var row = await RequireCitizenRowAsync(citizen);
        var flags = await Sql($"SELECT COALESCE(\"EligibilityFlags\",'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{row.ApplicationId}';");
        Assert.Contains("BelowMinQualification", flags);
        Assert.Contains("BelowMinExperience", flags);
        var details = AppDetails(row.ApplicationId);
        Assert.Contains(BelowQualificationLabel, await s.Central.GetAsync("30d-hr-flags", details));

        await s.Central.PostAsync("30d-screen", "ui/hr-core/RecruitmentApplications/SetStatus", details, [new("id", row.ApplicationId), new("status", "UnderReview")]);
        Assert.Equal(AppUnderReview, await AppStatusAsync(row.ApplicationId));
        await s.Central.PostAsync("30d-declare-coi", "ui/hr-core/RecruitmentApplications/DeclareCoi", details, [new("id", row.ApplicationId)]);
        await s.Central.PostAsync("30d-shortlist-no-reason", "ui/hr-core/RecruitmentApplications/SetStatus", details,
            [new("id", row.ApplicationId), new("status", "Shortlisted")]);
        Assert.Equal(AppUnderReview, await AppStatusAsync(row.ApplicationId));
        await s.Central.PostAsync("30d-shortlist-with-reason", "ui/hr-core/RecruitmentApplications/SetStatus", details,
            [new("id", row.ApplicationId), new("status", "Shortlisted"), new("eligibilityOverrideReason", OverrideReason)]);
        Assert.Equal(AppShortlisted, await AppStatusAsync(row.ApplicationId));
        Assert.Equal(OverrideReason, await Sql($"SELECT \"EligibilityOverrideReason\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{row.ApplicationId}';"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{row.ApplicationId}' AND \"Action\"='EligibilityOverride';"));

        // AC-30e
        await s.Central.PostAsync("30e-schedule", "ui/hr-core/RecruitmentApplications/ScheduleInterview", details, ScheduleForm(row.ApplicationId, DateTime.UtcNow.AddDays(2)));
        Assert.Equal(AppInterviewScheduled, await AppStatusAsync(row.ApplicationId));
        var eventId = await WaitForTextAsync(s, "30e-event", TimeSpan.FromSeconds(30), () => Sql(
            $"SELECT \"Id\"::text FROM \"RecruitmentApplicationEvents\" WHERE \"RecruitmentApplicationId\"='{row.ApplicationId}' AND \"EventType\"='InterviewScheduled' ORDER BY \"OccurredAt\" DESC LIMIT 1;"));
        var key = $"{eventId}:Email";
        var message = await WaitForFileDropAsync(s, "30e-filedrop", key, TimeSpan.FromMinutes(4));
        Assert.Equal("INTERVIEW_SCHEDULED", message.Template);
        Assert.DoesNotContain(citizen.NationalId, message.Subject ?? string.Empty);
        Assert.DoesNotContain(citizen.NationalId, message.Body);
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"CandidateNotifications\" WHERE \"IdempotencyKey\"='{key}';"));
        var file = FileDropPath(key);
        var writtenAt = File.GetLastWriteTimeUtc(file);

        // AC-30e (negative): replan → still one message.
        // Test-only: the dispatcher marks each event planned once; clearing the mark makes the next cycle plan it again.
        await Sql($"UPDATE \"RecruitmentApplicationEvents\" SET \"NotificationsPlannedAt\" = NULL WHERE \"Id\"='{eventId}';");
        await WaitForTextAsync(s, "30e-replanned", TimeSpan.FromMinutes(4), () => Sql(
            $"SELECT COALESCE(\"NotificationsPlannedAt\"::text,'') FROM \"RecruitmentApplicationEvents\" WHERE \"Id\"='{eventId}';"));
        // One more dispatcher cycle (60 s) for any resend.
        await Task.Delay(TimeSpan.FromSeconds(70));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"CandidateNotifications\" WHERE \"IdempotencyKey\"='{key}';"));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(file));
        var notificationStatus = await Sql($"SELECT \"Status\"::text FROM \"CandidateNotifications\" WHERE \"IdempotencyKey\"='{key}';");

        // AC-30b (negative): no hire step for a shortlisted/interviewed application.
        var portal = s.Citizen("30b-negative");
        var page = await SignInAsync(s, portal, "30b-negative", citizen, row);
        Assert.Contains(InterviewLabel, page);
        Assert.DoesNotContain("التعيين", page);

        WriteDbChecks(s, new { recruitmentApplication = row.ApplicationId, flags, eventId, notificationStatus, fileWrittenAt = writtenAt });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30f/g: OTP sign-in → decline (next candidate) → accept ────────────────────────────────────────

    [SkippableFact]
    public async Task Ac30_f_g_candidate_declines_then_next_candidate_accepts_with_otp()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("30f-g", "AC-HCM-30f/g OTP sign-in, candidate decline → next candidate, candidate acceptance");
        WriteScenarioSteps(s, """
            1. A requisition (law50) on a free graded BR1 position is created, approved and published through the UI (as AC-HCM-29).
            2. Two citizens apply through /careers; test-only SQL ranks them 1 and 2 (AC-HCM-26's scope).
            3. Rank 1's offer is created, approved (two steps) → Issued.
            4. AC-30g: rank 1 signs in with the one-time code (FileDrop) and declines with a reason → OfferDeclined, DeclinedBy Candidate,
               application OfferDeclined; HR sees «المرشح التالي بالجدارة» for rank 2 (Recruitment:AutoDraftNextOffer is off in
               acceptance; with it on a Draft offer for rank 2 is accepted instead).
            5. Rank 2's offer is created and approved → Issued. AC-30f: rank 2 signs in, downloads the offer letter (%PDF) and accepts
               (acknowledgement + full name) → offer Accepted, AcceptedBy Candidate, application OfferAccepted.
            """);

        var req = await CreatePublishedRequisitionAsync(s, "30f");
        var declining = NewCitizen("g");
        var accepting = NewCitizen("f");
        var rows = new List<CitizenRow>();
        foreach (var (citizen, rank) in new[] { (declining, 1), (accepting, 2) })
        {
            var result = await s.Citizen($"30f-apply-{rank}").ApplyAsync($"30f-apply-rank-{rank}", req.PostingId, citizen, []);
            Assert.Equal("/careers/submitted", result.FinalPath);
            var row = await RequireCitizenRowAsync(citizen);
            // Test-only precondition (AC-HCM-26's scope): ranked by merit.
            await Sql($"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"={rank}, \"AssessmentScore\"={95 - rank * 5}, " +
                      $"\"RankedAt\"=now(), \"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{row.ApplicationId}';");
            rows.Add(row);
        }
        var bounds = await RequireGradeBoundsAsync(s, req, rows[0].ApplicationId);

        // AC-30g
        var offer1 = await CreateOfferAsync(s, "30g-offer", rows[0].ApplicationId, bounds.Mid, req.Grade);
        await SubmitAndApproveAsync(s, "30g", offer1);
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer1));
        var g = s.Citizen("30g");
        var gPage = await SignInAsync(s, g, "30g", declining, rows[0]);
        Assert.Contains("بانتظار ردك", gPage);
        await g.PostAsync("30g-decline", "careers/my/offer/decline", "careers/my", [new("Category", "Salary"), new("Text", DeclineReason)]);
        Assert.Equal($"{OfferDeclined}|Candidate|{DeclineReason}", await Sql(
            $"SELECT \"Status\"::text || '|' || \"DeclinedBy\" || '|' || \"DeclineReason\" FROM \"OfferLetters\" WHERE \"Id\"='{offer1}';"));
        Assert.Equal(AppOfferDeclined, await AppStatusAsync(rows[0].ApplicationId));
        var nextDraft = await Sql($"SELECT COALESCE(MAX(\"Id\"::text),'') FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{rows[1].ApplicationId}' AND \"Status\"=0;");
        if (nextDraft.Length == 0)
        {
            var reqPage = await s.Central.GetAsync("30g-next-candidate", $"ui/hr-core/RecruitmentRequisitions/Details/{req.Id}");
            Assert.Contains(NextCandidateBanner, reqPage);
            Assert.Contains(accepting.FullName, reqPage);
        }

        // AC-30f
        var offer2 = nextDraft.Length > 0 ? nextDraft : await CreateOfferAsync(s, "30f-offer", rows[1].ApplicationId, bounds.Mid, req.Grade);
        await SubmitAndApproveAsync(s, "30f", offer2);
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer2));
        var f = s.Citizen("30f");
        await SignInAsync(s, f, "30f", accepting, rows[1]);
        var (pdfStatus, pdfType, pdfHead) = await f.GetRawAsync("30f-offer-letter", "careers/my/offer/letter");
        Assert.Equal(HttpStatusCode.OK, pdfStatus);
        Assert.Equal("application/pdf", pdfType);
        Assert.Equal("%PDF", pdfHead);
        var accepted = await f.PostAsync("30f-accept", "careers/my/offer/accept", "careers/my",
            [new("Acknowledge", "true"), new("FullName", accepting.FullName)]);
        Assert.Equal("/careers/my", accepted.FinalPath);
        Assert.Equal($"{OfferAccepted}|Candidate", await Sql(
            $"SELECT \"Status\"::text || '|' || \"AcceptedBy\" FROM \"OfferLetters\" WHERE \"Id\"='{offer2}';"));
        Assert.Equal(AppOfferAccepted, await AppStatusAsync(rows[1].ApplicationId));

        WriteDbChecks(s, new { requisition = req.Id, declined = new { offer = offer1, application = rows[0].ApplicationId }, accepted = new { offer = offer2, application = rows[1].ApplicationId, autoDrafted = nextDraft.Length > 0 } });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30f negatives: lock after 5 wrong codes; another person's reference + national id ─────────────

    [SkippableFact]
    public async Task Ac30_f_wrong_codes_lock_the_portal_and_another_persons_identity_gets_no_access()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("30f-negative", "AC-HCM-30f (negative) 5 wrong codes → locked; another person's reference + national id → no access");
        await RequireCandidateExperienceRequisitionAsync(s);
        WriteScenarioSteps(s, """
            1. Two citizens (L and P) apply on REQ-2026-A-CX01.
            2. AC-30f: L requests a code; five wrong codes → «رمز التحقق غير صحيح…» then locked (PortalLockedUntil, audit
               CandidatePortalLocked); the real code (FileDrop) is then refused with «تم إيقاف الدخول مؤقتاً…»; no careers_session.
            3. AC-30f: L's reference with P's national id → the same neutral message, no code is created for either, no session;
               /careers/my redirects to the sign-in page.
            """);

        var locked = NewCitizen("lock");
        var other = NewCitizen("other");
        foreach (var c in new[] { locked, other })
            Assert.Equal("/careers/submitted", (await s.Citizen($"30f-apply-{c.Tag}").ApplyAsync($"30f-apply-{c.Tag}", CxPosting, c, [])).FinalPath);
        var lockedRow = await RequireCitizenRowAsync(locked);
        var otherRow = await RequireCitizenRowAsync(other);

        var portal = s.Citizen("30f-lock");
        var requested = await portal.PostAsync("30f-lock-request", "careers/my/login", "careers/my/login",
            [new("ReferenceNumber", lockedRow.Reference), new("NationalId", locked.NationalId)]);
        Assert.Contains(NeutralOtpMessage, requested.Body);
        var code = await ReadOtpCodeAsync(s, "30f-lock", lockedRow);
        var codeNumber = int.Parse(code, CultureInfo.InvariantCulture);
        var html = requested.Body;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = ((codeNumber + attempt) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            var result = await portal.PostWithTokenAsync($"30f-wrong-code-{attempt}", "careers/my/verify", html,
                [new("ReferenceNumber", lockedRow.Reference), new("Code", wrong)]);
            html = result.Body;
            Assert.False(portal.HasCookie("careers_session"), "A wrong code opened a session.");
        }

        Assert.Contains(LockedMessage, html);
        Assert.Equal("true", await Sql(
            $"SELECT (\"PortalLockedUntil\" > now())::text FROM \"CitizenApplications\" WHERE \"Id\"='{lockedRow.CitizenId}';"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{lockedRow.CitizenId}' AND \"Action\"='CandidatePortalLocked';"));
        var withRealCode = await portal.PostWithTokenAsync("30f-locked-real-code", "careers/my/verify", html,
            [new("ReferenceNumber", lockedRow.Reference), new("Code", code)]);
        Assert.Contains(LockedMessage, withRealCode.Body);
        Assert.False(portal.HasCookie("careers_session"), "A locked portal opened a session.");

        // Another person's reference + national id.
        var challengesBefore = await Scalar(
            $"SELECT COUNT(*) FROM \"CandidateOtpChallenges\" WHERE \"CitizenApplicationId\" IN ('{lockedRow.CitizenId}','{otherRow.CitizenId}');");
        var intruder = s.Citizen("30f-intruder");
        var mixed = await intruder.PostAsync("30f-mixed-identity", "careers/my/login", "careers/my/login",
            [new("ReferenceNumber", otherRow.Reference), new("NationalId", locked.NationalId)]);
        Assert.Contains(NeutralOtpMessage, mixed.Body);
        Assert.Equal(challengesBefore, await Scalar(
            $"SELECT COUNT(*) FROM \"CandidateOtpChallenges\" WHERE \"CitizenApplicationId\" IN ('{lockedRow.CitizenId}','{otherRow.CitizenId}');"));
        Assert.False(intruder.HasCookie("careers_session"));
        var (status, location) = await intruder.GetNoRedirectAsync("30f-intruder-portal", "careers/my");
        Assert.Equal(HttpStatusCode.Redirect, status);
        Assert.EndsWith("/careers/my/login", location, StringComparison.Ordinal);

        WriteDbChecks(s, new { locked = lockedRow.CitizenId, other = otherRow.CitizenId, challengesBefore, portalRedirect = location });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30h negative: appeal after the window ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ac30_h_appeal_after_the_window_shows_no_button_and_the_post_is_refused()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("30h-negative", "AC-HCM-30h (negative) appeal after the window");
        await RequireCandidateExperienceRequisitionAsync(s);
        WriteScenarioSteps(s, """
            1. A citizen applies; central-a rejects with a note; test-only SQL moves RejectedAt 15 days back (as AC-28g).
            2. AC-30h: /careers/my shows «انتهت مدة الاستئناف» and no «تقديم الاستئناف» button; a POST to /careers/my/appeal is refused
               (the page again, no RecruitmentAppeals row).
            """);

        var citizen = NewCitizen("h-late");
        Assert.Equal("/careers/submitted", (await s.Citizen("30h-apply").ApplyAsync("30h-apply", CxPosting, citizen, [])).FinalPath);
        var row = await RequireCitizenRowAsync(citizen);
        await s.Central.PostAsync("30h-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(row.ApplicationId),
            [new("id", row.ApplicationId), new("status", "Rejected"), new("rejectionNote", "AC-HCM-30h")]);
        Assert.Equal(AppRejected, await AppStatusAsync(row.ApplicationId));
        await Sql($"UPDATE \"RecruitmentApplications\" SET \"RejectedAt\" = now() - interval '15 days' WHERE \"Id\"='{row.ApplicationId}';");

        var portal = s.Citizen("30h");
        var page = await SignInAsync(s, portal, "30h", citizen, row);
        Assert.Contains(NotAcceptedLabel, page);
        Assert.Contains(AppealClosedText, page);
        Assert.DoesNotContain(AppealButton, page);
        var refused = await portal.PostAsync("30h-appeal-late", "careers/my/appeal", "careers/my", [new("Text", AppealReason)]);
        Assert.Equal("/careers/my/appeal", refused.FinalPath);
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{row.ApplicationId}';"));

        WriteDbChecks(s, new { recruitmentApplication = row.ApplicationId, appeals = 0 });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30i: retention ──────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ac30_i_retention_anonymizes_the_due_pair_releases_files_and_keeps_the_held_pair()
    {
        AcceptanceEnv.RequireEnabled();
        var admin = AcceptanceHarness.RequirePersona("a-directory-checker");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(admin);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        recorder.WriteMetadata("AC-HCM-30i retention apply: due pair anonymized + files released; legal hold untouched", admin.Id);
        var s = new Session(recorder, "30i", null!, null!, null!, new Ui(await CreateClientAsync(admin, correlationId), recorder, admin.Id, "30i"));
        WriteScenarioSteps(s, """
            Fixture (AcceptanceCandidateExperiencePatch): REQ-2022-A-RET1 cancelled four years before the reference date; pair 1
            (recruitment application + linked citizen row AC30-RET-0001, one CV document) is due; pair 2 (AC30-RET-0002) has LegalHold.
            Acceptance runs retention with Apply=true and the default 36 months (a four-year-old cancellation is due; no Months override).
            1. a-directory-checker (hr-admin) opens «الاحتفاظ ببيانات المرشحين» and posts «تنفيذ الإخفاء» (CandidateLifecycle/apply).
               The job may already have anonymized pair 1 at startup: the end state is asserted either way.
            2. AC-30i: pair 1 anonymized (both rows: AnonymizedAt, name and national id cleared), its CandidateDocument deleted and a
               Release intent for its file in FileReferenceOutbox; a finished, non-dry-run CandidateRetentionRuns row for the tenant.
            3. AC-30i (negative): pair 2 untouched (names kept, AnonymizedAt null).
            """);

        var dueApp = AcceptanceSeedIds.CandidateRetentionDueApplicationId.ToString("D");
        var heldApp = AcceptanceSeedIds.CandidateRetentionHeldApplicationId.ToString("D");
        AcceptanceGate.RequireData(recorder,
            await Scalar($"SELECT COUNT(*) FROM \"RecruitmentApplications\" WHERE \"Id\" IN ('{dueApp}','{heldApp}');") == 2,
            "بيانات تهيئة الاحتفاظ الخاصة بـ AC-HCM-30i غير مُهيّأة — أعد بناء hr-core-web (AcceptanceCandidateExperiencePatch) أو أعد تهيئة البيئة",
            "The AC-HCM-30i retention fixtures are not seeded — rebuild hr-core-web (AcceptanceCandidateExperiencePatch) or reset the stack.");
        var before = await Sql(
            "SELECT string_agg(\"Id\" || ':' || COALESCE(\"AnonymizedAt\"::text,'-'), ',' ORDER BY \"Id\") FROM \"RecruitmentApplications\" " +
            $"WHERE \"Id\" IN ('{dueApp}','{heldApp}');");
        recorder.WriteBefore(new { pairs = before });
        var startedAt = await Sql("SELECT now()::text;");

        var page = await s.Admin!.GetAsync("30i-screen", "ui/hr-core/CandidateLifecycle");
        Assert.Contains("الاحتفاظ ببيانات المرشحين", page);
        await s.Admin.PostAsync("30i-apply", "ui/hr-core/CandidateLifecycle/apply", "ui/hr-core/CandidateLifecycle", []);
        var run = await Sql(
            "SELECT \"DueCount\"::text || '|' || \"AnonymizedCount\"::text || '|' || \"SkippedLegalHold\"::text || '|' || \"TriggeredBy\" FROM \"CandidateRetentionRuns\" " +
            $"WHERE \"TenantId\"='{AcceptanceSeedIds.AgencyAId}' AND NOT \"DryRun\" AND \"FinishedAt\" IS NOT NULL AND \"StartedAt\" >= '{startedAt}'::timestamptz ORDER BY \"StartedAt\" DESC LIMIT 1;");
        Assert.False(string.IsNullOrEmpty(run), "No finished apply run was logged for agency A.");
        Assert.True(int.Parse(run.Split('|')[2], CultureInfo.InvariantCulture) >= 1, $"The held pair was not counted as skipped: {run}");

        var due = await Sql(
            "SELECT (a.\"AnonymizedAt\" IS NOT NULL)::text || '|' || (c.\"AnonymizedAt\" IS NOT NULL)::text || '|' || " +
            $"(a.\"ApplicantFullName\" = '{Esc(RetentionDueName)}')::text || '|' || (c.\"FullName\" = '{Esc(RetentionDueName)}')::text || '|' || " +
            "(COALESCE(c.\"NationalId\",'') LIKE 'AC-1-%')::text " +
            $"FROM \"RecruitmentApplications\" a JOIN \"CitizenApplications\" c ON c.\"PromotedRecruitmentApplicationId\"=a.\"Id\" WHERE a.\"Id\"='{dueApp}';");
        Assert.Equal("true|true|false|false|false", due);
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"CandidateDocuments\" WHERE \"RecruitmentApplicationId\"='{dueApp}';"));
        Assert.True(await Scalar(
            $"SELECT COUNT(*) FROM \"FileReferenceOutbox\" WHERE \"FileId\"='{AcceptanceSeedIds.CandidateRetentionDueFileId}' AND \"Operation\"='Release';") >= 1,
            "No release intent for the due pair's document.");

        var held = await Sql(
            "SELECT (a.\"AnonymizedAt\" IS NULL)::text || '|' || (c.\"AnonymizedAt\" IS NULL)::text || '|' || " +
            $"(a.\"ApplicantFullName\" = '{Esc(RetentionHeldName)}')::text || '|' || (c.\"FullName\" = '{Esc(RetentionHeldName)}')::text || '|' || a.\"LegalHold\"::text " +
            $"FROM \"RecruitmentApplications\" a JOIN \"CitizenApplications\" c ON c.\"PromotedRecruitmentApplicationId\"=a.\"Id\" WHERE a.\"Id\"='{heldApp}';");
        Assert.Equal("true|true|true|true|true", held);

        WriteDbChecks(s, new { before, run, due, held });
        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    // ── AC-HCM-30j: backfill ──────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ac30_j_backfill_links_unlinked_citizen_and_internal_rows_once()
    {
        AcceptanceEnv.RequireEnabled();
        var central = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        recorder.WriteMetadata("AC-HCM-30j unified-intake backfill: dry run, apply, second apply", "operator (scripts/hcm030-backfill-unified-intake.ps1)");
        var s = new Session(recorder, "30j", null!, null!, null!, null);
        WriteScenarioSteps(s, """
            Fixture: two unlinked citizen rows on REQ-2026-A-CX01 (AC30-BF-0001 Received, AC30-BF-0002 UnderReview; recent dates) and the
            seed pack's unlinked internal application (UnderReview) on the BR1 requisition.
            1. scripts/hcm030-backfill-unified-intake.ps1 -DryRun -DockerContainer <acceptance postgresql> → reports, writes nothing.
            2. AC-30j: -Apply → every unlinked requisition-linked row gets a RecruitmentApplication (Public/Internal) with the mapped
               status (Received → Submitted, UnderReview → UnderReview; Rejected for a closed/cancelled requisition) and the link.
            3. AC-30j (negative): a second -Apply links nothing (Total linked: 0) and inserts no RecruitmentApplications.
            """);

        var received = AcceptanceSeedIds.CandidateBackfillCitizenReceivedId.ToString("D");
        var underReview = AcceptanceSeedIds.CandidateBackfillCitizenUnderReviewId.ToString("D");
        var internalId = AcceptanceSeedIds.Br1InternalApplicationId.ToString("D");
        AcceptanceGate.RequireData(recorder,
            await Scalar($"SELECT COUNT(*) FROM \"CitizenApplications\" WHERE \"Id\" IN ('{received}','{underReview}');") == 2,
            "بيانات تهيئة الترحيل الموحّد الخاصة بـ AC-HCM-30j غير مُهيّأة — أعد بناء hr-core-web (AcceptanceCandidateExperiencePatch) أو أعد تهيئة البيئة",
            "The AC-HCM-30j backfill fixtures are not seeded — rebuild hr-core-web (AcceptanceCandidateExperiencePatch) or reset the stack.");
        // The patch never re-arms the fixtures: after one run they stay linked and a rerun would prove nothing.
        var fixturesUnlinked =
            await Scalar($"SELECT COUNT(*) FROM \"CitizenApplications\" WHERE \"Id\" IN ('{received}','{underReview}') AND \"PromotedRecruitmentApplicationId\" IS NULL;") +
            await Scalar($"SELECT COUNT(*) FROM \"InternalApplications\" WHERE \"Id\"='{internalId}' AND \"PromotedRecruitmentApplicationId\" IS NULL;");
        AcceptanceGate.RequireData(recorder, fixturesUnlinked == 3,
            "بيانات تهيئة AC-HCM-30j رُبطت في تشغيل سابق — شغّل scripts/acceptance-reset.ps1 لإعادة تهيئتها",
            "The AC-HCM-30j backfill fixtures were already linked by an earlier run — run scripts/acceptance-reset.ps1 to re-arm them.");

        const string unlinkedSql =
            "SELECT (SELECT COUNT(*) FROM \"CitizenApplications\" WHERE \"RequisitionId\" IS NOT NULL AND \"PromotedRecruitmentApplicationId\" IS NULL) + " +
            "(SELECT COUNT(*) FROM \"InternalApplications\" WHERE \"RequisitionId\" IS NOT NULL AND \"PromotedRecruitmentApplicationId\" IS NULL);";
        var unlinkedBefore = await Scalar(unlinkedSql);
        var applicationsBefore = await Scalar("SELECT COUNT(*) FROM \"RecruitmentApplications\";");
        recorder.WriteBefore(new { unlinkedBefore, applicationsBefore });

        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("postgresql");
        var dryRun = await RunBackfillAsync(s, "30j-dry-run", container, apply: false);
        Assert.True(dryRun >= 3, $"30j: the dry run reported {dryRun} rows; the two citizen fixtures and the internal row must be among them.");
        Assert.Equal(unlinkedBefore, dryRun);
        Assert.Equal(unlinkedBefore, await Scalar(unlinkedSql));
        Assert.Equal(applicationsBefore, await Scalar("SELECT COUNT(*) FROM \"RecruitmentApplications\";"));

        var applied = await RunBackfillAsync(s, "30j-apply", container, apply: true);
        Assert.Equal(unlinkedBefore, applied);
        Assert.Equal(0, await Scalar(unlinkedSql));
        Assert.Equal(applicationsBefore + applied, await Scalar("SELECT COUNT(*) FROM \"RecruitmentApplications\";"));

        var citizenRows = await Sql(
            "SELECT string_agg(c.\"ReferenceNumber\" || ':' || a.\"Status\"::text || ':' || a.\"IntakeChannel\" || ':' || (a.\"RequisitionId\"=c.\"RequisitionId\")::text, ',' ORDER BY c.\"ReferenceNumber\") " +
            $"FROM \"CitizenApplications\" c JOIN \"RecruitmentApplications\" a ON a.\"Id\"=c.\"PromotedRecruitmentApplicationId\" WHERE c.\"Id\" IN ('{received}','{underReview}');");
        Assert.Equal($"AC30-BF-0001:{AppSubmitted}:Public:true,AC30-BF-0002:{AppUnderReview}:Public:true", citizenRows);
        var internalRow = await Sql(
            "SELECT a.\"Status\"::text || ':' || a.\"IntakeChannel\" || ':' || r.\"Status\" FROM \"InternalApplications\" i " +
            "JOIN \"RecruitmentApplications\" a ON a.\"Id\"=i.\"PromotedRecruitmentApplicationId\" JOIN \"RecruitmentRequisitions\" r ON r.\"Id\"=i.\"RequisitionId\" " +
            $"WHERE i.\"Id\"='{internalId}';");
        var internalParts = internalRow.Split(':');
        Assert.Equal("Internal", internalParts[1]);
        Assert.Equal(internalParts[2] is "Closed" or "Cancelled" ? AppRejected : AppUnderReview, int.Parse(internalParts[0], CultureInfo.InvariantCulture));

        var applicationsAfter = await Scalar("SELECT COUNT(*) FROM \"RecruitmentApplications\";");
        var second = await RunBackfillAsync(s, "30j-apply-again", container, apply: true);
        Assert.Equal(0, second);
        Assert.Equal(applicationsAfter, await Scalar("SELECT COUNT(*) FROM \"RecruitmentApplications\";"));

        WriteDbChecks(s, new { unlinkedBefore, dryRun, applied, second, citizenRows, internalRow });
        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    // ── Session, personas, journeys ─────────────────────────────────────────────────────────────────────────

    private sealed record Session(EvidenceRecorder Recorder, string Tag, Ui Central, Ui Br1, Ui Approver, Ui? Admin)
    {
        /// <summary>A fresh anonymous visitor (own cookie jar, no Authorization header).</summary>
        public CitizenUi Citizen(string tag) => new(CreateAnonymousClient(), Recorder, tag);
    }

    private sealed record Citizen(string Tag, string FullName, string NationalId, string Email, string? EducationLevel, string? YearsOfExperience);

    private sealed record CitizenRow(string CitizenId, string Reference, string ApplicationId);

    private sealed record PublishedRequisition(string Id, string PostingId, string Grade);

    private sealed record GradeBounds(decimal Min, decimal Max)
    {
        public decimal Mid => Math.Round((Min + Max) / 2m, 0);
    }

    private sealed record FileDropMessage(string Template, string Channel, string? Subject, string Body);

    private static async Task<Session> StartAsync(string tag, string scenario)
    {
        var central = AcceptanceHarness.RequirePersona("central-a");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var approver = AcceptanceHarness.RequirePersona("a-management-approver");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        recorder.WriteMetadata(scenario, $"{central.Id}+{br1.Id}+{approver.Id}+citizen (anonymous)");
        return new Session(
            recorder,
            tag,
            new Ui(await CreateClientAsync(central, correlationId), recorder, central.Id, tag),
            new Ui(await CreateClientAsync(br1, correlationId), recorder, br1.Id, tag),
            new Ui(await CreateClientAsync(approver, correlationId), recorder, approver.Id, tag),
            null);
    }

    private static async Task<HttpClient> CreateClientAsync(HRManagement.E2E.E2ePersona persona, string correlationId)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona, correlationId);
        var http = CreateAnonymousClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        return http;
    }

    /// <summary>Manual cookies (UseCookies = false): a CookieContainer drops Secure cookies over http://localhost.</summary>
    private static HttpClient CreateAnonymousClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(HRManagement.E2E.E2eEnv.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2)
        };

    private static Citizen NewCitizen(string tag, string? educationLevel = "BACH", string? yearsOfExperience = "4")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new Citizen(
            tag,
            $"مواطن الخطة30 {tag} {suffix}",
            "30" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture),
            $"ac30-{tag}-{suffix}@test.sy",
            educationLevel,
            yearsOfExperience);
    }

    private static async Task RequireCandidateExperienceRequisitionAsync(Session s) =>
        AcceptanceGate.RequireData(s.Recorder,
            await Scalar($"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{CxRequisition}' AND \"Status\"='Published';") == 1,
            "طلب الشاغر REQ-2026-A-CX01 الخاص بالخطة 30 غير مُهيّأ أو غير منشور — أعد بناء hr-core-web (AcceptanceCandidateExperiencePatch) أو أعد تهيئة البيئة",
            "The PLAN-30 requisition REQ-2026-A-CX01 is not seeded or not published — rebuild hr-core-web (AcceptanceCandidateExperiencePatch) or reset the stack.");

    /// <summary>The citizen row (by national-id hash) and its linked recruitment application; the reference is TempData-only in the UI.</summary>
    private static async Task<CitizenRow> RequireCitizenRowAsync(Citizen citizen)
    {
        var row = await Sql(
            "SELECT \"Id\" || '|' || \"ReferenceNumber\" || '|' || COALESCE(\"PromotedRecruitmentApplicationId\"::text,'') FROM \"CitizenApplications\" " +
            $"WHERE \"NationalIdHash\"='{PiiMask.Sha256Hex(citizen.NationalId)}' ORDER BY \"SubmittedAt\" DESC LIMIT 1;");
        var parts = row.Split('|');
        Assert.True(parts.Length == 3 && Guid.TryParse(parts[2], out _), $"Citizen {citizen.Tag}: no linked application ({row.Length} chars).");
        return new CitizenRow(parts[0], parts[1], parts[2]);
    }

    private static Task<long> CitizenRowCountAsync(Citizen citizen) =>
        Scalar($"SELECT COUNT(*) FROM \"CitizenApplications\" WHERE \"NationalIdHash\"='{PiiMask.Sha256Hex(citizen.NationalId)}';");

    /// <summary>Requests a code, reads it from the FileDrop file of the latest challenge and verifies it → «طلبي».</summary>
    private static async Task<string> SignInAsync(Session s, CitizenUi portal, string step, Citizen citizen, CitizenRow row)
    {
        var requested = await portal.PostAsync($"{step}-otp-request", "careers/my/login", "careers/my/login",
            [new("ReferenceNumber", row.Reference), new("NationalId", citizen.NationalId)]);
        Assert.Contains(NeutralOtpMessage, requested.Body);
        var code = await ReadOtpCodeAsync(s, step, row);
        var verified = await portal.PostWithTokenAsync($"{step}-otp-verify", "careers/my/verify", requested.Body,
            [new("ReferenceNumber", row.Reference), new("Code", code)]);
        Assert.Equal("/careers/my", verified.FinalPath);
        Assert.True(portal.HasCookie("careers_session"), $"{step}: no careers_session after a valid code.");
        return verified.Body;
    }

    /// <summary>The code of the citizen's latest challenge (FileDrop key otp:{challengeId}:Email). Never written to evidence.</summary>
    private static async Task<string> ReadOtpCodeAsync(Session s, string step, CitizenRow row)
    {
        var challenge = await WaitForTextAsync(s, $"{step}-otp-challenge", TimeSpan.FromSeconds(30), () => Sql(
            $"SELECT \"Id\"::text FROM \"CandidateOtpChallenges\" WHERE \"CitizenApplicationId\"='{row.CitizenId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;"));
        var message = await WaitForFileDropAsync(s, $"{step}-otp-filedrop", $"otp:{challenge}:Email", TimeSpan.FromSeconds(60));
        Assert.Equal("OTP", message.Template);
        var match = Regex.Match(message.Body, @"(?<!\d)(\d{6})(?!\d)");
        Assert.True(match.Success, $"{step}: no six-digit code in the OTP message.");
        return match.Groups[1].Value;
    }

    private static string FileDropFolder => Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "acceptance", "candidate-notifications");

    private static string FileDropPath(string idempotencyKey) => Path.Combine(FileDropFolder, PiiMask.Sha256Hex(idempotencyKey) + ".json");

    private static async Task<FileDropMessage> WaitForFileDropAsync(Session s, string timing, string idempotencyKey, TimeSpan timeout)
    {
        var path = FileDropPath(idempotencyKey);
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8));
                    var root = doc.RootElement;
                    Assert.Equal(idempotencyKey, root.GetProperty("idempotencyKey").GetString());
                    s.Recorder.RecordTiming(timing, (DateTime.UtcNow - started).TotalSeconds);
                    return new FileDropMessage(
                        root.GetProperty("template").GetString()!,
                        root.GetProperty("channel").GetString()!,
                        root.GetProperty("subject").ValueKind == JsonValueKind.Null ? null : root.GetProperty("subject").GetString(),
                        root.GetProperty("body").GetString()!);
                }
                catch (IOException)
                {
                    // Being written (temp file + move): read again.
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        Assert.Fail($"{timing}: no FileDrop message within {timeout.TotalSeconds:0}s (folder {FileDropFolder}).");
        return null!;
    }

    private static async Task<string> WaitForScanAsync(Session s, string timing, IEnumerable<string> fileIds, string[] expected)
    {
        var ids = string.Join(",", fileIds.Select(id => $"'{id}'"));
        var count = ids.Split(',').Length;
        var accepted = string.Join(",", expected.Select(e => $"'{e}'"));
        return await WaitForTextAsync(s, timing, TimeSpan.FromMinutes(3), async () =>
        {
            var statuses = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("file_storage_db",
                $"SELECT string_agg(\"ScanStatus\", ',' ORDER BY \"Id\") FROM \"Files\" WHERE \"Id\" IN ({ids}) AND \"ScanStatus\" IN ({accepted}) HAVING COUNT(*) = {count};");
            return statuses;
        });
    }

    private static async Task<string> WaitForPageAsync(Session s, Ui ui, string step, string path, Func<string, bool> ready)
    {
        var started = DateTime.UtcNow;
        string html;
        var attempt = 0;
        do
        {
            html = await ui.GetAsync($"{step}-{++attempt}", path);
            if (ready(html))
            {
                s.Recorder.RecordTiming(step, (DateTime.UtcNow - started).TotalSeconds);
                return html;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        while (DateTime.UtcNow - started < TimeSpan.FromMinutes(2));

        Assert.Fail($"{step}: the page did not reach the expected state within 2 minutes.");
        return html;
    }

    private static async Task<string> WaitForTextAsync(Session s, string timing, TimeSpan timeout, Func<Task<string>> query)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            var value = (await query()).Trim();
            if (value.Length > 0)
            {
                s.Recorder.RecordTiming(timing, (DateTime.UtcNow - started).TotalSeconds);
                return value;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        Assert.Fail($"{timing}: nothing within {timeout.TotalSeconds:0}s.");
        return string.Empty;
    }

    /// <summary>Runs the backfill script with -DockerContainer (no host psql); returns «Total linked».</summary>
    private static async Task<long> RunBackfillAsync(Session s, string step, string container, bool apply)
    {
        var script = Path.Combine(AcceptanceHarness.RepoRoot, "scripts", "hcm030-backfill-unified-intake.ps1");
        var shell = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var (exitCode, output) = await AcceptanceMessagingHelper.RunProcessAsync(
            shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, apply ? "-Apply" : "-DryRun", "-DockerContainer", container);
        s.Recorder.CaptureHttp($"{s.Tag}-{step}", "SCRIPT", $"scripts/hcm030-backfill-unified-intake.ps1 {(apply ? "-Apply" : "-DryRun")} -DockerContainer {container}",
            exitCode, output.Length > 2000 ? output[..2000] : output);
        Assert.True(exitCode == 0, $"{step}: backfill script exited {exitCode}: {output}");
        var match = Regex.Match(output, @"Total linked:\s*(\d+)");
        Assert.True(match.Success, $"{step}: no 'Total linked' line: {output}");
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<PublishedRequisition> CreatePublishedRequisitionAsync(Session s, string step)
    {
        var positionId = await FindFreeGradedBr1PositionAsync(s.Central);
        AcceptanceGate.RequireData(s.Recorder, positionId is not null,
            "لم يتبقَّ في الجهة (أ) منصب ممول شاغر مُدرَّج في الفرع الأول دون طلب شاغر يحجبه",
            "No graded vacant BR1 funded position without a blocking requisition is left in agency A.");
        var grade = await Sql($"SELECT COALESCE(\"GradeCode\",'') FROM \"FundedPositions\" WHERE \"Id\"='{positionId}';");

        await s.Central.PostAsync($"{step}-create-requisition", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", Br1Scope),
            new("FundedPositionId", positionId!),
            new("JobTitle", "وظيفة AC-HCM-30"),
            new("JustificationNotes", "PLAN-30 acceptance"),
            new("LegalSourceId", Syr001LegislationIds.Law50_2004),
            new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)),
            new("PublishingChannel", "PublicOnly")
        ]);
        var reqId = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(reqId, out _), $"Requisition was not created on position {positionId}.");
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";
        await s.Central.PostAsync($"{step}-submit-requisition", $"ui/hr-core/RecruitmentRequisitions/Submit/{reqId}", details, []);
        await s.Br1.PostAsync($"{step}-approve-requisition-1", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-30 step 1")]);
        await s.Approver.PostAsync($"{step}-approve-requisition-2", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-30 step 2")]);
        await s.Central.PostAsync($"{step}-criteria", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details, CriteriaForm(reqId));
        await s.Central.PostAsync($"{step}-publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
        var published = await Sql($"SELECT \"Status\" || '|' || COALESCE(\"PublicJobPostingId\"::text,'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';");
        var parts = published.Split('|');
        Assert.Equal("Published", parts[0]);
        Assert.True(Guid.TryParse(parts[1], out _), "The published requisition has no public posting.");
        s.Recorder.WriteBefore(new { scenario = s.Tag, requisitionId = reqId, positionId, grade, postingId = parts[1] });
        return new PublishedRequisition(reqId, parts[1], grade);
    }

    private static async Task<GradeBounds> RequireGradeBoundsAsync(Session s, PublishedRequisition req, string appId)
    {
        var page = await s.Central.GetAsync("payroll-grade-hint", AppDetails(appId));
        AcceptanceGate.RequireData(s.Recorder, !page.Contains("تعذّر التحقق من حدود الدرجة", StringComparison.Ordinal),
            "تعذّر على HR.Core الوصول إلى خدمة الرواتب للاستعلام عن حدود الدرجة",
            "HR.Core cannot reach the Payroll grade lookup (see PLAN-27-EXECUTION).");
        var row = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            "SELECT g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g JOIN \"SalaryScales\" sc ON sc.\"Id\"=g.\"SalaryScaleId\" " +
            $"WHERE g.\"TenantId\"='{AcceptanceSeedIds.AgencyAId}' AND g.\"GradeCode\"='{req.Grade}' AND sc.\"IsActive\" AND sc.\"EffectiveFrom\" <= now() " +
            "AND (sc.\"EffectiveTo\" IS NULL OR sc.\"EffectiveTo\" >= now()) ORDER BY sc.\"EffectiveFrom\" DESC LIMIT 1;");
        var parts = row.Split('|');
        Assert.True(parts.Length == 2, $"Payroll grade {req.Grade} not found for agency A.");
        return new GradeBounds(decimal.Parse(parts[0], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static async Task<string> CreateOfferAsync(Session s, string step, string appId, decimal salary, string grade)
    {
        var page = await s.Central.PostAsync(step, "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(appId),
        [
            new("ApplicationId", appId),
            new("OfferedSalary", salary.ToString(CultureInfo.InvariantCulture)),
            new("GradeCode", grade),
            new("ProposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("ValidityDays", "14")
        ]);
        AcceptanceGate.RequireData(s.Recorder, !page.Contains("تعذّر التحقق من حدود الدرجة", StringComparison.Ordinal),
            "تعذّر على HR.Core الوصول إلى خدمة الرواتب للاستعلام عن حدود الدرجة",
            "HR.Core cannot reach the Payroll grade lookup (see PLAN-27-EXECUTION).");
        var offerId = await Sql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{appId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offerId, out _), $"{step}: offer was not created for application {appId}.");
        return offerId;
    }

    private static async Task SubmitAndApproveAsync(Session s, string step, string offerId)
    {
        await s.Central.PostAsync($"{step}-submit-offer", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offerId), [new("id", offerId)]);
        await s.Br1.PostAsync($"{step}-approve-offer-1", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", "AC-HCM-30 step 1")]);
        await s.Approver.PostAsync($"{step}-approve-offer-2", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", "AC-HCM-30 step 2")]);
    }

    private static async Task<string?> FindFreeGradedBr1PositionAsync(Ui central)
    {
        var json = await central.GetAsync("vacant-positions", $"ui/hr-core/api/recruitment/vacant-positions?orgUnitId={Br1Scope}");
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var blocking = await Scalar(
                $"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{id}' AND \"Status\" IN ({string.Join(",", BlockingStatuses.Select(b => $"'{b}'"))});");
            if (blocking != 0)
                continue;
            var grade = await Sql($"SELECT COALESCE(\"GradeCode\",'') FROM \"FundedPositions\" WHERE \"Id\"='{id}';");
            if (Regex.IsMatch(grade, "^G([1-9]|10)$"))
                return id;
        }

        return null;
    }

    private static List<KeyValuePair<string, string>> CriteriaForm(string reqId)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", reqId), new("Title", "AC-HCM-30") };
        (string Key, string Name, string Weight, string Stage)[] items =
            [("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")];
        for (var i = 0; i < items.Length; i++)
        {
            var t = $"r{i}";
            form.Add(new("Items.Index", t));
            form.Add(new($"Items[{t}].Key", items[i].Key));
            form.Add(new($"Items[{t}].NameAr", items[i].Name));
            form.Add(new($"Items[{t}].Weight", items[i].Weight));
            form.Add(new($"Items[{t}].MaxScore", "100"));
            form.Add(new($"Items[{t}].Stage", items[i].Stage));
            form.Add(new($"Items[{t}].IsMandatory", "true"));
        }

        return form;
    }

    private static List<KeyValuePair<string, string>> ScheduleForm(string appId, DateTime utc) =>
    [
        new("ApplicationId", appId),
        // datetime-local is interpreted in the server's local time; the acceptance containers run in UTC.
        new("ScheduledAt", utc.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)),
        new("Mode", "InPerson"),
        new("Location", "قاعة المقابلات"),
        new("PanelEmployeeIds", AcceptanceSeedIds.EmpBr1Id.ToString("D")),
        new("PanelNotes", "AC-HCM-30")
    ];

    private static string AppDetails(string appId) => $"ui/hr-core/RecruitmentApplications/Details/{appId}";

    private static string OfferDetails(string offerId) => $"ui/hr-core/OfferLetters/Details/{offerId}";

    private static string Esc(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Scenarios share the AC-HCM-30 folder: steps-*.md and db-checks-*.json are kept per scenario.</summary>
    private static void WriteScenarioSteps(Session s, string markdown)
    {
        File.WriteAllText(Path.Combine(s.Recorder.Folder, $"steps-{s.Tag}.md"), markdown);
        var combined = Directory.GetFiles(s.Recorder.Folder, "steps-*.md")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"## {Path.GetFileNameWithoutExtension(f)[6..]}\n\n{File.ReadAllText(f)}");
        s.Recorder.WriteSteps(string.Join("\n\n", combined));
    }

    private static void WriteDbChecks(Session s, object checks)
    {
        File.WriteAllText(Path.Combine(s.Recorder.Folder, $"db-checks-{s.Tag}.json"),
            JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        s.Recorder.WriteDbChecks(checks);
    }

    // ── DB helpers ────────────────────────────────────────────────────────────

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);
    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);

    private static async Task<int> AppStatusAsync(string appId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';");

    private static async Task<int> OfferStatusAsync(string offerId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{offerId}';");

    // ── HTTP clients ──────────────────────────────────────────────────────────

    private sealed record PostResult(HttpStatusCode Status, string? FinalPath, string Body);

    /// <summary>
    /// Anonymous visitor of <c>/careers</c> through the gateway: manual cookie jar (antiforgery + <c>careers_session</c>), no
    /// Authorization header. National ids and codes are masked in the evidence. A 429 from the shared public-careers
    /// window (30/min for all visitors) is retried after a pause.
    /// </summary>
    private sealed class CitizenUi(HttpClient http, EvidenceRecorder recorder, string tag)
    {
        private static readonly string[] Masked = ["NationalId", "nationalId", "Code"];
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        public bool HasCookie(string name) => _cookies.ContainsKey(name);

        public async Task<PostResult> ApplyAsync(
            string step, string postingId, Citizen citizen, IReadOnlyList<(string Field, string FileName, string ContentType, byte[] Bytes)> files)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new("PublicJobPostingId", postingId),
                new("FullName", citizen.FullName),
                new("NationalId", citizen.NationalId),
                new("Email", citizen.Email),
                new("PreferredChannel", "Email"),
                new("NotificationConsent", "true"),
                new("BirthDate", DateTime.UtcNow.Date.AddYears(-28).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new("Specialization", "إدارة أعمال"),
                new("GraduationYear", (DateTime.UtcNow.Year - 6).ToString(CultureInfo.InvariantCulture)),
                new("CoverNote", "تقديم آلي لاختبار القبول AC-HCM-30"),
                new("PrivacyConsent", "true"),
                new("Website", string.Empty)
            };
            if (citizen.EducationLevel is not null)
                fields.Add(new("EducationLevelCode", citizen.EducationLevel));
            if (citizen.YearsOfExperience is not null)
                fields.Add(new("YearsOfExperience", citizen.YearsOfExperience));

            return await PostAsync(step, "careers/apply", $"careers/{postingId}", fields, files);
        }

        public async Task<PostResult> PostAsync(
            string step,
            string path,
            string tokenPage,
            List<KeyValuePair<string, string>> fields,
            IReadOnlyList<(string Field, string FileName, string ContentType, byte[] Bytes)>? files = null)
        {
            for (var attempt = 1; ; attempt++)
            {
                var page = await FollowRedirectsAsync(await SendAsync(HttpMethod.Get, tokenPage));
                var html = await page.Content.ReadAsStringAsync();
                Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {tokenPage} returned {(int)page.StatusCode}.");
                var result = await PostWithTokenCoreAsync(step, path, html, fields, files);
                if (result.Status != HttpStatusCode.TooManyRequests || attempt == 4)
                    return Checked(path, result);
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
        }

        /// <summary>Posts with the antiforgery token of an already loaded page (e.g. the code form returned by a POST).</summary>
        public async Task<PostResult> PostWithTokenAsync(string step, string path, string html, List<KeyValuePair<string, string>> fields)
        {
            for (var attempt = 1; ; attempt++)
            {
                var result = await PostWithTokenCoreAsync(step, path, html, fields, null);
                if (result.Status != HttpStatusCode.TooManyRequests || attempt == 4)
                    return Checked(path, result);
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
        }

        public async Task<(HttpStatusCode Status, string? ContentType, string Head)> GetRawAsync(string step, string path)
        {
            using var resp = await SendAsync(HttpMethod.Get, path);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var head = bytes.Length >= 4 ? Encoding.ASCII.GetString(bytes, 0, 4) : string.Empty;
            recorder.CaptureHttp($"{tag}-{step}-citizen", "GET", "/" + path, (int)resp.StatusCode,
                new { contentType = resp.Content.Headers.ContentType?.MediaType, length = bytes.Length, head });
            return (resp.StatusCode, resp.Content.Headers.ContentType?.MediaType, head);
        }

        public async Task<(HttpStatusCode Status, string Location)> GetNoRedirectAsync(string step, string path)
        {
            using var resp = await SendAsync(HttpMethod.Get, path);
            var location = resp.Headers.Location?.OriginalString ?? string.Empty;
            recorder.CaptureHttp($"{tag}-{step}-citizen", "GET", "/" + path, (int)resp.StatusCode, new { location });
            return (resp.StatusCode, location);
        }

        private static PostResult Checked(string path, PostResult result)
        {
            Assert.True((int)result.Status < 500, $"POST {path} returned {(int)result.Status}.");
            return result;
        }

        private async Task<PostResult> PostWithTokenCoreAsync(
            string step,
            string path,
            string html,
            List<KeyValuePair<string, string>> fields,
            IReadOnlyList<(string Field, string FileName, string ContentType, byte[] Bytes)>? files)
        {
            var token = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""", RegexOptions.IgnoreCase);
            Assert.True(token.Success, $"No antiforgery token before POST {path}.");
            var form = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)) };
            HttpContent content;
            if (files is null)
            {
                content = new FormUrlEncodedContent(form);
            }
            else
            {
                var multipart = new MultipartFormDataContent();
                foreach (var field in form)
                    multipart.Add(new StringContent(field.Value), field.Key);
                foreach (var file in files)
                {
                    var part = new ByteArrayContent(file.Bytes);
                    part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                    multipart.Add(part, file.Field, file.FileName);
                }
                content = multipart;
            }

            var posted = await SendAsync(HttpMethod.Post, path, content);
            var postStatus = (int)posted.StatusCode;
            var redirect = posted.Headers.Location?.ToString();
            var resp = await FollowRedirectsAsync(posted);
            var body = await resp.Content.ReadAsStringAsync();
            var finalPath = resp.RequestMessage?.RequestUri?.AbsolutePath;
            recorder.CaptureHttp($"{tag}-{step}-citizen", "POST", "/" + path, (int)resp.StatusCode, new
            {
                fields = fields.Select(f => Masked.Contains(f.Key) ? $"{f.Key}=***" : f.Key is "Email" or "FullName" ? $"{f.Key}=(set)" : $"{f.Key}={f.Value}"),
                files = files?.Select(f => new { f.Field, f.FileName, f.ContentType, length = f.Bytes.Length }),
                postStatus,
                redirect,
                final = finalPath
            });
            return new PostResult(resp.StatusCode, finalPath, WebUtility.HtmlDecode(body));
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            if (_cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            var response = await http.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var header in setCookies)
                {
                    var pair = header.Split(';', 2)[0];
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var name = pair[..eq].Trim();
                    var value = pair[(eq + 1)..].Trim();
                    var expired = header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(value) || expired) _cookies.Remove(name);
                    else _cookies[name] = value;
                }
            }

            return response;
        }

        private async Task<HttpResponseMessage> FollowRedirectsAsync(HttpResponseMessage resp)
        {
            for (var hops = 0; hops < 3 && (int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null; hops++)
            {
                var location = resp.Headers.Location;
                resp.Dispose();
                resp = await SendAsync(HttpMethod.Get,
                    location.IsAbsoluteUri ? location.PathAndQuery.TrimStart('/') : location.OriginalString.TrimStart('/'));
            }

            return resp;
        }
    }

    /// <summary>Gateway UI client for one HR persona: antiforgery-aware form posts with evidence capture (as AC-HCM-26..29).</summary>
    private sealed class Ui(HttpClient http, EvidenceRecorder recorder, string persona, string tag)
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            if (_cookies.Count > 0)
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            var response = await http.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var header in setCookies)
                {
                    var pair = header.Split(';', 2)[0];
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var name = pair[..eq].Trim();
                    var value = pair[(eq + 1)..].Trim();
                    var expired = header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(value) || expired) _cookies.Remove(name);
                    else _cookies[name] = value;
                }
            }

            return response;
        }

        private async Task<HttpResponseMessage> FollowRedirectsAsync(HttpResponseMessage resp)
        {
            for (var hops = 0; hops < 3 && (int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null; hops++)
            {
                var location = resp.Headers.Location;
                resp.Dispose();
                resp = await SendAsync(HttpMethod.Get,
                    location.IsAbsoluteUri ? location.PathAndQuery.TrimStart('/') : location.OriginalString.TrimStart('/'));
            }

            return resp;
        }

        public async Task<string> GetAsync(string step, string path)
        {
            var resp = await FollowRedirectsAsync(await SendAsync(HttpMethod.Get, path));
            var body = await resp.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode, body.Length > 600 ? body[..600] : body);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} as {persona} returned {(int)resp.StatusCode}.");
            return WebUtility.HtmlDecode(body);
        }

        /// <summary>Raw GET (no redirects): status, content type and the first four bytes.</summary>
        public async Task<(HttpStatusCode Status, string? ContentType, string Head)> GetRawAsync(string step, string path)
        {
            using var resp = await SendAsync(HttpMethod.Get, path);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var head = bytes.Length >= 4 ? Encoding.ASCII.GetString(bytes, 0, 4) : string.Empty;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode,
                new { contentType = resp.Content.Headers.ContentType?.MediaType, length = bytes.Length, head });
            return (resp.StatusCode, resp.Content.Headers.ContentType?.MediaType, head);
        }

        public async Task<string> PostAsync(string step, string path, string tokenPage, List<KeyValuePair<string, string>> fields)
        {
            var page = await FollowRedirectsAsync(await SendAsync(HttpMethod.Get, tokenPage));
            var pageHtml = await page.Content.ReadAsStringAsync();
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {tokenPage} as {persona} returned {(int)page.StatusCode}.");
            var token = Regex.Match(pageHtml, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""", RegexOptions.IgnoreCase);
            Assert.True(token.Success, $"No antiforgery token on {tokenPage}.");

            var form = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)) };
            var posted = await SendAsync(HttpMethod.Post, path, new FormUrlEncodedContent(form));
            var postStatus = (int)posted.StatusCode;
            var redirect = posted.Headers.Location?.ToString();
            var resp = await FollowRedirectsAsync(posted);
            var body = await resp.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "POST", "/" + path, (int)resp.StatusCode,
                new { fields = fields.Select(f => f.Key.Contains("NationalId", StringComparison.Ordinal) ? $"{f.Key}=***" : $"{f.Key}={f.Value}"), postStatus, redirect, final = resp.RequestMessage?.RequestUri?.AbsolutePath });
            Assert.True((int)resp.StatusCode < 500, $"POST {path} as {persona} returned {(int)resp.StatusCode}.");
            Assert.True(resp.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"POST {path} as {persona} was denied.");
            return WebUtility.HtmlDecode(body);
        }
    }
}
