using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-28a..i (PLAN-28): the selection committee as an audited decision body that gates ranking (session 1), and
/// the single appeal path through an independent, employee-identified compliance reviewer (session 2).
/// Committee scenarios use the seeded committee-gated requisition (REQ-2026-A-CMT1, RIF branch); appeal scenarios
/// create their own requisition through the UI. Direct SQL is used only for evidence checks, for the documented
/// ranking precondition of the appeal requisitions (ranking is AC-HCM-26's scope) and for time travel (appeal window).
/// Flash (TempData) messages are not observable through the gateway, so negatives are asserted on database state.
/// </summary>
public class AcHcm28CommitteeAppealTests
{
    private const string Ac = "AC-HCM-28";
    private static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string CommitteeRequisition = AcceptanceSeedIds.CommitteeRequisitionId.ToString("D");
    private static readonly string CommitteeSession = AcceptanceSeedIds.CommitteeSessionId.ToString("D");
    private static readonly string[] BlockingStatuses = ["Draft", "PendingApproval", "Approved", "Published"];

    // RecruitmentApplicationStatus / OfferStatus / RecruitmentAppealStatus are stored as integers.
    private const int AppUnderReview = 1, AppRejected = 3, AppHired = 4, AppAssessed = 6, AppRanked = 7, AppOfferAccepted = 9;
    private const int OfferIssued = 2, OfferAccepted = 3;
    private const int AppealPending = 1, AppealDecided = 2;

    private const string Rationale = "ثبت للمراجع المستقل خطأ في احتساب درجة الخبرة وفق المعايير المعتمدة";
    private const string AppealReason = "يعترض المرشح على احتساب درجة الخبرة في التقييم النهائي";

    // ── AC-HCM-28a/c/d: committee on the seeded requisition ─────────────────────────────────────

    [SkippableFact]
    public async Task Ac28_a_c_d_committee_votes_decisions_three_step_approval_minutes_and_ranking_gate()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("28a-c-d", "AC-HCM-28a/c/d committee votes, COI, decisions, approval, minutes, ranking gate");
        Skip.If(await Sql($"SELECT COALESCE(\"SubmittedAt\"::text,'') FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{CommitteeSession}';") != "",
            "The seeded committee session was already submitted — run scripts/acceptance-reset.ps1 (seed is single-use).");
        WriteScenarioSteps(s.Recorder, """
            Personas: central-a (HR secretary), br1-mgr (steps 1–2), a-management-approver (step 3).
            Seed: REQ-2026-A-CMT1 (RequiresCommitteeApproval), 3 Assessed candidates, session with chair + 3 members, unsubmitted.
            1. AC-28c (negative): ranking a candidate before the committee is approved is refused (rank stays empty).
            2. AC-28d: a member declares a conflict with candidate 1 → their vote on that candidate is removed; their votes on the others stay.
            3. Every eligible member votes Agree; decisions: candidates 1 and 2 Recommend (ranks 1, 2), candidate 3 Reserve (rank 3).
            4. AC-28a: submit → session locked (SubmittedAt/LockedAt); adding a member after submit is refused.
            5. AC-28a: br1-mgr approves steps 1–2, a-management-approver step 3 → ApprovedAt; the minutes PDF downloads.
            6. AC-28c (negative): a rank different from the proposed rank without a reason is refused.
            7. AC-28c (positive): «تطبيق ترتيب اللجنة» assigns the proposed ranks 1..3.
            """);

        var details = $"ui/hr-core/SelectionCommittees/Details/{CommitteeSession}";
        var candidates = (await Sql(
                $"SELECT string_agg(\"Id\"::text, ',' ORDER BY \"AssessmentScore\" DESC) FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{CommitteeRequisition}';"))
            .Split(',');
        var members = (await Sql(
                $"SELECT string_agg(\"Id\"::text || ':' || \"EmployeeId\"::text, ',' ORDER BY \"Role\", \"Id\") FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{CommitteeSession}';"))
            .Split(',').Select(m => m.Split(':')).Select(p => (Member: p[0], Employee: p[1])).ToList();
        Assert.Equal(3, candidates.Length);
        Assert.Equal(4, members.Count);
        s.Recorder.WriteBefore(new { requisition = CommitteeRequisition, session = CommitteeSession, candidates, members });

        // AC-28c negative: no approved committee yet.
        await s.Central.PostAsync("28c-rank-before-approval", "ui/hr-core/RecruitmentApplications/AssignRank", AppDetails(candidates[0]),
            [new("ApplicationId", candidates[0]), new("Rank", "1")]);
        ExpectedFlash.Add("يتطلب الترتيب اعتماد لجنة الاختيار أولاً.");
        Assert.Equal("", await Sql($"SELECT COALESCE(\"Rank\"::text,'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{candidates[0]}';"));

        // Votes, then AC-28d: the last member declares a conflict with candidate 1.
        foreach (var app in candidates)
            foreach (var (member, _) in members)
                await s.Central.PostAsync($"vote-{member[..8]}-{app[..8]}", "ui/hr-core/SelectionCommittees/RecordVote", details,
                    [new("sessionId", CommitteeSession), new("memberId", member), new("applicationId", app), new("vote", "Agree")]);
        var conflicted = members[^1].Member;
        await s.Central.PostAsync("28d-coi", "ui/hr-core/SelectionCommittees/RecordCoi", details,
            [new("sessionId", CommitteeSession), new("memberId", conflicted), new("applicationId", candidates[0]), new("hasConflict", "true")]);
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeVotes\" WHERE \"MemberId\"='{conflicted}' AND \"ApplicationId\"='{candidates[0]}';"));
        Assert.Equal(2, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeVotes\" WHERE \"MemberId\"='{conflicted}';"));
        Assert.Equal(3, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeVotes\" WHERE \"ApplicationId\"='{candidates[0]}';"));

        string[] recommendations = ["Recommend", "Recommend", "Reserve"];
        for (var i = 0; i < candidates.Length; i++)
            await s.Central.PostAsync($"decision-{i + 1}", "ui/hr-core/SelectionCommittees/UpsertDecision", details,
            [
                new("sessionId", CommitteeSession), new("applicationId", candidates[i]), new("recommendation", recommendations[i]),
                new("proposedRank", (i + 1).ToString(CultureInfo.InvariantCulture)), new("rationale", "توصية لجنة الاختيار وفق الدرجات")
            ]);
        Assert.Equal(3, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeDecisions\" WHERE \"SessionId\"='{CommitteeSession}';"));

        // AC-28a: submit → lock; adding a member afterwards is refused.
        await s.Central.PostAsync("28a-submit", $"ui/hr-core/SelectionCommittees/Submit/{CommitteeSession}", details, [new("id", CommitteeSession)]);
        Assert.Equal("true|true", await Sql(
            $"SELECT (\"SubmittedAt\" IS NOT NULL)::text || '|' || (\"LockedAt\" IS NOT NULL)::text FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{CommitteeSession}';"));
        var outsider = await FreeEmployeeAsync(members.Select(m => m.Employee));
        await s.Central.PostAsync("28a-add-member-after-submit", "ui/hr-core/SelectionCommittees/AddMember", details,
            [new("sessionId", CommitteeSession), new("employeeId", outsider), new("role", "Member"), new("hasConflictOfInterest", "false")]);
        ExpectedFlash.Add("تم تقديم الجلسة للاعتماد — العضوية مقفلة.");
        Assert.Equal(4, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{CommitteeSession}';"));

        // AC-28a: three-step approval, then the minutes.
        await ApproveCommitteeAsync(s.Br1, "28a-step-1", CommitteeSession);
        await ApproveCommitteeAsync(s.Br1, "28a-step-2", CommitteeSession);
        await ApproveCommitteeAsync(s.Approver, "28a-step-3", CommitteeSession);
        Assert.Equal("true", await Sql($"SELECT (\"ApprovedAt\" IS NOT NULL)::text FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{CommitteeSession}';"));
        var (minutesStatus, minutesType, minutesBytes) = await s.Central.GetRawAsync("28a-minutes", $"ui/hr-core/SelectionCommittees/Minutes/{CommitteeSession}");
        Assert.Equal(HttpStatusCode.OK, minutesStatus);
        Assert.Equal("application/pdf", minutesType);
        Assert.True(minutesBytes > 1000, "Minutes PDF is unexpectedly small.");

        // AC-28c: rank ≠ proposed without a reason → refused; then the committee ranking is applied.
        await s.Central.PostAsync("28c-rank-mismatch", "ui/hr-core/RecruitmentApplications/AssignRank", AppDetails(candidates[0]),
            [new("ApplicationId", candidates[0]), new("Rank", "2")]);
        ExpectedFlash.Add("الترتيب مخالف للترتيب المقترح من اللجنة؛ يلزم سبب لا يقل عن 20 حرفاً.");
        Assert.Equal("", await Sql($"SELECT COALESCE(\"Rank\"::text,'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{candidates[0]}';"));

        await s.Central.PostAsync("28c-apply-committee-ranking", $"ui/hr-core/RecruitmentRequisitions/ApplyCommitteeRanking/{CommitteeRequisition}",
            $"ui/hr-core/RecruitmentRequisitions/Details/{CommitteeRequisition}", [new("id", CommitteeRequisition)]);
        Assert.Equal("1:7,2:7,3:7", await Sql(
            $"SELECT string_agg(\"Rank\"::text || ':' || \"Status\", ',' ORDER BY \"Rank\") FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{CommitteeRequisition}';"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{CommitteeRequisition}' AND \"Action\"='ApplyCommitteeRanking';"));

        await WriteDbChecksAsync(s, new
        {
            session = await Sql($"SELECT \"SubmittedAt\"::text || '|' || \"LockedAt\"::text || '|' || \"ApprovedAt\"::text FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{CommitteeSession}';"),
            votes = await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeVotes\" WHERE \"SessionId\"='{CommitteeSession}';"),
            ranks = await Sql($"SELECT string_agg(\"ApplicantFullName\" || ':' || \"Rank\", ',' ORDER BY \"Rank\") FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{CommitteeRequisition}';")
        });
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-28b: duplicate member, creator cannot approve ────────────────────────────────────

    [SkippableFact]
    public async Task Ac28_b_duplicate_member_and_session_creator_approval_are_refused()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("28b", "AC-HCM-28b duplicate member, creator self-approval");
        WriteScenarioSteps(s.Recorder, """
            Persona: central-a creates a new session on REQ-2026-A-CMT1 (CreatedBy = central-a) and acts as HR secretary.
            1. Chair + 2 members are added from the employee picker; adding the same employee again is refused.
            2. Every member votes Agree on every Assessed/Ranked candidate; each gets a Recommend/Reserve decision; submit.
            3. AC-28b (negative): central-a, the session creator, approves step 1 → refused (creator never approves); the step stays 1.
            """);

        var candidates = (await Sql(
                $"SELECT COALESCE(string_agg(\"Id\"::text, ',' ORDER BY \"AssessmentScore\" DESC), '') FROM \"RecruitmentApplications\" " +
                $"WHERE \"RequisitionId\"='{CommitteeRequisition}' AND \"Status\" IN ({AppAssessed},{AppRanked});"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Skip.If(candidates.Length == 0, "No Assessed/Ranked candidate left on REQ-2026-A-CMT1 — run scripts/acceptance-reset.ps1.");

        await s.Central.PostAsync("28b-create-session", "ui/hr-core/SelectionCommittees/CreateSession",
            $"ui/hr-core/SelectionCommittees?requisitionId={CommitteeRequisition}",
            [new("requisitionId", CommitteeRequisition), new("committeeDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)), new("purpose", "Ranking")]);
        var session = await Sql(
            $"SELECT \"Id\" FROM \"SelectionCommitteeSessions\" WHERE \"RequisitionId\"='{CommitteeRequisition}' AND \"SubmittedAt\" IS NULL AND \"CreatedBy\" IS NOT NULL ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(session, out _), "Session was not created.");
        var details = $"ui/hr-core/SelectionCommittees/Details/{session}";

        var employees = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var employee = await FreeEmployeeAsync(employees);
            employees.Add(employee);
            await s.Central.PostAsync($"28b-add-member-{i + 1}", "ui/hr-core/SelectionCommittees/AddMember", details,
                [new("sessionId", session), new("employeeId", employee), new("role", i == 0 ? "Chair" : "Member"), new("hasConflictOfInterest", "false")]);
        }

        await s.Central.PostAsync("28b-duplicate-member", "ui/hr-core/SelectionCommittees/AddMember", details,
            [new("sessionId", session), new("employeeId", employees[1]), new("role", "Member"), new("hasConflictOfInterest", "false")]);
        ExpectedFlash.Add("الموظف عضو في هذه الجلسة مسبقاً.");
        Assert.Equal(3, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{session}';"));

        var memberIds = (await Sql($"SELECT string_agg(\"Id\"::text, ',') FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{session}';")).Split(',');
        for (var i = 0; i < candidates.Length; i++)
        {
            foreach (var member in memberIds)
                await s.Central.PostAsync($"28b-vote-{i + 1}-{member[..8]}", "ui/hr-core/SelectionCommittees/RecordVote", details,
                    [new("sessionId", session), new("memberId", member), new("applicationId", candidates[i]), new("vote", "Agree")]);
            await s.Central.PostAsync($"28b-decision-{i + 1}", "ui/hr-core/SelectionCommittees/UpsertDecision", details,
            [
                new("sessionId", session), new("applicationId", candidates[i]), new("recommendation", i == 0 ? "Recommend" : "Reserve"),
                new("proposedRank", (i + 1).ToString(CultureInfo.InvariantCulture)), new("rationale", "AC-HCM-28b")
            ]);
        }

        await s.Central.PostAsync("28b-submit", $"ui/hr-core/SelectionCommittees/Submit/{session}", details, [new("id", session)]);
        var instance = await Sql($"SELECT COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{session}';");
        Assert.True(Guid.TryParse(instance, out _), "Submitting the session did not start the committee workflow.");

        await ApproveCommitteeAsync(s.Central, "28b-creator-approves", session);
        ExpectedFlash.Add("لا يجوز لعضو في اللجنة أو منشئها اعتماد الجلسة.");
        Assert.Equal(1, await Scalar($"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance}';"));
        Assert.Equal("false", await Sql($"SELECT (\"ApprovedAt\" IS NOT NULL)::text FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{session}';"));

        await WriteDbChecksAsync(s, new { session, instance, members = memberIds.Length });
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-28e/f/g/i: the appeal path ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ac28_e_f_g_i_single_appeal_path_with_independent_compliance_reviewer()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("28e-f-g-i", "AC-HCM-28e/f/g/i reject → appeal → independent reviewer → explicit outcomes");
        Skip.If(s.Compliance2 is null, s.Compliance2Missing);
        WriteScenarioSteps(s.Recorder, """
            Personas: central-a (HR: rejects, files, assigns), a-compliance (assigned reviewer), a-compliance-2 (not assigned).
            1. A requisition is created, approved and published through the UI; three HR applications; test-only precondition: ranks 1..3.
               A committee session is created on it with one member (employee E).
            2. AC-28e: central-a rejects rank 3 with a note → RejectionKind SelectionDecision, RejectedAt; files an appeal on the candidate's
               behalf (no reviewer); assigning E (committee member) is refused; assigning a-compliance's employee starts RECRUITMENT-APPEAL-REVIEW.
            3. AC-28e: a-compliance opens /ui/hr-core/RecruitmentAppeals and the work inbox (compliance-only access) but not /ui/hr-core/Employees;
               a-compliance-2 gets 404 on appeal 1. Rank 2 is rejected and appealed (appeal 2) and assigned to a-compliance-2, who then
               tries to decide appeal 1 → refused. a-compliance decides appeal 1 «Upheld» → UnderReview, appeal Decided, rejection cleared.
            4. AC-28f: appeal 2 is reassigned to a-compliance (the running review is cancelled); a-compliance asks for information;
               central-a resubmits; a-compliance decides «Dismissed» → stays Rejected with the original kind/date and the rationale stored.
            5. AC-28g (negative): a new application rejected 15 days ago (test-only time travel on RejectedAt) cannot be appealed.
            6. AC-28i (negative): the legacy «استئناف (مسار قديم)» card is gone and POST RecruitmentApplications/ReopenAppeal → 404.
            """);

        var req = await CreateRankedRequisitionAsync(s, rankedCount: 3);
        var complianceEmployee = AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id.ToString("D");
        var compliance2Employee = AcceptancePersonaCatalog.Get("a-compliance-2").Employee!.Id.ToString("D");

        // Committee member E on this requisition (for the independence negative).
        await s.Central.PostAsync("28e-create-session", "ui/hr-core/SelectionCommittees/CreateSession",
            $"ui/hr-core/SelectionCommittees?requisitionId={req.Id}",
            [new("requisitionId", req.Id), new("committeeDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)), new("purpose", "Ranking")]);
        var session = await Sql($"SELECT \"Id\" FROM \"SelectionCommitteeSessions\" WHERE \"RequisitionId\"='{req.Id}' LIMIT 1;");
        var memberEmployee = await FreeEmployeeAsync([complianceEmployee, compliance2Employee]);
        await s.Central.PostAsync("28e-add-member", "ui/hr-core/SelectionCommittees/AddMember", $"ui/hr-core/SelectionCommittees/Details/{session}",
            [new("sessionId", session), new("employeeId", memberEmployee), new("role", "Chair"), new("hasConflictOfInterest", "false")]);
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{session}';"));

        // AC-28e: reject → file (no reviewer) → independence negative → assign a-compliance.
        var appeal1 = await RejectAndFileAsync(s, "28e", req.Ranked[2], "لم يحقق الحد الأدنى في المقابلة");
        Assert.Equal("SelectionDecision|true|لم يحقق الحد الأدنى في المقابلة", await Sql(
            $"SELECT \"RejectionKind\" || '|' || (\"RejectedAt\" IS NOT NULL)::text || '|' || \"RejectionNote\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[2]}';"));
        Assert.Equal("|", await Sql($"SELECT COALESCE(\"IndependentReviewerEmployeeId\"::text,'') || '|' || COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal1}';"));

        await AssignAsync(s.Central, "28e-assign-committee-member", appeal1, memberEmployee);
        ExpectedFlash.Add("يجب أن يكون المراجع مستقلاً عن اللجنة والمقابلة والتقييم.");
        Assert.Equal("", await Sql($"SELECT COALESCE(\"IndependentReviewerEmployeeId\"::text,'') FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal1}';"));

        await AssignAsync(s.Central, "28e-assign-compliance", appeal1, complianceEmployee);
        Assert.Equal(complianceEmployee, await Sql($"SELECT \"IndependentReviewerEmployeeId\"::text FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal1}';"));
        Assert.Equal("RECRUITMENT-APPEAL-REVIEW|InProgress", await Sql(
            "SELECT d.\"Code\" || '|' || i.\"Status\" FROM \"RecruitmentAppeals\" a JOIN \"WorkflowInstances\" i ON i.\"Id\"=a.\"WorkflowInstanceId\" " +
            $"JOIN \"WorkflowDefinitions\" d ON d.\"Id\"=i.\"DefinitionId\" WHERE a.\"Id\"='{appeal1}';"));

        // Compliance-only access (PLAN-25 slice).
        var list = await s.Compliance.GetAsync("28e-compliance-appeals", "ui/hr-core/RecruitmentAppeals");
        Assert.Contains($"RecruitmentAppeals/Details/{appeal1}", list);
        var inbox = await s.Compliance.GetAsync("28e-compliance-inbox", "ui/hr-core/WorkInbox");
        Assert.Contains($"RecruitmentAppeals/Details/{appeal1}", inbox);
        Assert.Equal(HttpStatusCode.Forbidden, await s.Compliance.TryGetStatusAsync("ui/hr-core/Employees"));
        Assert.Equal(HttpStatusCode.NotFound, await s.Compliance2!.TryGetStatusAsync($"ui/hr-core/RecruitmentAppeals/Details/{appeal1}"));

        // A second appeal is assigned to a-compliance-2; from that page they try to decide appeal 1 → refused server-side.
        var appeal2 = await RejectAndFileAsync(s, "28f", req.Ranked[1], "اكتفاء بمرشح أعلى درجة");
        var rejectedAt = await Sql($"SELECT \"RejectedAt\"::text FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[1]}';");
        await AssignAsync(s.Central, "28f-assign-compliance-2", appeal2, compliance2Employee);
        var firstInstance = await Sql($"SELECT \"WorkflowInstanceId\"::text FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal2}';");
        await s.Compliance2.PostAsync("28e-non-assigned-decides", "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appeal2),
            [new("id", appeal1), new("outcome", "Upheld"), new("rationale", Rationale)]);
        ExpectedFlash.Add("غير مصرح: يجب أن تكون المراجع المستقل المعيّن.");
        Assert.Equal(AppealPending, await Scalar($"SELECT \"Status\" FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal1}';"));

        await DecideAsync(s.Compliance, "28e-upheld", appeal1, "Upheld");
        Assert.Equal($"{AppealDecided}|Upheld|{Rationale}", await Sql(
            $"SELECT \"Status\"::text || '|' || \"Outcome\" || '|' || \"Decision\" FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal1}';"));
        Assert.Equal($"{AppUnderReview}|Decided||", await Sql(
            $"SELECT \"Status\"::text || '|' || \"AppealStatus\" || '|' || COALESCE(\"RejectionKind\",'') || '|' || COALESCE(\"RejectedAt\"::text,'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[2]}';"));
        var actor = await Sql($"SELECT \"UserId\"::text FROM \"AuditLogs\" WHERE \"EntityId\"='{appeal1}' AND \"Action\"='AppealDecided';");
        Assert.True(Guid.TryParse(actor, out _), "AppealDecided audit has no actor.");

        // AC-28f: appeal 2 is reassigned to a-compliance (the running review is cancelled) → info request → resubmit → Dismissed.
        await AssignAsync(s.Central, "28f-reassign", appeal2, complianceEmployee);
        Assert.Equal("Cancelled", await Sql($"SELECT \"Status\" FROM \"WorkflowInstances\" WHERE \"Id\"='{firstInstance}';"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{appeal2}' AND \"Action\"='AppealReviewerReassigned';"));
        var appeal2Page = AppealDetails(appeal2);
        await s.Compliance.PostAsync("28f-request-info", "ui/hr-core/RecruitmentAppeals/RequestInfo", appeal2Page,
            [new("id", appeal2), new("infoRequest", "يرجى إرفاق محضر المقابلة الموقّع")]);
        Assert.Equal("يرجى إرفاق محضر المقابلة الموقّع|", await Sql(
            $"SELECT \"InfoRequest\" || '|' || COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal2}';"));
        await s.Central.PostAsync("28f-resubmit", "ui/hr-core/RecruitmentAppeals/Resubmit", appeal2Page,
            [new("id", appeal2), new("additionalInfo", "أُرفق محضر المقابلة الموقّع من اللجنة")]);
        Assert.Equal("InProgress", await Sql(
            $"SELECT i.\"Status\" FROM \"RecruitmentAppeals\" a JOIN \"WorkflowInstances\" i ON i.\"Id\"=a.\"WorkflowInstanceId\" WHERE a.\"Id\"='{appeal2}';"));
        await DecideAsync(s.Compliance, "28f-dismissed", appeal2, "Dismissed");
        Assert.Equal($"{AppealDecided}|Dismissed", await Sql($"SELECT \"Status\"::text || '|' || \"Outcome\" FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal2}';"));
        Assert.Equal($"{AppRejected}|SelectionDecision|{rejectedAt}", await Sql(
            $"SELECT \"Status\"::text || '|' || \"RejectionKind\" || '|' || \"RejectedAt\"::text FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[1]}';"));

        // AC-28g: appeal window from RejectedAt.
        var (late, _) = await CreateApplicationAsync(s.Central, req.Id, "متأخر");
        await s.Central.PostAsync("28g-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(late),
            [new("id", late), new("status", "Rejected"), new("rejectionNote", "AC-HCM-28g")]);
        await Sql($"UPDATE \"RecruitmentApplications\" SET \"RejectedAt\" = now() - interval '15 days' WHERE \"Id\"='{late}';");
        await s.Central.PostAsync("28g-file-late", "ui/hr-core/RecruitmentApplications/FileAppeal", AppDetails(late),
            [new("id", late), new("reason", AppealReason)]);
        ExpectedFlash.Add("انقضت مهلة الاستئناف على هذا الرفض.");
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{late}';"));

        // AC-28i: no legacy path.
        var rejectedPage = await s.Central.GetAsync("28i-details-no-legacy-card", AppDetails(late));
        Assert.DoesNotContain("مسار قديم", rejectedPage);
        Assert.DoesNotContain("ReopenAppeal", rejectedPage);
        Assert.Equal(HttpStatusCode.NotFound, await s.Central.PostRawStatusAsync("28i-post-reopen", "ui/hr-core/RecruitmentApplications/ReopenAppeal",
            [new("id", late), new("appealNotes", "x")]));

        await WriteDbChecksAsync(s, new
        {
            requisition = req.Id,
            appeals = await Sql(
                "SELECT string_agg(a.\"Outcome\" || ':' || a.\"Status\" || ':' || COALESCE(a.\"IndependentReviewerEmployeeId\"::text,'-'), ',' ORDER BY a.\"CreatedAt\") " +
                $"FROM \"RecruitmentAppeals\" a JOIN \"RecruitmentApplications\" p ON p.\"Id\"=a.\"RecruitmentApplicationId\" WHERE p.\"RequisitionId\"='{req.Id}';"),
            applications = await Sql(
                $"SELECT string_agg(\"ApplicantFullName\" || ':' || \"Status\" || ':' || \"AppealStatus\" || ':' || COALESCE(\"RejectionKind\",'-'), ',') FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{req.Id}';"),
            audit = await Sql(
                $"SELECT string_agg(\"Action\", ',' ORDER BY \"CreatedAt\") FROM \"AuditLogs\" WHERE \"EntityId\" IN ('{appeal1}','{appeal2}');")
        });
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-28h: hire blocked while an appeal is pending ─────────────────────────────────────

    [SkippableFact]
    public async Task Ac28_h_hire_is_blocked_while_an_appeal_on_the_requisition_is_pending()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("28h", "AC-HCM-28h no hire while an appeal is pending");
        WriteScenarioSteps(s.Recorder, """
            Personas: central-a (maker), br1-mgr + a-management-approver (offer approval), a-compliance (reviewer).
            1. Published requisition with ranks 1 and 2 (test-only ranking precondition).
            2. Rank 2 is rejected and appealed (pending). Offers stay allowed: rank 1's offer is issued (two-step workflow) and accepted.
            3. AC-28h (negative): central-a submits the Hire form for rank 1 → refused; the application stays OfferAccepted.
            4. a-compliance (assigned) dismisses the appeal → the same Hire form now hires rank 1.
            """);

        var req = await CreateRankedRequisitionAsync(s, rankedCount: 2);
        var bounds = await RequireGradeBoundsAsync(s, req);
        var appeal = await RejectAndFileAsync(s, "28h", req.Ranked[1], "اختيار المرشح الأعلى رتبة");
        await AssignAsync(s.Central, "28h-assign", appeal, AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id.ToString("D"));

        await s.Central.PostAsync("28h-offer", "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(req.Ranked[0]),
        [
            new("ApplicationId", req.Ranked[0]), new("OfferedSalary", bounds.Mid.ToString(CultureInfo.InvariantCulture)), new("GradeCode", req.Grade),
            new("ProposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), new("ValidityDays", "14")
        ]);
        var offer = await Sql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{req.Ranked[0]}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offer, out _), "Offer was not created while an appeal was pending.");
        var offerPage = $"ui/hr-core/OfferLetters/Details/{offer}";
        await s.Central.PostAsync("28h-submit-offer", "ui/hr-core/OfferLetters/SubmitOffer", offerPage, [new("id", offer)]);
        foreach (var (ui, step) in new[] { (s.Br1, "28h-offer-1"), (s.Approver, "28h-offer-2") })
            await ui.PostAsync(step, $"ui/hr-core/OfferLetters/ApproveWorkflow/{offer}", offerPage, [new("OfferId", offer), new("Comments", $"AC-HCM-28 {step}")]);
        Assert.Equal(OfferIssued, await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{offer}';"));
        await s.Central.PostAsync("28h-accept", "ui/hr-core/OfferLetters/AcceptOffer", offerPage, OfferOnBehalfEvidence.AcceptFields(offer), withEvidence: true);
        Assert.Equal(OfferAccepted, await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{offer}';"));

        var hireForm = HireForm(req);
        await s.Central.PostAsync("28h-hire-blocked", "ui/hr-core/RecruitmentApplications/Hire", AppDetails(req.Ranked[0]), hireForm);
        ExpectedFlash.Add("يوجد استئناف معلق على هذا الشاغر — يجب البت فيه قبل التعيين.");
        Assert.Equal(AppOfferAccepted, await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[0]}';"));

        await DecideAsync(s.Compliance, "28h-dismiss", appeal, "Dismissed");
        await s.Central.PostAsync("28h-hire", "ui/hr-core/RecruitmentApplications/Hire", $"ui/hr-core/RecruitmentApplications/Hire/{req.Ranked[0]}", hireForm);
        Assert.Equal(AppHired, await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[0]}';"));

        await WriteDbChecksAsync(s, new
        {
            requisition = req.Id,
            offer,
            appeal = await Sql($"SELECT \"Status\"::text || ':' || \"Outcome\" FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal}';"),
            applications = await Sql($"SELECT string_agg(COALESCE(\"Rank\"::text,'-') || ':' || \"Status\", ',' ORDER BY \"Rank\") FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{req.Id}';")
        });
        s.Recorder.WriteResult("Pass");
    }

    // ── Building blocks ─────────────────────────────────────────────────────────────────────────

    private sealed record Session(EvidenceRecorder Recorder, Ui Central, Ui Br1, Ui Approver, Ui Compliance, Ui? Compliance2, string Compliance2Missing);

    private sealed record RankedRequisition(string Id, string Grade, string OrgUnitId, IReadOnlyList<string> Ranked);

    private sealed record GradeBounds(decimal Min, decimal Max)
    {
        public decimal Mid => Math.Round((Min + Max) / 2m, 0);
    }

    private static async Task<Session> StartAsync(string tag, string scenario)
    {
        var central = AcceptanceHarness.RequirePersona("central-a");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var approver = AcceptanceHarness.RequirePersona("a-management-approver");
        var compliance = AcceptanceHarness.RequirePersona("a-compliance");
        var compliance2 = AcceptanceHarness.LoadPersonas().FirstOrDefault(p => p.Id == "a-compliance-2" && p.Enabled);
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        ScenarioTags.AddOrUpdate(recorder, tag);
        ExpectedFlash.Clear();
        recorder.WriteMetadata(scenario, $"{central.Id}+{br1.Id}+{approver.Id}+{compliance.Id}+{compliance2?.Id ?? "-"}");

        Ui? second = null;
        var missing = "Persona a-compliance-2 is missing from personas.json (run scripts/prepare-keycloak-import.ps1).";
        if (compliance2 is not null)
        {
            try
            {
                second = new Ui(await CreateClientAsync(compliance2, correlationId), recorder, compliance2.Id, tag);
            }
            catch (Exception ex)
            {
                missing = "Persona a-compliance-2 cannot sign in — re-sync Keycloak (scripts/sync-keycloak-dev-realm.ps1): " + ex.Message;
            }
        }

        return new Session(
            recorder,
            new Ui(await CreateClientAsync(central, correlationId), recorder, central.Id, tag),
            new Ui(await CreateClientAsync(br1, correlationId), recorder, br1.Id, tag),
            new Ui(await CreateClientAsync(approver, correlationId), recorder, approver.Id, tag),
            new Ui(await CreateClientAsync(compliance, correlationId), recorder, compliance.Id, tag),
            second,
            missing);
    }

    private static async Task<HttpClient> CreateClientAsync(HRManagement.E2E.E2ePersona persona, string correlationId)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona, correlationId);
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(HRManagement.E2E.E2eEnv.BaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        return http;
    }

    /// <summary>UI: requisition on a free graded BR1 position → approval → criteria → publish; HR applications ranked by test-only SQL.</summary>
    private static async Task<RankedRequisition> CreateRankedRequisitionAsync(Session s, int rankedCount)
    {
        var positionId = await FindFreeGradedBr1PositionAsync(s.Central);
        Skip.If(positionId is null, "No graded vacant BR1 funded position without a blocking requisition is left in agency A.");
        var row = (await Sql($"SELECT COALESCE(\"GradeCode\",'') || '|' || \"OrgUnitId\" FROM \"FundedPositions\" WHERE \"Id\"='{positionId}';")).Split('|');

        await s.Central.PostAsync("create-requisition", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", Br1Scope), new("FundedPositionId", positionId!), new("JobTitle", "وظيفة AC-HCM-28"),
            new("JustificationNotes", "PLAN-28 acceptance"), new("LegalSourceId", Syr001LegislationIds.Law50_2004),
            new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)), new("PublishingChannel", "PublicOnly")
        ]);
        var reqId = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(reqId, out _), $"Requisition was not created on position {positionId}.");
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";
        await s.Central.PostAsync("submit-requisition", $"ui/hr-core/RecruitmentRequisitions/Submit/{reqId}", details, []);
        await s.Br1.PostAsync("approve-requisition-1", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-28 step 1")]);
        await s.Approver.PostAsync("approve-requisition-2", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-28 step 2")]);
        await s.Central.PostAsync("criteria", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details, CriteriaForm(reqId));
        await s.Central.PostAsync("publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));

        var ranked = new List<string>();
        for (var rank = 1; rank <= rankedCount; rank++)
        {
            var (appId, _) = await CreateApplicationAsync(s.Central, reqId, $"رتبة{rank}");
            await Sql(
                $"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"={rank}, \"AssessmentScore\"={95 - rank * 5}, " +
                $"\"RankedAt\"=now(), \"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
            ranked.Add(appId);
        }

        s.Recorder.WriteBefore(new { requisitionId = reqId, positionId, grade = row[0], ranked });
        return new RankedRequisition(reqId, row[0], row.Length > 1 ? row[1] : "", ranked);
    }

    private static async Task<string> RejectAndFileAsync(Session s, string step, string appId, string note)
    {
        await s.Central.PostAsync($"{step}-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(appId),
            [new("id", appId), new("status", "Rejected"), new("rejectionNote", note)]);
        Assert.Equal(AppRejected, await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';"));
        await s.Central.PostAsync($"{step}-file", "ui/hr-core/RecruitmentApplications/FileAppeal", AppDetails(appId),
            [new("id", appId), new("reason", AppealReason)]);
        var appeal = await Sql($"SELECT \"Id\" FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{appId}' AND \"Status\"={AppealPending};");
        Assert.True(Guid.TryParse(appeal, out _), $"{step}: appeal was not filed.");
        Assert.Equal("HrOnBehalf", await Sql($"SELECT \"FiledBy\" FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal}';"));
        return appeal;
    }

    private static Task AssignAsync(Ui ui, string step, string appealId, string employeeId) =>
        ui.PostAsync(step, "ui/hr-core/RecruitmentAppeals/AssignReviewer", AppealDetails(appealId),
            [new("id", appealId), new("reviewerEmployeeId", employeeId)]);

    private static Task DecideAsync(Ui ui, string step, string appealId, string outcome) =>
        ui.PostAsync(step, "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appealId),
            [new("id", appealId), new("outcome", outcome), new("rationale", Rationale)]);

    private static Task ApproveCommitteeAsync(Ui ui, string step, string sessionId) =>
        ui.PostAsync(step, $"ui/hr-core/SelectionCommittees/ApproveWorkflow/{sessionId}", $"ui/hr-core/SelectionCommittees/Details/{sessionId}",
            [new("SessionId", sessionId), new("Comments", $"AC-HCM-28 {step}")]);

    /// <summary>An active agency-A employee not linked to a persona and not in <paramref name="exclude"/>.</summary>
    private static async Task<string> FreeEmployeeAsync(IEnumerable<string> exclude)
    {
        var excluded = AcceptancePersonaCatalog.Personas.Where(p => p.Employee is not null).Select(p => p.Employee!.Id.ToString("D"))
            .Concat(exclude)
            .Concat(await AllCommitteeEmployeesAsync())
            .Select(id => $"'{id}'");
        var id = await Sql(
            $"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{AcceptanceSeedIds.AgencyAId}' AND \"CurrentStatus\"=0 " +
            $"AND \"Id\" NOT IN ({string.Join(",", excluded)}) ORDER BY \"Id\" LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), "No free active employee found.");
        return id;
    }

    private static async Task<IEnumerable<string>> AllCommitteeEmployeesAsync() =>
        (await Sql("SELECT COALESCE(string_agg(DISTINCT \"EmployeeId\"::text, ','), '') FROM \"SelectionCommitteeMembers\";"))
        .Split(',', StringSplitOptions.RemoveEmptyEntries);

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

    private static async Task<(string Id, string Name)> CreateApplicationAsync(Ui central, string reqId, string label)
    {
        var name = $"AC28 {label} {Guid.NewGuid():N}"[..24];
        await central.PostAsync("create-application", "ui/hr-core/RecruitmentApplications/Create", "ui/hr-core/RecruitmentApplications/Index",
            [new("ApplicantFullName", name), new("RequisitionId", reqId), new("Notes", "AC-HCM-28")]);
        var appId = await Sql($"SELECT \"Id\" FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{reqId}' AND \"ApplicantFullName\"='{name}';");
        Assert.True(Guid.TryParse(appId, out _), "HR application was not created.");
        return (appId, name);
    }

    /// <summary>Payroll bounds of the position grade; skips when HR.Core cannot reach Payroll (S2S precondition, see AC-HCM-27).</summary>
    private static async Task<GradeBounds> RequireGradeBoundsAsync(Session s, RankedRequisition req)
    {
        var page = await s.Central.GetAsync("payroll-grade-hint", AppDetails(req.Ranked[0]));
        Skip.If(page.Contains("تعذّر التحقق من حدود الدرجة", StringComparison.Ordinal),
            "HR.Core cannot reach the Payroll grade lookup — see PLAN-27-EXECUTION (S2S secret, payroll.grades.read).");
        var row = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            $"SELECT g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g JOIN \"SalaryScales\" sc ON sc.\"Id\"=g.\"SalaryScaleId\" " +
            $"WHERE g.\"TenantId\"='{AcceptanceSeedIds.AgencyAId}' AND g.\"GradeCode\"='{req.Grade}' AND sc.\"IsActive\" AND sc.\"EffectiveFrom\" <= now() " +
            "AND (sc.\"EffectiveTo\" IS NULL OR sc.\"EffectiveTo\" >= now()) ORDER BY sc.\"EffectiveFrom\" DESC LIMIT 1;");
        var parts = row.Split('|');
        Assert.True(parts.Length == 2, $"Payroll grade {req.Grade} not found for agency A.");
        return new GradeBounds(decimal.Parse(parts[0], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static List<KeyValuePair<string, string>> HireForm(RankedRequisition req) =>
    [
        new("ApplicationId", req.Ranked[0]),
        new("Mode", "CreateNew"),
        new("NationalId", "02" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture)),
        new("FirstName", "مرشح"),
        new("LastName", "الاستئناف"),
        new("BirthDate", DateTime.UtcNow.Date.AddYears(-28).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("Gender", "Male"),
        new("PrimaryOrgUnitId", req.OrgUnitId),
        new("ExpectedStartDate", DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("EmploymentCategory", "PERMANENT")
    ];

    private static List<KeyValuePair<string, string>> CriteriaForm(string reqId)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", reqId), new("Title", "AC-HCM-28") };
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

    private static string AppDetails(string appId) => $"ui/hr-core/RecruitmentApplications/Details/{appId}";

    private static string AppealDetails(string appealId) => $"ui/hr-core/RecruitmentAppeals/Details/{appealId}";

    private static async Task WriteDbChecksAsync(Session s, object checks)
    {
        s.Recorder.WriteAfter(checks);
        s.Recorder.WriteDbChecks(new { checks, expectedRejections = ExpectedFlash });
        s.Recorder.FlushTiming();
        await Task.CompletedTask;
    }

    /// <summary>Each scenario keeps its own steps file; steps.md concatenates them (scenarios share the AC-HCM-28 folder).</summary>
    private static void WriteScenarioSteps(EvidenceRecorder recorder, string markdown)
    {
        var scenario = ScenarioTags.TryGetValue(recorder, out var t) ? t : "scenario";
        File.WriteAllText(Path.Combine(recorder.Folder, $"steps-{scenario}.md"), markdown);
        var combined = Directory.GetFiles(recorder.Folder, "steps-*.md")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"## {Path.GetFileNameWithoutExtension(f)[6..]}\n\n{File.ReadAllText(f)}");
        recorder.WriteSteps(string.Join("\n\n", combined));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EvidenceRecorder, string> ScenarioTags = new();

    /// <summary>Expected rejection messages (or their constant names), written to db-checks.json.</summary>
    private static readonly List<string> ExpectedFlash = [];

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);

    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);

    /// <summary>Gateway UI client for one persona: antiforgery-aware form posts with evidence capture (as in AC-HCM-26/27).</summary>
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
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode, Snippet(body));
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} as {persona} returned {(int)resp.StatusCode}.");
            return WebUtility.HtmlDecode(body);
        }

        public async Task<(HttpStatusCode Status, string? ContentType, long Length)> GetRawAsync(string step, string path)
        {
            using var resp = await SendAsync(HttpMethod.Get, path);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var type = resp.Content.Headers.ContentType?.MediaType;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode, new { contentType = type, bytes = bytes.Length });
            return (resp.StatusCode, type, bytes.Length);
        }

        public async Task<HttpStatusCode?> TryGetStatusAsync(string path)
        {
            try
            {
                using var resp = await SendAsync(HttpMethod.Get, path);
                recorder.CaptureHttp($"{tag}-status-{persona}", "GET", "/" + path, (int)resp.StatusCode, null);
                return resp.StatusCode;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>POST without following redirects or an antiforgery token — for "route does not exist" checks.</summary>
        public async Task<HttpStatusCode> PostRawStatusAsync(string step, string path, List<KeyValuePair<string, string>> fields)
        {
            using var resp = await SendAsync(HttpMethod.Post, path, new FormUrlEncodedContent(fields));
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "POST", "/" + path, (int)resp.StatusCode, null);
            return resp.StatusCode;
        }

        public async Task<string> PostAsync(string step, string path, string tokenPage, List<KeyValuePair<string, string>> fields, bool withEvidence = false)
        {
            var page = await FollowRedirectsAsync(await SendAsync(HttpMethod.Get, tokenPage));
            var pageHtml = await page.Content.ReadAsStringAsync();
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {tokenPage} as {persona} returned {(int)page.StatusCode}.");
            var token = Regex.Match(pageHtml, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""", RegexOptions.IgnoreCase);
            Assert.True(token.Success, $"No antiforgery token on {tokenPage}.");

            var form = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)) };
            var posted = await SendAsync(HttpMethod.Post, path, OfferOnBehalfEvidence.Content(form, withEvidence));
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

        private static string Snippet(string body) => body.Length > 600 ? body[..600] : body;
    }
}
