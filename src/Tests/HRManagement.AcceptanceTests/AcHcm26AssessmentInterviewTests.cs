using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-26a..i (PLAN-26): assessment criteria, structured scoring, interview lifecycle,
/// requisition integrity (duplicate guard, cancel cascade, freeze cascade) and the intake window.
/// Every scenario creates its own requisition through the UI (create → submit → two-step approval)
/// on a free vacant position under BR1, so runs do not depend on mutable seeded pipelines.
/// Direct SQL is used only for evidence checks and for two documented test-only preconditions:
/// moving an interview/closing date into the past (time travel) and inserting an Issued offer (AC-HCM-26h).
/// </summary>
public class AcHcm26AssessmentInterviewTests
{
    private const string Ac = "AC-HCM-26";
    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string PanelEmployee = AcceptanceSeedIds.EmpBr1Id.ToString("D");

    private static readonly string[] BlockingStatuses = ["Draft", "PendingApproval", "Approved", "Published"];

    // Application statuses are stored as integers.
    private const int AppSubmitted = 0, AppRejected = 3, AppInterviewScheduled = 5, AppAssessed = 6, AppRanked = 7;

    [SkippableFact]
    public async Task Ac26_a_b_c_d_e_full_selection_pipeline_without_seed_data()
    {
        AcceptanceEnv.RequireEnabled();
        var (recorder, central, br1, approver) = await StartAsync("26a-e", "AC-HCM-26a-e selection pipeline");
        WriteScenarioSteps(recorder, """
            Personas: central-a (hr-core, assessor 1), br1-mgr (hr-core, approval step 1, assessor 2), a-management-approver (step 2).
            1. central-a creates a requisition on a free vacant BR1 position; br1-mgr and a-management-approver approve it.
            2. AC-26a (negative): publishing without assessment criteria is rejected.
            3. AC-26b (negative): a criteria version whose weights sum to 90 is rejected.
            4. AC-26a (positive): criteria v1 (interview 40 / exam 30 / experience 30) is created and the requisition is published with a closing date.
            5. An HR application is registered, screened, COI declared and shortlisted.
            6. AC-26e (negative): an interview in the past is rejected. (positive): an interview is scheduled with a panel.
            7. AC-26c (negative): Interview-stage scores before an attended interview are rejected.
            8. Test-only time travel: the slot is moved one hour into the past; central-a completes it as Attended.
            9. AC-26c (positive): central-a (80/70/90 → 80.00) and br1-mgr (60/50/70 → 60.00) score; final 70.00; status Assessed.
            10. AC-26d (negative): a new criteria version on the locked version without a reason is rejected.
            11. Rank 1 → Ranked. AC-26e (negative): scheduling for a Ranked application is rejected and the status is unchanged.
            """);

        var (reqId, _) = await CreateApprovedRequisitionAsync(recorder, central, br1, approver);
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";

        // AC-26a negative: no criteria → publish rejected.
        var html = await central.PostAsync("26a-publish-without-criteria", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", FutureDate(10))]);
        ExpectedFlash.Add("يجب اعتماد معايير التقييم قبل النشر"); // asserted via DB state below
        Assert.Equal("Approved", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));

        // AC-26b negative: weights sum to 90.
        html = await central.PostAsync("26b-criteria-sum-90", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details,
            CriteriaForm(reqId, null, ("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "20", "Experience")));
        ExpectedFlash.Add("مجموع الأوزان يجب أن يساوي 100"); // asserted via DB state below
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentAssessmentCriteria\" WHERE \"RequisitionId\"='{reqId}';"));

        // AC-26a positive: v1 then publish.
        await central.PostAsync("26a-criteria-v1", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details,
            CriteriaForm(reqId, null, ("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentAssessmentCriteria\" WHERE \"RequisitionId\"='{reqId}' AND \"IsActive\";"));

        var closingDate = FutureDate(10);
        await central.PostAsync("26a-publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", closingDate)]);
        Assert.Equal($"Published|{closingDate}", await Sql(
            $"SELECT \"Status\" || '|' || COALESCE(\"ApplicationClosingDate\"::text,'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));
        Assert.Equal(closingDate, await Sql($"SELECT \"ClosingDate\"::text FROM \"PublicJobPostings\" WHERE \"RequisitionId\"='{reqId}';"));

        // Application → shortlist.
        var appId = await CreateShortlistedApplicationAsync(central, reqId);
        var appDetails = $"ui/hr-core/RecruitmentApplications/Details/{appId}";

        // AC-26e negative: past interview.
        html = await central.PostAsync("26e-schedule-past", "ui/hr-core/RecruitmentApplications/ScheduleInterview", appDetails,
            ScheduleForm(appId, DateTime.UtcNow.AddHours(-2)));
        ExpectedFlash.Add("موعد المقابلة يجب أن يكون في المستقبل"); // asserted via DB state below
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentInterviewSlots\" WHERE \"RecruitmentApplicationId\"='{appId}';"));

        // AC-26e positive.
        await central.PostAsync("26e-schedule", "ui/hr-core/RecruitmentApplications/ScheduleInterview", appDetails,
            ScheduleForm(appId, DateTime.UtcNow.AddDays(2)));
        var slotId = await Sql($"SELECT \"Id\" FROM \"RecruitmentInterviewSlots\" WHERE \"RecruitmentApplicationId\"='{appId}' AND \"Status\"=0;");
        Assert.Matches("^[0-9a-f-]{36}$", slotId);
        Assert.Equal(AppInterviewScheduled, await AppStatusAsync(appId));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentInterviewPanelMembers\" WHERE \"SlotId\"='{slotId}';"));

        // AC-26c negative: Interview-stage item before an attended interview.
        html = await central.PostAsync("26c-score-before-interview", "ui/hr-core/RecruitmentApplications/SubmitScores", appDetails,
            ScoreForm(appId, "80", "70", "90"));
        ExpectedFlash.Add("بنود مرحلة المقابلة لا تُقيَّم إلا بعد إكمال المقابلة"); // asserted via DB state below
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentAssessmentScores\" WHERE \"RecruitmentApplicationId\"='{appId}';"));

        // Test-only time travel, then complete as Attended.
        await Sql($"UPDATE \"RecruitmentInterviewSlots\" SET \"ScheduledAt\" = now() - interval '1 hour' WHERE \"Id\"='{slotId}';");
        await central.PostAsync("26e-complete-attended", "ui/hr-core/RecruitmentApplications/CompleteInterview", appDetails,
            [new("applicationId", appId), new("slotId", slotId), new("outcome", "Attended"), new("notes", "AC-HCM-26 حضر المرشح")]);
        Assert.Equal("1|0", await Sql($"SELECT \"Status\" || '|' || \"Outcome\" FROM \"RecruitmentInterviewSlots\" WHERE \"Id\"='{slotId}';"));

        // AC-26c positive: two assessors, exact weighted mean.
        await central.PostAsync("26c-score-assessor-1", "ui/hr-core/RecruitmentApplications/SubmitScores", appDetails,
            ScoreForm(appId, "80", "70", "90"));
        Assert.Equal(AppAssessed, await AppStatusAsync(appId));
        await br1.PostAsync("26c-score-assessor-2", "ui/hr-core/RecruitmentApplications/SubmitScores", appDetails,
            ScoreForm(appId, "60", "50", "70"));
        Assert.Equal(2, await Scalar($"SELECT COUNT(DISTINCT \"AssessorUserId\") FROM \"RecruitmentAssessmentScores\" WHERE \"RecruitmentApplicationId\"='{appId}';"));
        Assert.Equal("70.00", await Sql($"SELECT to_char(\"AssessmentScore\", 'FM999990.00') FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';"));
        Assert.NotEqual("", await Sql($"SELECT COALESCE(\"LockedAt\"::text,'') FROM \"RecruitmentAssessmentCriteria\" WHERE \"RequisitionId\"='{reqId}' AND \"IsActive\";"));

        // AC-26d negative: replacing the locked version without a reason.
        html = await central.PostAsync("26d-new-version-on-locked", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details,
            CriteriaForm(reqId, null, ("interview", "المقابلة", "50", "Interview"), ("exam", "الاختبار", "25", "Exam"), ("experience", "الخبرة", "25", "Experience")));
        ExpectedFlash.Add("تغيير معايير بدأ التقييم عليها يتطلب سبباً مكتوباً"); // asserted via DB state below
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"RecruitmentAssessmentCriteria\" WHERE \"RequisitionId\"='{reqId}';"));

        // Rank, then AC-26e negative on a Ranked application.
        await central.PostAsync("26-rank", "ui/hr-core/RecruitmentApplications/AssignRank", appDetails,
            [new("ApplicationId", appId), new("Rank", "1")]);
        Assert.Equal(AppRanked, await AppStatusAsync(appId));
        html = await central.PostAsync("26e-schedule-ranked", "ui/hr-core/RecruitmentApplications/ScheduleInterview", appDetails,
            ScheduleForm(appId, DateTime.UtcNow.AddDays(3)));
        ExpectedFlash.Add("يمكن جدولة المقابلة فقط"); // asserted via DB state below
        Assert.Equal(AppRanked, await AppStatusAsync(appId));

        recorder.WriteAfter(new { requisitionId = reqId, applicationId = appId, slotId, finalScore = "70.00", status = "Ranked" });
        recorder.WriteDbChecks(new
        {
            scores = await Sql($"SELECT string_agg(\"ItemKey\" || '=' || \"Score\", ',' ORDER BY \"ItemKey\") FROM \"RecruitmentAssessmentScores\" WHERE \"RecruitmentApplicationId\"='{appId}';"),
            criteria = await Sql($"SELECT \"VersionNumber\" || '|' || \"IsActive\" || '|' || (\"LockedAt\" IS NOT NULL) FROM \"RecruitmentAssessmentCriteria\" WHERE \"RequisitionId\"='{reqId}';"),
            expectedRejections = ExpectedFlash
        });
        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac26_f_g_duplicate_requisition_and_cancel_cascade()
    {
        AcceptanceEnv.RequireEnabled();
        var (recorder, central, br1, approver) = await StartAsync("26f-g", "AC-HCM-26f/g duplicate guard and cancellation cascade");
        WriteScenarioSteps(recorder, """
            1. Create, approve, define criteria and publish a requisition; register one HR application.
            2. AC-26f (negative, UI): a second requisition on the same funded position is rejected.
            3. AC-26f (negative, DB): a direct duplicate insert hits IX_RecruitmentRequisitions_Tenant_Position_Blocking.
            4. AC-26g (positive): central-a cancels the published requisition with a reason → posting unpublished, application rejected, audit row.
            """);

        var (reqId, positionId) = await CreatePublishedRequisitionAsync(recorder, central, br1, approver);
        var appId = await CreateApplicationAsync(central, reqId);

        // AC-26f UI.
        var html = await central.PostAsync("26f-create-duplicate", "ui/hr-core/RecruitmentRequisitions/Create",
            "ui/hr-core/RecruitmentRequisitions/Create", CreateRequisitionForm(positionId));
        Assert.Contains("يوجد طلب شاغر نشط مسبقاً على هذا المنصب الممول", html);
        Assert.Equal(1, await BlockingCountAsync(positionId));

        // AC-26f DB guard.
        var (exit, output) = await SqlRawAsync(
            $"INSERT INTO \"RecruitmentRequisitions\" (\"Id\",\"TenantId\",\"RequisitionNumber\",\"FundedPositionId\",\"JobTitle\",\"Status\",\"RequestedDate\",\"VacancyCount\",\"PublishingChannel\",\"CreatedAt\",\"UpdatedAt\") " +
            $"VALUES (gen_random_uuid(),'{TenantA}','AC26-DUP-{Guid.NewGuid():N}'::varchar(50),'{positionId}','dup','Draft',now(),1,'PublicOnly',now(),now());");
        recorder.CaptureHttp("26f-db-duplicate-insert", "SQL", "INSERT RecruitmentRequisitions", exit, output);
        Assert.NotEqual(0, exit);
        Assert.Contains("IX_RecruitmentRequisitions_Tenant_Position_Blocking", output);

        // AC-26g positive.
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";
        await central.PostAsync("26g-cancel-published", $"ui/hr-core/RecruitmentRequisitions/Cancel/{reqId}", details,
            [new("reason", "إلغاء الشاغر لاختبار القبول AC-HCM-26g")]);
        Assert.Equal("Cancelled|true", await Sql(
            $"SELECT \"Status\" || '|' || (\"CancelledAt\" IS NOT NULL)::text FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));
        Assert.Equal("false", await Sql($"SELECT \"IsPublished\"::text FROM \"PublicJobPostings\" WHERE \"RequisitionId\"='{reqId}';"));
        Assert.Equal(AppRejected, await AppStatusAsync(appId));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{reqId}' AND \"Action\"='CancelRequisition';"));
        Assert.Equal(0, await BlockingCountAsync(positionId));

        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac26_g_cancel_blocked_when_candidate_accepted_offer()
    {
        AcceptanceEnv.RequireEnabled();
        var (recorder, central, _, _) = await StartAsync("26g-neg", "AC-HCM-26g negative: cancel with an accepted candidate");
        WriteScenarioSteps(recorder, """
            1. Find a Published requisition in agency A that has an application in OfferAccepted or Hired (acceptance seed pipeline).
            2. AC-26g (negative): central-a tries to cancel it with a valid reason → rejected, status unchanged.
            """);

        var reqId = await Sql(
            $"SELECT r.\"Id\" FROM \"RecruitmentRequisitions\" r WHERE r.\"TenantId\"='{TenantA}' AND r.\"Status\"='Published' " +
            "AND EXISTS (SELECT 1 FROM \"RecruitmentApplications\" a WHERE a.\"RequisitionId\"=r.\"Id\" AND a.\"Status\" IN (4,9)) ORDER BY r.\"RequisitionNumber\" LIMIT 1;");
        Skip.If(string.IsNullOrWhiteSpace(reqId), "No published requisition with an accepted/hired candidate in agency A (acceptance seed pipeline missing).");

        var html = await central.PostAsync("26g-cancel-with-accepted", $"ui/hr-core/RecruitmentRequisitions/Cancel/{reqId}",
            $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}", [new("reason", "محاولة إلغاء رغم وجود مرشح قبل العرض")]);
        ExpectedFlash.Add("لا يمكن إلغاء طلب شاغر عليه مرشح معيّن أو قبل عرض التوظيف"); // asserted via DB state below
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));

        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac26_h_freeze_cascade_through_establishment_workflow()
    {
        AcceptanceEnv.RequireEnabled();
        // ESTABLISHMENT-CHANGE-APPROVAL is central-only (EstablishmentChangesController requires central write scope) and
        // step 1 needs an hr-core approver other than the creator (SoD). The persona catalog must provide one.
        var secondCentralHrCore = AcceptanceHarness.LoadPersonas().FirstOrDefault(p =>
            p.Id != "central-a" && p.Enabled
            && string.IsNullOrWhiteSpace(p.ScopeOrgUnitId) && p.Roles.Contains("hr-core")
            && p.TenantId == TenantA);
        Skip.If(secondCentralHrCore is null,
            "No second central hr-core persona in agency A to approve ESTABLISHMENT-CHANGE-APPROVAL step 1 (SoD with central-a). " +
            "The freeze cascade is covered on PostgreSQL by Hcm026EstablishmentCascadeIntegrationTests.");
        var (recorder, central, br1, approver) = await StartAsync("26h", "AC-HCM-26h freeze cascade");
        WriteScenarioSteps(recorder, """
            1. Publish two requisitions (positions P1, P2), each with one HR application.
            2. Test-only precondition: insert an Issued offer on P2's application.
            3. AC-26h (negative): freeze P2 via FundedPositions/Freeze → submit → br1-mgr + a-management-approver approve → apply fails; P2 stays Vacant, requisition stays Published.
            4. AC-26h (positive): freeze P1 the same way → P1 Frozen, requisition Cancelled («تجميد المنصب: …»), application Rejected.
            """);

        var (blockedReq, blockedPos) = await CreatePublishedRequisitionAsync(recorder, central, br1, approver);
        var blockedApp = await CreateApplicationAsync(central, blockedReq);
        await Sql(
            $"INSERT INTO \"OfferLetters\" (\"Id\",\"TenantId\",\"RecruitmentApplicationId\",\"FundedPositionId\",\"OfferNumber\",\"Status\",\"OfferedSalary\",\"ProposedStartDate\",\"IsConvertedToEmployee\",\"CreatedAt\",\"UpdatedAt\") " +
            $"VALUES (gen_random_uuid(),'{TenantA}','{blockedApp}','{blockedPos}','AC26H-{Guid.NewGuid():N}'::varchar(30),2,1000,now() + interval '30 days',false,now(),now());");

        await FreezeThroughWorkflowAsync(recorder, central, br1, approver, blockedPos, "26h-negative");
        Assert.Equal("Vacant", await Sql($"SELECT \"Status\" FROM \"FundedPositions\" WHERE \"Id\"='{blockedPos}';"));
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{blockedReq}';"));

        var (req, pos) = await CreatePublishedRequisitionAsync(recorder, central, br1, approver);
        var app = await CreateApplicationAsync(central, req);
        await FreezeThroughWorkflowAsync(recorder, central, br1, approver, pos, "26h-positive");
        Assert.Equal("Frozen", await Sql($"SELECT \"Status\" FROM \"FundedPositions\" WHERE \"Id\"='{pos}';"));
        Assert.Equal("Cancelled|تجميد المنصب: AC-HCM-26h إعادة هيكلة", await Sql(
            $"SELECT \"Status\" || '|' || COALESCE(\"CancellationReason\",'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{req}';"));
        Assert.Equal(AppRejected, await AppStatusAsync(app));

        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac26_i_intake_close_job_closes_expired_posting()
    {
        AcceptanceEnv.RequireEnabled();
        var (recorder, central, br1, approver) = await StartAsync("26i", "AC-HCM-26i intake close job");
        WriteScenarioSteps(recorder, """
            1. Publish a requisition with a closing date (≥ today + 7).
            2. Test-only time travel: set its application closing date to yesterday.
            3. Restart the hr-core container so RecruitmentIntakeCloseJob runs its first pass (one minute after start).
            4. AC-26i: IntakeClosedAt is set, the posting is unpublished, the requisition stays Published,
               /careers shows «التقديم مغلق», and HR intake on the requisition is rejected.
            """);

        var (reqId, _) = await CreatePublishedRequisitionAsync(recorder, central, br1, approver);
        await Sql($"UPDATE \"RecruitmentRequisitions\" SET \"ApplicationClosingDate\" = current_date - 1 WHERE \"Id\"='{reqId}';");
        var postingId = await Sql($"SELECT \"Id\" FROM \"PublicJobPostings\" WHERE \"RequisitionId\"='{reqId}';");

        var hrCore = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-core-web");
        var (restartExit, restartOutput) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "restart", hrCore);
        recorder.CaptureHttp("26i-restart-hr-core", "DOCKER", $"restart {hrCore}", restartExit, restartOutput);
        Assert.Equal(0, restartExit);

        var started = DateTime.UtcNow;
        string closedAt = string.Empty;
        while (DateTime.UtcNow - started < TimeSpan.FromMinutes(5))
        {
            closedAt = await Sql($"SELECT COALESCE(\"IntakeClosedAt\"::text,'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';");
            if (closedAt.Length > 0) break;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        recorder.RecordTiming("intakeCloseSeconds", (DateTime.UtcNow - started).TotalSeconds);
        Assert.False(string.IsNullOrEmpty(closedAt), "RecruitmentIntakeCloseJob did not close intake within 5 minutes of restart.");
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));
        Assert.Equal("false", await Sql($"SELECT \"IsPublished\"::text FROM \"PublicJobPostings\" WHERE \"Id\"='{postingId}';"));

        await WaitForHrCoreAsync(central);
        var careers = await central.GetAsync("26i-careers-details", $"careers/{postingId}");
        Assert.Contains("التقديم مغلق", careers);

        var html = await central.PostAsync("26i-hr-intake-after-close", "ui/hr-core/RecruitmentApplications/Create",
            "ui/hr-core/RecruitmentApplications/Index", [new("ApplicantFullName", "مرشح بعد الإغلاق"), new("RequisitionId", reqId)]);
        Assert.Contains("التقديم على هذا الشاغر مغلق", html);

        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }

    // ── Scenario building blocks ──────────────────────────────────────────

    private static async Task<(EvidenceRecorder Recorder, Ui Central, Ui Br1, Ui Approver)> StartAsync(string tag, string scenario)
    {
        var centralPersona = AcceptanceHarness.RequirePersona("central-a");
        var br1Persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var approverPersona = AcceptanceHarness.RequirePersona("a-management-approver");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(centralPersona);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        ScenarioTags.AddOrUpdate(recorder, tag);
        ExpectedFlash.Clear();
        recorder.WriteMetadata(scenario, $"{centralPersona.Id}+{br1Persona.Id}+{approverPersona.Id}");
        return (
            recorder,
            new Ui(await CreateClientAsync(centralPersona, correlationId), recorder, "central-a", tag),
            new Ui(await CreateClientAsync(br1Persona, correlationId), recorder, "br1-mgr", tag),
            new Ui(await CreateClientAsync(approverPersona, correlationId), recorder, "a-management-approver", tag));
    }

    /// <summary>
    /// Bearer client with cookies handled by <see cref="Ui"/>: the TempData/antiforgery cookies are Secure, which a
    /// CookieContainer never sends over http://localhost (browsers do), and automatic redirects drop the bearer header.
    /// </summary>
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

    private static async Task<(string ReqId, string PositionId)> CreateApprovedRequisitionAsync(
        EvidenceRecorder recorder, Ui central, Ui br1, Ui approver)
    {
        var positionId = await FindFreeBr1PositionAsync(central);
        Skip.If(positionId is null, "No vacant BR1 funded position without a blocking requisition is left in agency A.");

        await central.PostAsync("create-requisition", "ui/hr-core/RecruitmentRequisitions/Create",
            "ui/hr-core/RecruitmentRequisitions/Create", CreateRequisitionForm(positionId!));
        var reqId = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(reqId, out _), $"Requisition was not created on position {positionId}.");
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";

        await central.PostAsync("submit-requisition", $"ui/hr-core/RecruitmentRequisitions/Submit/{reqId}", details, []);
        Assert.Equal("PendingApproval", await RequisitionStatusAsync(reqId));
        await br1.PostAsync("approve-step-1", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-26 step 1")]);
        await approver.PostAsync("approve-step-2", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-26 step 2")]);
        Assert.Equal("Approved", await RequisitionStatusAsync(reqId));
        recorder.WriteBefore(new { requisitionId = reqId, positionId, status = "Approved" });
        return (reqId, positionId!);
    }

    private static async Task<(string ReqId, string PositionId)> CreatePublishedRequisitionAsync(
        EvidenceRecorder recorder, Ui central, Ui br1, Ui approver)
    {
        var (reqId, positionId) = await CreateApprovedRequisitionAsync(recorder, central, br1, approver);
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";
        await central.PostAsync("criteria-v1", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details,
            CriteriaForm(reqId, null, ("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")));
        await central.PostAsync("publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", FutureDate(10))]);
        Assert.Equal("Published", await RequisitionStatusAsync(reqId));
        return (reqId, positionId);
    }

    private static async Task<string> CreateApplicationAsync(Ui central, string reqId)
    {
        var name = $"مرشح AC26 {Guid.NewGuid():N}"[..20];
        await central.PostAsync("create-application", "ui/hr-core/RecruitmentApplications/Create", "ui/hr-core/RecruitmentApplications/Index",
            [new("ApplicantFullName", name), new("RequisitionId", reqId), new("Notes", "AC-HCM-26")]);
        var appId = await Sql($"SELECT \"Id\" FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{reqId}' AND \"ApplicantFullName\"='{name}';");
        Assert.True(Guid.TryParse(appId, out _), "HR application was not created.");
        Assert.Equal(AppSubmitted, await AppStatusAsync(appId));
        return appId;
    }

    private static async Task<string> CreateShortlistedApplicationAsync(Ui central, string reqId)
    {
        var appId = await CreateApplicationAsync(central, reqId);
        var details = $"ui/hr-core/RecruitmentApplications/Details/{appId}";
        await central.PostAsync("screen", "ui/hr-core/RecruitmentApplications/SetStatus", details, [new("id", appId), new("status", "UnderReview")]);
        await central.PostAsync("declare-coi", "ui/hr-core/RecruitmentApplications/DeclareCoi", details, [new("id", appId)]);
        await central.PostAsync("shortlist", "ui/hr-core/RecruitmentApplications/SetStatus", details, [new("id", appId), new("status", "Shortlisted")]);
        Assert.Equal(2, await AppStatusAsync(appId));
        return appId;
    }

    private static async Task FreezeThroughWorkflowAsync(EvidenceRecorder recorder, Ui central, Ui br1, Ui approver, string positionId, string step)
    {
        var positionDetails = $"ui/hr-core/FundedPositions/Details/{positionId}";
        await central.PostAsync($"{step}-freeze-draft", $"ui/hr-core/FundedPositions/Freeze/{positionId}", positionDetails,
            [new("FreezeReason", "AC-HCM-26h إعادة هيكلة")]);
        var changeId = await Sql(
            $"SELECT \"Id\" FROM \"EstablishmentChangeRequests\" WHERE \"TargetEntityId\"='{positionId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(changeId, out _), "Freeze change request was not created.");
        var changeDetails = $"ui/hr-core/EstablishmentChanges/Details/{changeId}";
        await central.PostAsync($"{step}-submit", $"ui/hr-core/EstablishmentChanges/Submit/{changeId}", changeDetails,
            [new("LegalSourceId", Syr001LegislationIds.Law50_2004), new("DecisionReference", "AC-HCM-26h")]);
        await br1.PostAsync($"{step}-approve-1", $"ui/hr-core/EstablishmentChanges/ApproveWorkflow/{changeId}", changeDetails,
            [new("ChangeRequestId", changeId), new("Comments", "AC-HCM-26h step 1")]);
        await approver.PostAsync($"{step}-approve-2", $"ui/hr-core/EstablishmentChanges/ApproveWorkflow/{changeId}", changeDetails,
            [new("ChangeRequestId", changeId), new("Comments", "AC-HCM-26h step 2")]);
        recorder.RecordTiming($"{step}-change-status", await Sql($"SELECT \"Status\" FROM \"EstablishmentChangeRequests\" WHERE \"Id\"='{changeId}';"));
    }

    private static async Task<string?> FindFreeBr1PositionAsync(Ui central)
    {
        var json = await central.GetAsync("vacant-positions", $"ui/hr-core/api/recruitment/vacant-positions?orgUnitId={Br1Scope}");
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            if (await BlockingCountAsync(id) == 0)
                return id;
        }

        return null;
    }

    // ── Forms ─────────────────────────────────────────────────────────────

    private static List<KeyValuePair<string, string>> CreateRequisitionForm(string positionId) =>
    [
        new("ScopeOrgUnitId", Br1Scope),
        new("FundedPositionId", positionId),
        new("JobTitle", "وظيفة AC-HCM-26"),
        new("JustificationNotes", "PLAN-26 acceptance"),
        new("LegalSourceId", Syr001LegislationIds.Law50_2004),
        new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)),
        new("PublishingChannel", "PublicOnly")
    ];

    private static List<KeyValuePair<string, string>> CriteriaForm(
        string reqId, string? changeReason, params (string Key, string Name, string Weight, string Stage)[] items)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", reqId), new("Title", "AC-HCM-26") };
        if (changeReason is not null) form.Add(new("ChangeReason", changeReason));
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
        new("PanelEmployeeIds", PanelEmployee),
        new("PanelNotes", "AC-HCM-26")
    ];

    private static List<KeyValuePair<string, string>> ScoreForm(string appId, string interview, string exam, string experience) =>
    [
        new("ApplicationId", appId),
        new("Scores[interview]", interview),
        new("Scores[exam]", exam),
        new("Scores[experience]", experience),
        new("Notes", "AC-HCM-26"),
        new("NoConflictDeclared", "true")
    ];

    private static string FutureDate(int days) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Each scenario keeps its own steps file; steps.md concatenates them (scenarios share the AC-HCM-26 folder).</summary>
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

    /// <summary>
    /// Expected rejection messages, written to db-checks.json. Flash (TempData) messages are not observable through
    /// the gateway — GatewayUpstreamCookieForwarding forwards only antiforgery cookies, so the TempData cookie never
    /// reaches HR.Core — therefore every negative case is asserted on the database state instead.
    /// </summary>
    private static readonly List<string> ExpectedFlash = [];

    // ── DB helpers ────────────────────────────────────────────────────────

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);

    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);

    private static async Task<(int ExitCode, string Output)> SqlRawAsync(string sql)
    {
        var postgres = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("postgresql");
        return await AcceptanceMessagingHelper.RunProcessAsync(
            "docker", "exec", postgres, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hrm_ci", "-d", "hr_core_db", "-c", sql);
    }

    private static async Task<int> AppStatusAsync(string appId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';");

    private static Task<string> RequisitionStatusAsync(string reqId) =>
        Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';");

    private static Task<long> BlockingCountAsync(string positionId) =>
        Scalar($"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\" IN ({string.Join(",", BlockingStatuses.Select(s => $"'{s}'"))});");

    private static async Task WaitForHrCoreAsync(Ui ui)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromMinutes(3))
        {
            if (await ui.TryGetStatusAsync("ui/hr-core/RecruitmentRequisitions/Index") == HttpStatusCode.OK)
                return;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        Assert.Fail("hr-core did not become ready after restart.");
    }

    /// <summary>Gateway UI client for one persona: antiforgery-aware form posts with evidence capture.</summary>
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
            // HttpClient drops the bearer header on automatic redirects, so the PRG redirect is followed by hand.
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
            return body;
        }

        public async Task<HttpStatusCode?> TryGetStatusAsync(string path)
        {
            try
            {
                using var resp = await SendAsync(HttpMethod.Get, path);
                return resp.StatusCode;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>Loads <paramref name="tokenPage"/> for an antiforgery token, posts the form and returns the final page (redirects followed).</summary>
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
            var setCookies = posted.Headers.TryGetValues("Set-Cookie", out var sc)
                ? sc.Select(c => c.Split('=', 2)[0] + (c.Contains("secure", StringComparison.OrdinalIgnoreCase) ? " (secure)" : "")).ToList()
                : [];
            var resp = await FollowRedirectsAsync(posted);
            var body = await resp.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "POST", "/" + path, (int)resp.StatusCode,
                new { fields = fields.Select(f => $"{f.Key}={f.Value}"), postStatus, redirect, setCookies, final = resp.RequestMessage?.RequestUri?.AbsolutePath, alerts = Alerts(body) });
            Assert.True((int)resp.StatusCode < 500, $"POST {path} as {persona} returned {(int)resp.StatusCode}.");
            Assert.True(resp.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"POST {path} as {persona} was denied.");
            return WebUtility.HtmlDecode(body);
        }

        private static string Snippet(string body) => body.Length > 600 ? body[..600] : body;

        private static IEnumerable<string> Alerts(string html) =>
            Regex.Matches(WebUtility.HtmlDecode(html), @"class=""[^""]*(?:alert|text-danger|validation-summary)[^""]*""[^>]*>(.*?)</", RegexOptions.Singleline)
                .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
                .Where(s => s.Length > 0)
                .Take(5);
    }
}
