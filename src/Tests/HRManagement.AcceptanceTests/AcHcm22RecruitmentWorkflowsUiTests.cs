using System.Globalization;
using System.Net;
using HRManagement.ServiceDefaults.Seeding;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-22a–e (PLAN-22, rewritten in PLAN-33 on the current design): offer approval and acceptance, offer SoD,
/// selection committee quorum and approval (PLAN-28), the appeal through an assigned independent compliance reviewer
/// (PLAN-28), and lab-scale offer submission. Seeded states (<see cref="RecruitmentStates"/>) serve the read/refusal
/// checks; state-changing flows create their own requisition on an AC33-POOL position. Ranking is AC-HCM-26's scope:
/// where a scenario needs a Ranked or Assessed candidate it sets that precondition by test-only SQL (as AC-HCM-27/28 do);
/// the full real path runs in the requisition-to-hire E2E journey (AC-HCM-33c).
/// </summary>
[RequiredAcceptance]
public class AcHcm22RecruitmentWorkflowsUiTests
{
    private const string Ac = "AC-HCM-22";
    private const string Rationale = "ثبت للمراجع المستقل أن قرار الفرز لم يراعِ المؤهلات المرفقة بالطلب";
    private const string AppealReason = "يعترض المرشح على قرار الفرز لعدم احتساب مؤهلاته العلمية";
    private static readonly string[] Approvers = ["central-a", "br1-mgr", "a-management-approver"];

    [SkippableFact]
    public async Task Ac22_a_offer_two_step_approval_issued_then_accepted_with_evidence()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "22a", "AC-HCM-22a ranked application → offer → two-step approval → Issued → accepted", Approvers);
        s.Steps("""
            1. central-a publishes a requisition on an AC33-POOL position (two-step approval) and enters an HR application.
            2. Precondition (test-only SQL, ranking is AC-HCM-26): the application is Ranked 1 with COI declared.
            3. central-a creates an offer at the G5 mid salary (Draft; application OfferExtended) and submits it.
            4. br1-mgr approves step 1, a-management-approver step 2 → Issued with an expiry date.
            5. central-a records the candidate's acceptance on their behalf with the signed PDF (File.Storage) →
               offer Accepted (AcceptedBy HrOnBehalf, evidence file id stored, audit AcceptOfferOnBehalf), application OfferAccepted.
            Candidate self-acceptance on /careers/my with OTP is covered by AC-HCM-30 and by the E2E journey.
            """);

        var (reqId, appId) = await RankedApplicationAsync(s, "22a");
        var offerId = await CreateOfferAsync(s, "22a", appId);
        Assert.Equal($"{OfferDraft}||", await OfferStateAsync(offerId));
        Assert.Equal(AppOfferExtended, await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';"));

        await s["central-a"].PostAsync("22a-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offerId), [new("id", offerId)]);
        var pending = (await OfferStateAsync(offerId)).Split('|');
        Assert.Equal([OfferPending.ToString(CultureInfo.InvariantCulture), "1"], [pending[0], pending[2]]);
        Assert.Equal("RECRUITMENT-OFFER-APPROVAL", await WorkflowCodeAsync(pending[1]));
        await ApproveOfferAsync(s["br1-mgr"], "22a-approve-1", offerId);
        Assert.Equal("2", (await OfferStateAsync(offerId)).Split('|')[2]);
        await ApproveOfferAsync(s["a-management-approver"], "22a-approve-2", offerId);
        Assert.Equal($"{OfferIssued}|true", await Sql(
            $"SELECT \"Status\"::text || '|' || (\"ExpiryDate\" > now())::text FROM \"OfferLetters\" WHERE \"Id\"='{offerId}';"));

        await s["central-a"].PostAsync("22a-accept-on-behalf", "ui/hr-core/OfferLetters/AcceptOffer", OfferDetails(offerId),
            OfferOnBehalfEvidence.AcceptFields(offerId), withEvidence: true);
        var accepted = await Sql(
            "SELECT o.\"Status\"::text || '|' || COALESCE(o.\"AcceptedBy\",'') || '|' || (o.\"AcceptanceEvidenceFileId\" IS NOT NULL)::text || '|' || " +
            $"a.\"Status\"::text FROM \"OfferLetters\" o JOIN \"RecruitmentApplications\" a ON a.\"Id\"=o.\"RecruitmentApplicationId\" WHERE o.\"Id\"='{offerId}';");
        Assert.Equal($"{OfferAccepted}|HrOnBehalf|true|{AppOfferAccepted}", accepted);
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{offerId}' AND \"Action\"='AcceptOfferOnBehalf';"));

        await s.AuditAsync(reqId, appId, offerId);
        s.Checks(new { requisitionId = reqId, applicationId = appId, offerId, workflowInstanceId = pending[1], accepted });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac22_b_offer_creator_cannot_approve_own_offer()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "22b", "AC-HCM-22b offer creator SoD (UI and direct POST)", Approvers);
        var offer = RecruitmentStates.Load().Offers.PendingApproval;
        var id = offer.Id.ToString("D");
        s.Steps($"""
            Seed: {offer.Number} (PendingApproval, step 1 of RECRUITMENT-OFFER-APPROVAL, created by central-a).
            1. central-a opens the offer: «لا يمكن للمنشئ اعتماد عرضه الخاص.» and no approve form.
            2. br1-mgr (control) gets the approve form.
            3. Negative: central-a POSTs ApproveWorkflow directly → refused; status and step unchanged.
            """);

        var before = await OfferStateAsync(id);
        AcceptanceGate.RequireData(s.Recorder, before == $"{OfferPending}|{offer.WorkflowInstanceId:D}|1",
            "حالة العرض المعلق المبذور تغيّرت — شغّل scripts/acceptance-reset.ps1",
            $"Seeded {offer.Number} is no longer PendingApproval at step 1 ({before}) — run scripts/acceptance-reset.ps1.");
        s.Recorder.WriteBefore(new { offerId = id, state = before });

        var creatorPage = await s["central-a"].GetAsync("22b-creator-details", OfferDetails(id));
        Assert.Contains("لا يمكن للمنشئ اعتماد عرضه الخاص.", creatorPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveWorkflow", creatorPage, StringComparison.Ordinal);
        Assert.Contains("ApproveWorkflow", await s["br1-mgr"].GetAsync("22b-br1-details", OfferDetails(id)), StringComparison.Ordinal);

        await ApproveOfferAsync(s["central-a"], "22b-creator-post-approve", id);
        var after = await OfferStateAsync(id);
        Assert.Equal(before, after);

        await s.AuditAsync(id);
        s.Checks(new { offerId = id, before, after, expectedRefusal = "لا يمكن للمنشئ اعتماد عرضه الخاص." });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac22_c_committee_quorum_submit_and_three_step_approval()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "22c", "AC-HCM-22c committee: quorum refused below 3 members; chair + 3 → submit → approvals → ApprovedAt", Approvers);
        var states = RecruitmentStates.Load();
        var shortSession = states.Committee.ShortSessionId.ToString("D");
        s.Steps($"""
            Negative (seed): the session {shortSession} has a chair only → submit refused, SubmittedAt stays empty.
            Positive:
            1. central-a publishes a requisition on an AC33-POOL position and enters an HR application;
               precondition (test-only SQL, assessment is AC-HCM-26): the application is Assessed.
            2. central-a creates a Ranking session and adds the seeded roster (session {states.Committee.ReadySessionId:D}):
               a chair + 3 members, all linked to no persona.
            3. Every member votes Agree; decision Recommend with proposed rank 1; submit → SubmittedAt/LockedAt, RECRUITMENT-SELECTION-COMMITTEE.
            4. br1-mgr approves steps 1 and 2, a-management-approver step 3 → ApprovedAt set.
            """);

        Assert.Equal("", await Sql($"SELECT COALESCE(\"SubmittedAt\"::text,'') FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{shortSession}';"));
        await s["central-a"].PostAsync("22c-short-submit", $"ui/hr-core/SelectionCommittees/Submit/{shortSession}", SessionDetails(shortSession),
            [new("id", shortSession)]);
        Assert.Equal("||", await Sql(
            "SELECT COALESCE(\"SubmittedAt\"::text,'') || '|' || COALESCE(\"LockedAt\"::text,'') || '|' || COALESCE(\"WorkflowInstanceId\"::text,'') " +
            $"FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{shortSession}';"));

        var roster = (await Sql(
                "SELECT string_agg(\"EmployeeId\"::text || ':' || \"Role\", ',' ORDER BY \"Role\", \"Id\") FROM \"SelectionCommitteeMembers\" " +
                $"WHERE \"SessionId\"='{states.Committee.ReadySessionId:D}';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split(':')).ToList();
        AcceptanceGate.RequireData(s.Recorder, roster.Count == 4,
            "قائمة أعضاء اللجنة المبذورة غير مكتملة", "The seeded committee roster (chair + 3) is missing — run scripts/acceptance-reset.ps1.");

        var reqId = await PublishRequisitionAsync(s, "22c");
        var appId = await CreateApplicationAsync(s, "22c", reqId);
        await Sql($"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppAssessed}, \"AssessmentScore\"=84, \"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
        s.Recorder.WriteBefore(new { requisitionId = reqId, applicationId = appId, roster });

        await s["central-a"].PostAsync("22c-create-session", "ui/hr-core/SelectionCommittees/CreateSession",
            $"ui/hr-core/SelectionCommittees?requisitionId={reqId}",
        [
            new("requisitionId", reqId), new("committeeDate", DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("findings", "جلسة ترتيب PLAN-33 22c"), new("purpose", "Ranking")
        ]);
        var session = await Sql($"SELECT \"Id\" FROM \"SelectionCommitteeSessions\" WHERE \"RequisitionId\"='{reqId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(session, out _), "22c: committee session was not created.");
        foreach (var member in roster)
            await s["central-a"].PostAsync($"22c-add-{member[0][..8]}", "ui/hr-core/SelectionCommittees/AddMember", SessionDetails(session),
                [new("sessionId", session), new("employeeId", member[0]), new("role", member[1]), new("hasConflictOfInterest", "false")]);
        var members = (await Sql($"SELECT string_agg(\"Id\"::text, ',') FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{session}';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, members.Length);
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{session}' AND \"Role\"='Chair';"));

        foreach (var member in members)
            await s["central-a"].PostAsync($"22c-vote-{member[..8]}", "ui/hr-core/SelectionCommittees/RecordVote", SessionDetails(session),
                [new("sessionId", session), new("memberId", member), new("applicationId", appId), new("vote", "Agree")]);
        await s["central-a"].PostAsync("22c-decision", "ui/hr-core/SelectionCommittees/UpsertDecision", SessionDetails(session),
        [
            new("sessionId", session), new("applicationId", appId), new("recommendation", "Recommend"), new("proposedRank", "1"),
            new("rationale", "توصية لجنة الاختيار وفق الدرجات")
        ]);
        await s["central-a"].PostAsync("22c-submit", $"ui/hr-core/SelectionCommittees/Submit/{session}", SessionDetails(session), [new("id", session)]);
        var submitted = await Sql(
            "SELECT (s.\"SubmittedAt\" IS NOT NULL)::text || '|' || (s.\"LockedAt\" IS NOT NULL)::text || '|' || d.\"Code\" FROM \"SelectionCommitteeSessions\" s " +
            $"JOIN \"WorkflowInstances\" i ON i.\"Id\"=s.\"WorkflowInstanceId\" JOIN \"WorkflowDefinitions\" d ON d.\"Id\"=i.\"DefinitionId\" WHERE s.\"Id\"='{session}';");
        Assert.Equal("true|true|RECRUITMENT-SELECTION-COMMITTEE", submitted);

        await ApproveCommitteeAsync(s["br1-mgr"], "22c-step-1", session);
        await ApproveCommitteeAsync(s["br1-mgr"], "22c-step-2", session);
        await ApproveCommitteeAsync(s["a-management-approver"], "22c-step-3", session);
        Assert.Equal("true", await Sql($"SELECT (\"ApprovedAt\" IS NOT NULL)::text FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{session}';"));

        await s.AuditAsync(shortSession, reqId, session, appId);
        s.Checks(new { shortSession, requisitionId = reqId, session, applicationId = appId, submitted, approved = true });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac22_d_appeal_decided_only_by_the_assigned_compliance_reviewer()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "22d", "AC-HCM-22d appeal: HR files → a-compliance assigned → decides; non-assigned reviewer refused",
            "central-a", "a-compliance", "a-compliance-2");
        var states = RecruitmentStates.Load();
        var seededAppeal = states.Appeal.Id.ToString("D");
        var compliance = AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id.ToString("D");
        var compliance2 = AcceptancePersonaCatalog.Get("a-compliance-2").Employee!.Id.ToString("D");
        s.Steps($"""
            Design PLAN-28: HR files the appeal on the candidate's behalf → assigns an independent compliance reviewer
            (by employee) → only that reviewer decides; the application follows the outcome.
            1. central-a enters an application on {states.Requisitions.Published.Number}, rejects it, files an appeal and assigns a-compliance.
            2. a-compliance decides «Upheld» → appeal Decided, application back to UnderReview.
            3. Negative: the seeded pending appeal {seededAppeal} (assigned to a-compliance): a-compliance-2 gets 404 on its page,
               and a Decide POST from a-compliance-2 is refused (status stays Pending). a-compliance-2's token page is a second
               appeal assigned to them, which they then dismiss (clean-up, also a positive for an assigned reviewer).
            """);

        Assert.Equal(AppealPending, await Scalar($"SELECT \"Status\" FROM \"RecruitmentAppeals\" WHERE \"Id\"='{seededAppeal}';"));
        var reqId = states.Requisitions.Published.Id.ToString("D");
        var app1 = await CreateApplicationAsync(s, "22d-1", reqId);
        var appeal1 = await RejectAndFileAsync(s, "22d-1", app1);
        await AssignAsync(s["central-a"], "22d-1-assign", appeal1, compliance);
        Assert.Equal($"{compliance}|InProgress", await Sql(
            "SELECT a.\"IndependentReviewerEmployeeId\"::text || '|' || i.\"Status\" FROM \"RecruitmentAppeals\" a " +
            $"JOIN \"WorkflowInstances\" i ON i.\"Id\"=a.\"WorkflowInstanceId\" WHERE a.\"Id\"='{appeal1}';"));
        s.Recorder.WriteBefore(new { seededAppeal, appeal1, application1 = app1 });

        await DecideAsync(s["a-compliance"], "22d-1-decide-upheld", appeal1, "Upheld");
        Assert.Equal($"{AppealDecided}|Upheld|{AppUnderReview}", await Sql(
            "SELECT a.\"Status\"::text || '|' || a.\"Outcome\" || '|' || p.\"Status\"::text FROM \"RecruitmentAppeals\" a " +
            $"JOIN \"RecruitmentApplications\" p ON p.\"Id\"=a.\"RecruitmentApplicationId\" WHERE a.\"Id\"='{appeal1}';"));

        var (status, _) = await s["a-compliance-2"].GetWithStatusAsync("22d-non-assigned-details", AppealDetails(seededAppeal));
        Assert.Equal(HttpStatusCode.NotFound, status);
        var app2 = await CreateApplicationAsync(s, "22d-2", reqId);
        var appeal2 = await RejectAndFileAsync(s, "22d-2", app2);
        await AssignAsync(s["central-a"], "22d-2-assign-compliance-2", appeal2, compliance2);
        await s["a-compliance-2"].PostAsync("22d-non-assigned-decides", "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appeal2),
            [new("id", seededAppeal), new("outcome", "Upheld"), new("rationale", Rationale)]);
        Assert.Equal($"{AppealPending}|", await Sql(
            $"SELECT \"Status\"::text || '|' || COALESCE(\"Outcome\",'') FROM \"RecruitmentAppeals\" WHERE \"Id\"='{seededAppeal}';"));
        await DecideAsync(s["a-compliance-2"], "22d-2-decide-dismissed", appeal2, "Dismissed");
        Assert.Equal($"{AppealDecided}|Dismissed|{AppRejected}", await Sql(
            "SELECT a.\"Status\"::text || '|' || a.\"Outcome\" || '|' || p.\"Status\"::text FROM \"RecruitmentAppeals\" a " +
            $"JOIN \"RecruitmentApplications\" p ON p.\"Id\"=a.\"RecruitmentApplicationId\" WHERE a.\"Id\"='{appeal2}';"));

        await s.AuditAsync(appeal1, appeal2, seededAppeal, app1, app2);
        s.Checks(new { appeal1, appeal2, seededAppeal, seededStillPending = true, expectedRefusal = "غير مصرح: يجب أن تكون المراجع المستقل المعيّن." });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac22_e_lab_scale_offer_submit_starts_the_offer_workflow()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "22e", "AC-HCM-22e lab scale: offer submit → PendingApproval + WorkflowInstanceId", Approvers);
        s.Steps("""
            Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
            1. central-a publishes a requisition on an AC33-POOL position; HR application ranked 1 (test-only SQL precondition).
            2. central-a creates and submits an offer → PendingApproval, WorkflowInstanceId set (RECRUITMENT-OFFER-APPROVAL, step 1),
               not Issued; the offer page shows no «لم يُنشأ مسار اعتماد» warning.
            3. Clean-up: br1-mgr rejects (offer back to Draft).
            The non-lab case (IsLabScale=false + policy Off → PendingApproval without workflow, never Issued) is proven by
            src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs (G-02, PLAN-25).
            """);

        var (_, appId) = await RankedApplicationAsync(s, "22e");
        var offerId = await CreateOfferAsync(s, "22e", appId);
        await s["central-a"].PostAsync("22e-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offerId), [new("id", offerId)]);
        var state = (await OfferStateAsync(offerId)).Split('|');
        Assert.Equal(OfferPending.ToString(CultureInfo.InvariantCulture), state[0]);
        Assert.True(Guid.TryParse(state[1], out _), "22e: lab-scale offer submit did not create a WorkflowInstanceId.");
        Assert.Equal("1", state[2]);
        Assert.Equal("RECRUITMENT-OFFER-APPROVAL", await WorkflowCodeAsync(state[1]));
        Assert.DoesNotContain("لم يُنشأ مسار اعتماد", await s["central-a"].GetAsync("22e-details", OfferDetails(offerId)), StringComparison.Ordinal);

        await s["br1-mgr"].PostAsync("22e-cleanup-reject", $"ui/hr-core/OfferLetters/RejectWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", "تنظيف بعد سيناريو قبول PLAN-33")]);

        await s.AuditAsync(offerId);
        s.Checks(new { offerId, workflowInstanceId = state[1], nonLab = "Hcm033LabScaleBehaviourTests (integration)" });
        s.Recorder.WriteResult("Pass");
    }

    /// <summary>Published pool requisition + HR application set to Ranked 1 by test-only SQL (ranking is AC-HCM-26's scope).</summary>
    private static async Task<(string Requisition, string Application)> RankedApplicationAsync(RecruitmentSession s, string step)
    {
        var reqId = await PublishRequisitionAsync(s, step);
        var appId = await CreateApplicationAsync(s, step, reqId);
        await Sql(
            $"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"=1, \"AssessmentScore\"=90, \"RankedAt\"=now(), " +
            $"\"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
        s.Recorder.WriteBefore(new { requisitionId = reqId, applicationId = appId, precondition = "Ranked 1 by test-only SQL" });
        return (reqId, appId);
    }

    private static async Task<string> CreateOfferAsync(RecruitmentSession s, string step, string appId)
    {
        var salary = await MidSalaryAsync(s);
        await s["central-a"].PostAsync($"{step}-create-offer", "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(appId), OfferForm(appId, salary));
        var offerId = await Sql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{appId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offerId, out _), $"{step}: offer was not created for application {appId}.");
        return offerId;
    }

    private static async Task<string> RejectAndFileAsync(RecruitmentSession s, string step, string appId)
    {
        await s["central-a"].PostAsync($"{step}-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(appId),
            [new("id", appId), new("status", "Rejected"), new("rejectionNote", "لا يستوفي شرط المؤهل (PLAN-33)")]);
        Assert.Equal(AppRejected, await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';"));
        await s["central-a"].PostAsync($"{step}-file", "ui/hr-core/RecruitmentApplications/FileAppeal", AppDetails(appId),
            [new("id", appId), new("reason", AppealReason)]);
        var appeal = await Sql($"SELECT \"Id\" FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{appId}' AND \"Status\"={AppealPending};");
        Assert.True(Guid.TryParse(appeal, out _), $"{step}: appeal was not filed.");
        return appeal;
    }

    private static Task AssignAsync(AcceptanceUi ui, string step, string appealId, string employeeId) =>
        ui.PostAsync(step, "ui/hr-core/RecruitmentAppeals/AssignReviewer", AppealDetails(appealId),
            [new("id", appealId), new("reviewerEmployeeId", employeeId)]);

    private static Task DecideAsync(AcceptanceUi ui, string step, string appealId, string outcome) =>
        ui.PostAsync(step, "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appealId),
            [new("id", appealId), new("outcome", outcome), new("rationale", Rationale)]);

    private static Task ApproveCommitteeAsync(AcceptanceUi ui, string step, string sessionId) =>
        ui.PostAsync(step, $"ui/hr-core/SelectionCommittees/ApproveWorkflow/{sessionId}", SessionDetails(sessionId),
            [new("SessionId", sessionId), new("Comments", $"PLAN-33 {step}")]);
}
