using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-31a..g (PLAN-31): multi-vacancy competitions through the gateway with the agency A personas — central-a (HR),
/// br1-mgr (first approval step, hr-core), a-management-approver (second step), a-compliance (independent objection
/// review) and the three exam markers (a-exam-marker, -2, -3; role hr-exam-marker, exam numbers only).
/// Fixtures come from <c>AcceptanceCompetitionPatch</c>: three approved requisitions for 31a, and competition
/// <see cref="AcceptanceSeedIds.CompetitionNumber"/> at IntakeClosed with 120 admitted applicants for 31b–g (the journey
/// mutates it; <c>scripts/acceptance-reset.ps1</c> re-arms it). Ids the UI shows only through TempData are read by SQL.
/// Test-only SQL preconditions are documented at each use. No national id or score reaches a candidate message.
/// </summary>
[RequiredAcceptance]
public class AcHcm31CompetitionTests
{
    private const string Ac = "AC-HCM-31";
    private static readonly string CompetitionId = AcceptanceSeedIds.CompetitionId.ToString("D");
    private static readonly string[] SessionIds =
        [AcceptanceSeedIds.CompetitionSession1Id.ToString("D"), AcceptanceSeedIds.CompetitionSession2Id.ToString("D")];
    private static readonly string[] MarkerPersonas = ["a-exam-marker", "a-exam-marker-2", "a-exam-marker-3"];
    private static readonly string[] MarkerRoles = ["First", "Second", "Reconciler"];

    private const int AppRanked = 7, AppReserveListed = 12, AppRejected = 3, AppOfferDeclined = 11;
    private const int OfferPendingApproval = 1, OfferIssued = 2, OfferDeclined = 4;
    // AcceptanceCompetitionPatch: ObjectionWindowDays 7, ReconcileThreshold 10 (acceptance test data).
    private const int ObjectionWindowDays = 7;
    private const string ApplicantNamePrefix = "متقدم المسابقة";

    // ── AC-HCM-31a: competition from approved requisitions → approval (creator excluded) → announcement ─────────────

    [SkippableFact]
    public async Task Ac31_a_competition_groups_requisitions_is_approved_by_others_and_announced()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("31a", "AC-HCM-31a competition: lines, attach approved requisitions, approval, announcement");
        var drafts = new[] { AcceptanceSeedIds.CompetitionDraftRequisition1Id, AcceptanceSeedIds.CompetitionDraftRequisition2Id, AcceptanceSeedIds.CompetitionDraftRequisition3Id }
            .Select(id => id.ToString("D")).ToArray();
        AcceptanceGate.RequireData(s.Recorder,
            await Scalar($"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"Id\" IN ('{string.Join("','", drafts)}') AND \"Status\"='Approved' AND \"CompetitionLineId\" IS NULL;") == 3,
            "طلبات الشواغر الخاصة بـ AC-HCM-31a غير مُهيّأة أو مرتبطة مسبقاً بمسابقة — أعد بناء hr-core-web (AcceptanceCompetitionPatch) أو شغّل scripts/acceptance-reset.ps1",
            "The AC-HCM-31a requisitions are not seeded or already attached — rebuild hr-core-web (AcceptanceCompetitionPatch) or run scripts/acceptance-reset.ps1.");
        s.Recorder.WriteSteps("""
            1. central-a creates a competition (legal inputs entered per competition), adds two lines (grade of each line = grade of
               its requisitions) and attaches the three approved requisitions (2 + 1).
            2. Negative: a requisition already published (attached to the seeded competition) is refused.
            3. Submit → RECRUITMENT-COMPETITION-APPROVAL. Negative: the creator cannot approve. br1-mgr then a-management-approver approve.
            4. Announce → one public posting; /careers/competitions/{id} shows the title and both lines; the requisitions are Published.
            """);

        var grades = (await Sql(
                "SELECT string_agg(p.\"GradeCode\", ',' ORDER BY r.\"RequisitionNumber\") FROM \"RecruitmentRequisitions\" r " +
                $"JOIN \"FundedPositions\" p ON p.\"Id\"=r.\"FundedPositionId\" WHERE r.\"Id\" IN ('{string.Join("','", drafts)}');"))
            .Split(',');
        var title = $"مسابقة قبول AC-HCM-31a {Guid.NewGuid().ToString("N")[..6]}";
        await s.Central.PostAsync("31a-create", "ui/hr-core/Competitions/Create", "ui/hr-core/Competitions/Create",
        [
            new("Title", title),
            new("LegalSourceId", Syr001LegislationIds.Law50_2004),
            new("ApplicationConditions", "شروط التقديم (اختبار قبول)"),
            new("PublishingChannel", "PublicOnly"),
            new("ApplicationClosingDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("ObjectionWindowDays", "7"),
            new("ReserveValidityMonths", "6"),
            new("MaxLinesPerCandidate", "2"),
            new("PassMark", "50"),
            new("ReconcileThreshold", "10"),
            new("ReconcileRule", "ReconcilerDecides"),
            new("TieBreakOrder[0]", "ExamScore"),
            new("TieBreakOrder[1]", "AppliedAt")
        ]);
        var id = await Sql($"SELECT \"Id\" FROM \"RecruitmentCompetitions\" WHERE \"Title\"='{Esc(title)}';");
        Assert.True(Guid.TryParse(id, out _), "31a: the competition was not created.");
        Assert.Equal("Draft|7|50.00", await Sql($"SELECT \"Status\" || '|' || \"ObjectionWindowDays\" || '|' || \"PassMark\"::text FROM \"RecruitmentCompetitions\" WHERE \"Id\"='{id}';"));

        foreach (var (lineTitle, grade) in new[] { ("السطر الأول (قبول)", grades[0]), ("السطر الثاني (قبول)", grades[2]) })
            await s.Central.PostAsync($"31a-line-{grade}", "ui/hr-core/Competitions/SaveLine", $"ui/hr-core/Competitions/AddLine/{id}",
                [new("CompetitionId", id), new("Title", lineTitle), new("GradeCode", grade)]);
        var lines = (await Sql($"SELECT string_agg(\"Id\"::text, ',' ORDER BY \"LineNo\") FROM \"CompetitionLines\" WHERE \"CompetitionId\"='{id}';")).Split(',');
        Assert.Equal(2, lines.Length);
        for (var i = 0; i < 3; i++)
            await s.Central.PostAsync($"31a-attach-{i + 1}", $"ui/hr-core/Competitions/AttachRequisition/{id}", Details(id),
                [new("lineId", i < 2 ? lines[0] : lines[1]), new("requisitionId", drafts[i])]);
        Assert.Equal(3, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"CompetitionLineId\" IN ('{lines[0]}','{lines[1]}');"));

        // Negative: a published requisition (already attached to the seeded competition) cannot be attached.
        var published = AcceptanceSeedIds.CompetitionRequisitionId(0).ToString("D");
        await s.Central.PostAsync("31a-attach-published", $"ui/hr-core/Competitions/AttachRequisition/{id}", Details(id),
            [new("lineId", lines[0]), new("requisitionId", published)]);
        Assert.Equal(AcceptanceSeedIds.CompetitionLine1Id.ToString("D"),
            await Sql($"SELECT \"CompetitionLineId\"::text FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{published}';"));

        await s.Central.PostAsync("31a-submit", $"ui/hr-core/Competitions/Submit/{id}", Details(id), []);
        Assert.Equal("PendingApproval", await CompetitionStatusAsync(id));
        // Negative (SoD): the creator's approval is refused; the competition stays at step 1.
        await s.Central.PostAsync("31a-creator-approve", $"ui/hr-core/Competitions/ApproveWorkflow/{id}", Details(id), [new("comments", "محاولة اعتماد ذاتي")]);
        Assert.Equal("PendingApproval", await CompetitionStatusAsync(id));
        await s.Br1.PostAsync("31a-approve-1", $"ui/hr-core/Competitions/ApproveWorkflow/{id}", Details(id), [new("comments", "AC-HCM-31a step 1")]);
        await s.Approver.PostAsync("31a-approve-2", $"ui/hr-core/Competitions/ApproveWorkflow/{id}", Details(id), [new("comments", "AC-HCM-31a step 2")]);
        Assert.Equal("Approved", await CompetitionStatusAsync(id));

        await s.Central.PostAsync("31a-announce", $"ui/hr-core/Competitions/Announce/{id}", Details(id), []);
        Assert.Equal("Announced", await CompetitionStatusAsync(id));
        Assert.Equal(3, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"Id\" IN ('{string.Join("','", drafts)}') AND \"Status\"='Published';"));
        var publicPage = await AnonymousGetAsync(s, "31a-public-page", $"careers/competitions/{id}");
        Assert.Contains(title, publicPage);
        Assert.Contains("السطر الأول (قبول)", publicPage);
        Assert.Contains("السطر الثاني (قبول)", publicPage);

        s.Recorder.WriteDbChecks(new { competitionId = id, lines, attached = drafts, status = "Announced" });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-31b..g: seating, admission cards, blind double marking, results, objection, allocation, offers, recall ──

    [SkippableFact]
    public async Task Ac31_b_to_g_exam_results_objection_allocation_bulk_offers_and_reserve_recall()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("31b-g", "AC-HCM-31b..g seating → marking → results → objection → allocation → offers → recall");
        AcceptanceGate.RequireData(s.Recorder, await CompetitionStatusAsync(CompetitionId) == "IntakeClosed",
            $"المسابقة {AcceptanceSeedIds.CompetitionNumber} ليست في حالة إغلاق التقديم (نقلها تشغيل سابق) — شغّل scripts/acceptance-reset.ps1",
            $"Competition {AcceptanceSeedIds.CompetitionNumber} is not at IntakeClosed (an earlier run moved it on) — run scripts/acceptance-reset.ps1.");
        s.Recorder.WriteSteps("""
            31b. central-a seats the 120 admitted applicants in the two sessions (random 8-digit exam numbers), issues the admission
                 cards (PDF), records attendance (2 absent per session) and confirms the sessions were held.
            31c. Three markers are assigned per session by employee. Each marker sees exam numbers only (no name, no national id) and
                 no HR page. First and second markers mark every present paper; one paper differs by 20 (> threshold 10) and the
                 reconciler decides it. central-a closes marking.
            31d. Results computed by central-a; approval by br1-mgr (committee) then a-management-approver (central-a, who computed,
                 is refused); published. The public list shows exam numbers and outcomes, never a name or national id. Candidates
                 with consent get RESULTS_PUBLISHED (no score).
            31e. HR files an objection on behalf of the second portal applicant (by exam number); finalization is refused while it is
                 pending; a-compliance upholds it with a corrected mark → result version 2 (approved and published); v1 superseded;
                 the window still starts at the first publication.
            31f. Test-only SQL moves the first publication back past the window; finalization allocates the five vacancies by merit
                 and preference, lists the reserve per line and rejects the rest (kind CompetitionResult). A second call changes nothing.
            31g. Bulk offer drafts for the allocated candidates go into the existing per-offer approval; one offer is declined →
                 the first valid reserve of the same line is recalled onto the vacated requisition.
            """);

        // ── 31b ──
        await s.Central.PostAsync("31b-seat", $"ui/hr-core/Competitions/Seat/{CompetitionId}", Details(CompetitionId), []);
        Assert.Equal(120, await Scalar($"SELECT COUNT(*) FROM \"ExamCandidates\" WHERE \"CompetitionId\"='{CompetitionId}';"));
        Assert.Equal(120, await Scalar($"SELECT COUNT(DISTINCT \"ExamNumber\") FROM \"ExamCandidates\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"ExamNumber\" ~ '^[0-9]{{8}}$';"));
        Assert.Equal("Examining", await CompetitionStatusAsync(CompetitionId));
        await s.Central.PostAsync("31b-cards", $"ui/hr-core/Competitions/IssueAdmissionCards/{CompetitionId}", Details(CompetitionId), []);
        Assert.Equal(120, await Scalar($"SELECT COUNT(*) FROM \"ExamCandidates\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"AdmissionCardSha256\" IS NOT NULL;"));
        var firstPaper = await Sql($"SELECT \"Id\" FROM \"ExamCandidates\" WHERE \"CompetitionId\"='{CompetitionId}' ORDER BY \"ExamNumber\" LIMIT 1;");
        var card = await s.Central.GetRawAsync("31b-card-pdf", $"ui/hr-core/Competitions/AdmissionCard/{firstPaper}");
        Assert.Equal(HttpStatusCode.OK, card.Status);
        Assert.Equal("application/pdf", card.ContentType);
        Assert.Equal("%PDF", card.Head);

        var papersBySession = new Dictionary<string, List<(string Id, string Number)>>();
        foreach (var sessionId in SessionIds)
        {
            var rows = (await Sql($"SELECT string_agg(\"Id\"::text || ':' || \"ExamNumber\", ',' ORDER BY \"ExamNumber\") FROM \"ExamCandidates\" WHERE \"ExamSessionId\"='{sessionId}';"))
                .Split(',').Select(r => r.Split(':')).Select(p => (p[0], p[1])).ToList();
            papersBySession[sessionId] = rows;
            var absent = rows.TakeLast(2).Select(r => r.Item1).ToList();
            var fields = rows.Select(r => new KeyValuePair<string, string>(absent.Contains(r.Item1) ? "absent" : "present", r.Item1)).ToList();
            await s.Central.PostAsync($"31b-attendance-{sessionId[^2..]}", $"ui/hr-core/Competitions/SetAttendance/{sessionId}", SessionPage(sessionId), fields);
            await s.Central.PostAsync($"31b-held-{sessionId[^2..]}", $"ui/hr-core/Competitions/MarkSessionHeld/{sessionId}", SessionPage(sessionId), []);
            Assert.Equal("Held", await Sql($"SELECT \"Status\" FROM \"ExamSessions\" WHERE \"Id\"='{sessionId}';"));
        }

        // ── 31c ──
        var markers = new List<Ui>();
        for (var m = 0; m < 3; m++)
        {
            var employee = AcceptancePersonaCatalog.Get(MarkerPersonas[m]).Employee;
            Assert.True(employee is not null, $"31c: persona {MarkerPersonas[m]} has no employee.");
            foreach (var sessionId in SessionIds)
                await s.Central.PostAsync($"31c-assign-{m + 1}-{sessionId[^2..]}", $"ui/hr-core/Competitions/AssignMarker/{CompetitionId}", Details(CompetitionId),
                    [new("sessionId", sessionId), new("employeeId", employee!.Id.ToString("D")), new("role", MarkerRoles[m])]);
            markers.Add(await PersonaUiAsync(s, MarkerPersonas[m]));
        }
        Assert.Equal(6, await Scalar($"SELECT COUNT(*) FROM \"ExamMarkerAssignments\" WHERE \"ExamSessionId\" IN ('{SessionIds[0]}','{SessionIds[1]}');"));

        // Blindness: the marker's screens carry exam numbers only, and HR pages are closed to the marker role.
        var markerSession = await markers[0].GetAsync("31c-marker-session", $"ui/hr-core/ExamMarking/Session/{SessionIds[0]}");
        Assert.Contains(papersBySession[SessionIds[0]][0].Number, markerSession);
        Assert.DoesNotContain(ApplicantNamePrefix, markerSession);
        Assert.DoesNotContain("31700000", markerSession);
        var hrPage = await markers[0].GetRawAsync("31c-marker-hr-denied", "ui/hr-core/RecruitmentApplications");
        Assert.True(hrPage.Status is HttpStatusCode.Forbidden or HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"31c: the marker reached the HR applications list ({(int)hrPage.Status}).");

        var expected = new Dictionary<string, decimal>();
        string? reconciledPaper = null;
        foreach (var sessionId in SessionIds)
        {
            var present = papersBySession[sessionId].SkipLast(2).ToList();
            for (var k = 0; k < present.Count; k++)
            {
                var (paperId, _) = present[k];
                var mark = 30m + (k * 13 + (sessionId == SessionIds[1] ? 7 : 0)) % 70;
                var second = sessionId == SessionIds[0] && k == 3 ? mark + 20m : mark;
                await SubmitMarkAsync(markers[0], $"31c-m1-{sessionId[^2..]}-{k}", paperId, sessionId, mark);
                await SubmitMarkAsync(markers[1], $"31c-m2-{sessionId[^2..]}-{k}", paperId, sessionId, second);
                if (second != mark)
                {
                    reconciledPaper = paperId;
                    await SubmitMarkAsync(markers[2], "31c-m3-reconcile", paperId, sessionId, mark + 10m);
                    expected[paperId] = mark + 10m; // ReconcilerDecides
                }
                else
                    expected[paperId] = mark;
            }
            await s.Central.PostAsync($"31c-close-{sessionId[^2..]}", $"ui/hr-core/Competitions/CloseMarking/{sessionId}", SessionPage(sessionId), []);
            Assert.Equal("MarkingClosed", await Sql($"SELECT \"Status\" FROM \"ExamSessions\" WHERE \"Id\"='{sessionId}';"));
        }
        Assert.NotNull(reconciledPaper);
        Assert.Equal(1, await Scalar(
            $"SELECT COUNT(*) FROM \"ExamCandidates\" WHERE \"Id\"='{reconciledPaper}' AND \"FinalMark\" = {expected[reconciledPaper!].ToString(CultureInfo.InvariantCulture)};"));
        Assert.Equal(116, await Scalar($"SELECT COUNT(*) FROM \"ExamCandidates\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"FinalMark\" IS NOT NULL;"));

        // ── 31d ──
        await s.Central.PostAsync("31d-compute", $"ui/hr-core/Competitions/ComputeResults/{CompetitionId}", Details(CompetitionId), []);
        var v1 = await Sql($"SELECT \"Id\" FROM \"CompetitionResultVersions\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"VersionNo\"=1;");
        Assert.True(Guid.TryParse(v1, out _), "31d: no result version 1.");
        await ApproveAndPublishAsync(s, "31d", v1, expectSelfApprovalRefused: true);
        Assert.Equal("ResultsPublished", await CompetitionStatusAsync(CompetitionId));
        var publicResults = await AnonymousGetAsync(s, "31d-public-results", $"careers/competitions/{CompetitionId}/results");
        Assert.Contains(papersBySession[SessionIds[0]][0].Number, publicResults);
        Assert.DoesNotContain(ApplicantNamePrefix, publicResults);
        Assert.DoesNotContain("31700000", publicResults);
        var portalApps = $"'{AcceptanceSeedIds.CompetitionApplicantId(0)}','{AcceptanceSeedIds.CompetitionApplicantId(1)}'";
        await WaitForCountAsync(s, "31d-results-published-message", TimeSpan.FromMinutes(4), 2,
            $"SELECT COUNT(*) FROM \"CandidateNotifications\" WHERE \"TemplateCode\"='RESULTS_PUBLISHED' AND \"RecruitmentApplicationId\" IN ({portalApps});");
        Assert.Equal(0, await Scalar(
            $"SELECT COUNT(*) FROM \"CandidateNotifications\" WHERE \"RecruitmentApplicationId\" IN ({portalApps}) AND (\"Body\" LIKE '%درجة%' OR \"Body\" LIKE '%علامة%' OR \"Body\" LIKE '%31700000%');"));

        // ── 31e ──
        var objector = AcceptanceSeedIds.CompetitionApplicantId(1).ToString("D");
        var objectorPaper = await Sql($"SELECT \"Id\" || '|' || \"ExamNumber\" || '|' || COALESCE(\"FinalMark\"::text,'') FROM \"ExamCandidates\" WHERE \"RecruitmentApplicationId\"='{objector}';");
        var (paper, examNumber, oldMark) = (objectorPaper.Split('|')[0], objectorPaper.Split('|')[1], objectorPaper.Split('|')[2]);
        AcceptanceGate.RequireData(s.Recorder, oldMark.Length != 0,
            "31e: سُجّل المتقدم المعترض غائباً في هذا التشغيل (ترتيب المقاعد) — أعد التشغيل بعد إعادة التهيئة",
            "31e: the objecting applicant was marked absent in this run (seating order) — rerun after reset.");
        var firstPublishedAt = await Sql($"SELECT \"ResultsFirstPublishedAt\"::text FROM \"RecruitmentCompetitions\" WHERE \"Id\"='{CompetitionId}';");
        await s.Central.PostAsync("31e-file", $"ui/hr-core/Competitions/FileObjection/{CompetitionId}", Details(CompetitionId),
            [new("examNumber", examNumber), new("reason", "يطلب المتقدم إعادة تدقيق جمع علامات ورقته (اختبار قبول)")]);
        var appealId = await Sql($"SELECT \"Id\" FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{objector}' AND \"Kind\"='ResultObjection' AND \"Status\"=1;");
        Assert.True(Guid.TryParse(appealId, out _), "31e: the objection was not recorded.");
        await s.Central.PostAsync("31e-finalize-refused", $"ui/hr-core/Competitions/Finalize/{CompetitionId}", Details(CompetitionId), []);
        Assert.Equal("ResultsPublished", await CompetitionStatusAsync(CompetitionId));

        // The independent reviewer is assigned by HR (PLAN-28), then decides (the competition's markers are excluded).
        var reviewerEmployee = AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id.ToString("D");
        await s.Central.PostAsync("31e-assign-reviewer", "ui/hr-core/RecruitmentAppeals/AssignReviewer", $"ui/hr-core/RecruitmentAppeals/Details/{appealId}",
            [new("id", appealId), new("reviewerEmployeeId", reviewerEmployee)]);
        Assert.Equal(reviewerEmployee, await Sql($"SELECT \"IndependentReviewerEmployeeId\"::text FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appealId}';"));
        var compliance = await PersonaUiAsync(s, "a-compliance");
        var corrected = Math.Min(100m, decimal.Parse(oldMark, CultureInfo.InvariantCulture) + 15m);
        await compliance.PostAsync("31e-decide", "ui/hr-core/RecruitmentAppeals/Decide", $"ui/hr-core/RecruitmentAppeals/Details/{appealId}",
        [
            new("id", appealId),
            new("outcome", "ObjectionUpheld"),
            new("rationale", "خطأ في جمع العلامات ثابت بالتدقيق المستقل (اختبار قبول)"),
            new("correctedScore", corrected.ToString(CultureInfo.InvariantCulture))
        ]);
        var v2 = await Sql($"SELECT \"Id\" FROM \"CompetitionResultVersions\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"VersionNo\"=2;");
        Assert.True(Guid.TryParse(v2, out _), "31e: the upheld objection did not produce result version 2.");
        await ApproveAndPublishAsync(s, "31e", v2, expectSelfApprovalRefused: false);
        Assert.Equal("Superseded|Published", await Sql(
            $"SELECT string_agg(\"Status\", '|' ORDER BY \"VersionNo\") FROM \"CompetitionResultVersions\" WHERE \"CompetitionId\"='{CompetitionId}';"));
        Assert.Equal(firstPublishedAt, await Sql($"SELECT \"ResultsFirstPublishedAt\"::text FROM \"RecruitmentCompetitions\" WHERE \"Id\"='{CompetitionId}';"));
        Assert.Equal(1, await Scalar(
            $"SELECT COUNT(*) FROM \"ExamScores\" WHERE \"ExamCandidateId\"='{paper}' AND \"Round\"='ObjectionCorrection' AND \"Score\" = {corrected.ToString(CultureInfo.InvariantCulture)};"));
        await WaitForCountAsync(s, "31e-objection-message", TimeSpan.FromMinutes(4), 1,
            $"SELECT COUNT(*) FROM \"CandidateNotifications\" WHERE \"TemplateCode\"='OBJECTION_DECIDED' AND \"RecruitmentApplicationId\"='{objector}';");

        // ── 31f ── Test-only SQL precondition: the objection window (7 days from the first publication) is moved into the past.
        await Sql($"UPDATE \"RecruitmentCompetitions\" SET \"ResultsFirstPublishedAt\" = now() - interval '{ObjectionWindowDays + 1} days' WHERE \"Id\"='{CompetitionId}' RETURNING 1;");
        await s.Central.PostAsync("31f-finalize", $"ui/hr-core/Competitions/Finalize/{CompetitionId}", Details(CompetitionId), []);
        Assert.Equal("Finalized", await CompetitionStatusAsync(CompetitionId));
        Assert.Equal(5, await Scalar($"SELECT COUNT(DISTINCT \"AllocatedRequisitionId\") FROM \"CompetitionAllocations\" WHERE \"CompetitionId\"='{CompetitionId}';"));
        Assert.Equal(5, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentApplications\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"Status\"={AppRanked};"));
        var reserveCount = await Scalar($"SELECT COUNT(*) FROM \"RecruitmentApplications\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"Status\"={AppReserveListed};");
        Assert.True(reserveCount > 0, "31f: no reserve list.");
        Assert.Equal(0, await Scalar(
            $"SELECT COUNT(*) FROM \"RecruitmentApplications\" WHERE \"CompetitionId\"='{CompetitionId}' AND \"Status\" NOT IN ({AppRanked},{AppReserveListed},{AppRejected});"));
        // Merit: no allocated candidate ranks below a passed candidate who got nothing on a line he preferred first.
        Assert.Equal(0, await Scalar(
            "SELECT COUNT(*) FROM \"RecruitmentApplications\" a JOIN \"RecruitmentApplications\" b ON b.\"CompetitionId\"=a.\"CompetitionId\" " +
            "JOIN \"CompetitionPreferences\" pb ON pb.\"RecruitmentApplicationId\"=b.\"Id\" AND pb.\"Order\"=1 AND pb.\"CompetitionLineId\"=a.\"CompetitionLineId\" " +
            $"WHERE a.\"CompetitionId\"='{CompetitionId}' AND a.\"Status\"={AppRanked} AND b.\"Status\"={AppReserveListed} AND b.\"AssessmentScore\" > a.\"AssessmentScore\";"));
        var allocationsBefore = await Scalar($"SELECT COUNT(*) FROM \"CompetitionAllocations\" WHERE \"CompetitionId\"='{CompetitionId}';");
        await s.Central.PostAsync("31f-finalize-again", $"ui/hr-core/Competitions/Finalize/{CompetitionId}", Details(CompetitionId), []);
        Assert.Equal(allocationsBefore, await Scalar($"SELECT COUNT(*) FROM \"CompetitionAllocations\" WHERE \"CompetitionId\"='{CompetitionId}';"));

        // ── 31g ──
        var draftsPage = await s.Central.PostAsync("31g-offer-drafts", $"ui/hr-core/Competitions/CreateOfferDrafts/{CompetitionId}", Details(CompetitionId),
        [
            new("proposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("validityDays", "14")
        ]);
        AcceptanceGate.RequireData(s.Recorder, !draftsPage.Contains("تعذّر التحقق من سلم الرواتب", StringComparison.Ordinal),
            "تعذّر على HR.Core الوصول إلى خدمة الرواتب للاستعلام عن حدود الدرجة",
            "HR.Core cannot reach the Payroll grade lookup (see PLAN-27-EXECUTION).");
        var offers = (await Sql(
                "SELECT string_agg(o.\"Id\"::text, ',' ORDER BY a.\"Rank\") FROM \"OfferLetters\" o JOIN \"RecruitmentApplications\" a ON a.\"Id\"=o.\"RecruitmentApplicationId\" " +
                $"WHERE a.\"CompetitionId\"='{CompetitionId}';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, offers.Length);
        await s.Central.PostAsync("31g-submit-drafts", $"ui/hr-core/Competitions/SubmitOfferDrafts/{CompetitionId}", Details(CompetitionId), []);
        Assert.Equal(5, await Scalar($"SELECT COUNT(*) FROM \"OfferLetters\" WHERE \"Id\" IN ('{string.Join("','", offers)}') AND \"Status\"={OfferPendingApproval};"));
        var declined = offers[0];
        foreach (var (step, ui) in new[] { ("1", s.Br1), ("2", s.Approver) })
            await ui.PostAsync($"31g-approve-offer-{step}", $"ui/hr-core/OfferLetters/ApproveWorkflow/{declined}", OfferDetails(declined),
                [new("OfferId", declined), new("Comments", $"AC-HCM-31g step {step}")]);
        Assert.Equal(OfferIssued, (int)await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{declined}';"));

        var vacated = await Sql(
            $"SELECT a.\"RequisitionId\" || '|' || a.\"CompetitionLineId\" FROM \"OfferLetters\" o JOIN \"RecruitmentApplications\" a ON a.\"Id\"=o.\"RecruitmentApplicationId\" WHERE o.\"Id\"='{declined}';");
        var (vacatedRequisition, line) = (vacated.Split('|')[0], vacated.Split('|')[1]);
        var firstReserve = await Sql(
            $"SELECT \"RecruitmentApplicationId\" FROM \"CompetitionReserveEntries\" WHERE \"CompetitionLineId\"='{line}' AND \"Status\"='Active' ORDER BY \"LineRank\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(firstReserve, out _),
            "31g: لا يوجد احتياطي نشط على سطر العرض المرفوض في هذا التشغيل",
            "31g: the declined offer's line has no active reserve in this run.");
        await s.Central.PostAsync("31g-decline", $"ui/hr-core/OfferLetters/DeclineOffer/{declined}", OfferDetails(declined),
            [new("id", declined), new("ReasonCategory", "Personal"), new("Reason", "اعتذر المرشح عن الالتحاق (اختبار قبول)")]);
        Assert.Equal(OfferDeclined, (int)await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{declined}';"));
        Assert.Equal($"{AppRanked}|{vacatedRequisition}|{line}", await Sql(
            $"SELECT \"Status\"::text || '|' || \"RequisitionId\" || '|' || \"CompetitionLineId\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{firstReserve}';"));
        Assert.Equal("Recalled", await Sql(
            $"SELECT \"Status\" FROM \"CompetitionReserveEntries\" WHERE \"RecruitmentApplicationId\"='{firstReserve}' AND \"CompetitionLineId\"='{line}';"));
        Assert.Equal(1, await Scalar(
            $"SELECT COUNT(*) FROM \"RecruitmentApplicationEvents\" WHERE \"RecruitmentApplicationId\"='{firstReserve}' AND \"EventType\"='CompetitionReserveRecalled';"));
        Assert.Equal(AppOfferDeclined, (int)await Scalar(
            $"SELECT a.\"Status\" FROM \"RecruitmentApplications\" a JOIN \"OfferLetters\" o ON o.\"RecruitmentApplicationId\"=a.\"Id\" WHERE o.\"Id\"='{declined}';"));

        s.Recorder.WriteDbChecks(new
        {
            seated = 120,
            reconciledPaper,
            v1,
            v2,
            appealId,
            reserveCount,
            allocations = allocationsBefore,
            declinedOffer = declined,
            recalled = firstReserve,
            vacatedRequisition
        });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── Journeys ─────────────────────────────────────────────────────────────

    /// <summary>Submit (central-a) → negative self-approval when asked → br1-mgr → a-management-approver → publish.</summary>
    private static async Task ApproveAndPublishAsync(Session s, string step, string versionId, bool expectSelfApprovalRefused)
    {
        await s.Central.PostAsync($"{step}-submit-results", $"ui/hr-core/Competitions/SubmitResults/{CompetitionId}", Details(CompetitionId), [new("versionId", versionId)]);
        Assert.Equal("PendingApproval", await VersionStatusAsync(versionId));
        if (expectSelfApprovalRefused)
        {
            await s.Central.PostAsync($"{step}-self-approve", $"ui/hr-core/Competitions/ApproveResultsWorkflow/{CompetitionId}", Details(CompetitionId),
                [new("versionId", versionId), new("comments", "محاولة اعتماد ذاتي")]);
            Assert.Equal("PendingApproval", await VersionStatusAsync(versionId));
        }
        await s.Br1.PostAsync($"{step}-approve-results-1", $"ui/hr-core/Competitions/ApproveResultsWorkflow/{CompetitionId}", Details(CompetitionId),
            [new("versionId", versionId), new("comments", $"AC-HCM-{step} لجنة المسابقة")]);
        await s.Approver.PostAsync($"{step}-approve-results-2", $"ui/hr-core/Competitions/ApproveResultsWorkflow/{CompetitionId}", Details(CompetitionId),
            [new("versionId", versionId), new("comments", $"AC-HCM-{step} مدير الموارد البشرية")]);
        Assert.Equal("Approved", await VersionStatusAsync(versionId));
        await s.Central.PostAsync($"{step}-publish", $"ui/hr-core/Competitions/PublishResults/{CompetitionId}", Details(CompetitionId), [new("versionId", versionId)]);
        Assert.Equal("Published", await VersionStatusAsync(versionId));
    }

    private static async Task SubmitMarkAsync(Ui marker, string step, string paperId, string sessionId, decimal score)
    {
        await marker.PostAsync(step, $"ui/hr-core/ExamMarking/Submit/{paperId}", $"ui/hr-core/ExamMarking/Paper/{paperId}?sessionId={sessionId}",
            [new("score", score.ToString(CultureInfo.InvariantCulture)), new("sessionId", sessionId)]);
    }

    private static async Task WaitForCountAsync(Session s, string timing, TimeSpan timeout, long expected, string sql)
    {
        var started = DateTime.UtcNow;
        long count;
        do
        {
            count = await Scalar(sql);
            if (count >= expected)
                break;
            await Task.Delay(TimeSpan.FromSeconds(3));
        } while (DateTime.UtcNow - started < timeout);
        s.Recorder.RecordTiming(timing, (DateTime.UtcNow - started).TotalMilliseconds);
        Assert.True(count >= expected, $"{timing}: {count} of {expected} after {timeout.TotalSeconds:0} s.");
    }

    // ── Session, personas ────────────────────────────────────────────────────

    private sealed record Session(EvidenceRecorder Recorder, string Tag, string CorrelationId, Ui Central, Ui Br1, Ui Approver);

    private static async Task<Session> StartAsync(string tag, string scenario)
    {
        var central = AcceptanceHarness.RequirePersona("central-a");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var approver = AcceptanceHarness.RequirePersona("a-management-approver");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        recorder.WriteMetadata(scenario, $"{central.Id}+{br1.Id}+{approver.Id}+a-compliance+a-exam-marker(-2,-3)+anonymous");
        return new Session(
            recorder,
            tag,
            correlationId,
            new Ui(await CreateClientAsync(central, correlationId), recorder, central.Id, tag),
            new Ui(await CreateClientAsync(br1, correlationId), recorder, br1.Id, tag),
            new Ui(await CreateClientAsync(approver, correlationId), recorder, approver.Id, tag));
    }

    private static async Task<Ui> PersonaUiAsync(Session s, string personaId)
    {
        var persona = AcceptanceHarness.RequirePersona(personaId);
        return new Ui(await CreateClientAsync(persona, s.CorrelationId), s.Recorder, persona.Id, s.Tag);
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

    private static async Task<string> AnonymousGetAsync(Session s, string step, string path)
    {
        using var http = CreateAnonymousClient();
        using var resp = await http.GetAsync(path);
        var body = await resp.Content.ReadAsStringAsync();
        s.Recorder.CaptureHttp($"{s.Tag}-{step}-anonymous", "GET", "/" + path, (int)resp.StatusCode, body.Length > 600 ? body[..600] : body);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} (anonymous) returned {(int)resp.StatusCode}.");
        return WebUtility.HtmlDecode(body);
    }

    private static string Details(string id) => $"ui/hr-core/Competitions/Details/{id}";

    private static string SessionPage(string sessionId) => $"ui/hr-core/Competitions/ExamSession/{sessionId}";

    private static string OfferDetails(string offerId) => $"ui/hr-core/OfferLetters/Details/{offerId}";

    private static string Esc(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    // ── DB helpers ────────────────────────────────────────────────────────────

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);
    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);

    private static Task<string> CompetitionStatusAsync(string id) =>
        Sql($"SELECT \"Status\" FROM \"RecruitmentCompetitions\" WHERE \"Id\"='{id}';");

    private static Task<string> VersionStatusAsync(string versionId) =>
        Sql($"SELECT \"Status\" FROM \"CompetitionResultVersions\" WHERE \"Id\"='{versionId}';");

    // ── HTTP client ───────────────────────────────────────────────────────────

    /// <summary>Gateway UI client for one persona: antiforgery-aware form posts with evidence capture (as AC-HCM-26..30).</summary>
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
                new { fields = fields.Select(f => $"{f.Key}={f.Value}"), postStatus, redirect, final = resp.RequestMessage?.RequestUri?.AbsolutePath });
            Assert.True((int)resp.StatusCode < 500, $"POST {path} as {persona} returned {(int)resp.StatusCode}.");
            Assert.True(resp.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"POST {path} as {persona} was denied.");
            return WebUtility.HtmlDecode(body);
        }
    }
}
