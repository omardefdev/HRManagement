using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-27a..h (PLAN-27): offers by merit order, exception offers with three distinct approvers, offer terms
/// against the Payroll grade bounds, decline → next candidate, automatic offer expiry, hire → reserve list,
/// reserve expiry and administrative revoke.
/// Each scenario creates its own requisition through the UI (create → submit → two-step approval → criteria →
/// publish) on a free vacant BR1 position and registers HR applications through the UI. Ranking itself is
/// AC-HCM-26's scope, so ranks are set by a documented test-only SQL precondition; direct SQL is otherwise used
/// only for evidence checks and for time travel (offer expiry date, reserve date).
/// </summary>
public class AcHcm27OfferMeritTests
{
    private const string Ac = "AC-HCM-27";
    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");

    private static readonly string[] BlockingStatuses = ["Draft", "PendingApproval", "Approved", "Published"];

    // Stored as integers.
    private const int AppRejected = 3, AppHired = 4, AppAssessed = 6, AppRanked = 7, AppOfferExtended = 8,
        AppOfferAccepted = 9, AppOfferDeclined = 11, AppReserveListed = 12;
    private const int OfferDraft = 0, OfferPending = 1, OfferIssued = 2, OfferAccepted = 3, OfferDeclined = 4,
        OfferExpired = 5, OfferRevoked = 6;

    private const string OverrideReason = "المرشح الأول طلب تأجيل المباشرة ستة أشهر وأوصت اللجنة بالتالي";

    [SkippableFact]
    public async Task Ac27_a_c_d_h_merit_offer_terms_decline_next_candidate_and_revoke()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("27a-c-d-h", "AC-HCM-27a/c/d/h merit offer, salary bounds, decline → next candidate, revoke");
        WriteScenarioSteps(s.Recorder, """
            Personas: central-a (maker), br1-mgr (offer approval step 1), a-management-approver (step 2).
            1. A requisition is created, approved, given criteria and published through the UI; four HR applications are registered.
            2. Test-only precondition (ranking is AC-HCM-26's scope): three applications become Ranked 1..3; the fourth is Assessed.
            3. AC-27a (negative): an offer for the Assessed application is rejected.
            4. AC-27c (negative): an offer for rank 1 with a salary above the Payroll grade maximum is rejected.
            5. AC-27a (positive): an offer for rank 1 within the grade bounds → two-step RECRUITMENT-OFFER-APPROVAL → Issued.
            6. AC-27d: HR records the candidate's decline with a reason → offer Declined, application OfferDeclined;
               the requisition and offer pages show «المرشح التالي بالجدارة» for rank 2; an offer for rank 2 is drafted without merit override.
            7. AC-27h: the rank-2 draft is revoked as an administrative error → the application is Ranked again.
            """);

        var req = await CreateRankedRequisitionAsync(s, rankedCount: 3, withAssessed: true);
        var bounds = await RequireGradeBoundsAsync(s, req);

        // AC-27a negative: Assessed candidate.
        await s.Central.PostAsync("27a-offer-assessed", "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(req.Assessed!),
            OfferForm(req.Assessed!, bounds.Mid, req.Grade));
        ExpectedFlash.Add("يمكن إصدار العرض للمرشحين المرتبين فقط");
        Assert.Equal(0, await OfferCountAsync(req.Assessed!));
        Assert.Equal(AppAssessed, await AppStatusAsync(req.Assessed!));

        // AC-27c negative: salary above the grade maximum.
        await s.Central.PostAsync("27c-offer-salary-out-of-range", "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(req.Ranked[0]),
            OfferForm(req.Ranked[0], bounds.Max + 1000m, req.Grade));
        ExpectedFlash.Add($"الراتب المعروض خارج حدود الدرجة {req.Grade}");
        Assert.Equal(0, await OfferCountAsync(req.Ranked[0]));
        Assert.Equal(AppRanked, await AppStatusAsync(req.Ranked[0]));

        // AC-27a positive: rank 1, two-step workflow → Issued.
        var offer1 = await CreateOfferAsync(s, "27a-offer-rank-1", req.Ranked[0], bounds.Mid, req.Grade);
        Assert.Equal("false|false|false", await Sql(
            $"SELECT \"IsMeritOverride\"::text || '|' || (\"GradeDeviationReason\" IS NOT NULL)::text || '|' || \"SalaryUnverified\"::text FROM \"OfferLetters\" WHERE \"Id\"='{offer1}';"));
        Assert.Equal($"{bounds.Min.ToString("F2", CultureInfo.InvariantCulture)}|{bounds.Max.ToString("F2", CultureInfo.InvariantCulture)}", await Sql(
            $"SELECT \"GradeMinSalary\"::text || '|' || \"GradeMaxSalary\"::text FROM \"OfferLetters\" WHERE \"Id\"='{offer1}';"));
        await SubmitAndApproveAsync(s, "27a", offer1, expectedWorkflow: "RECRUITMENT-OFFER-APPROVAL", s.Br1, s.Approver);
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer1));
        Assert.Equal(AppOfferExtended, await AppStatusAsync(req.Ranked[0]));

        // AC-27d: decline → OfferDeclined → next candidate suggested → offer for rank 2 without override.
        await s.Central.PostAsync("27d-decline", $"ui/hr-core/OfferLetters/DeclineOffer/{offer1}", OfferDetails(offer1),
            [new("id", offer1), new("Reason", "قبل المرشح عرضاً من جهة أخرى")]);
        Assert.Equal(OfferDeclined, await OfferStatusAsync(offer1));
        Assert.Equal("قبل المرشح عرضاً من جهة أخرى|HrOnBehalf", await Sql(
            $"SELECT \"DeclineReason\" || '|' || \"DeclinedBy\" FROM \"OfferLetters\" WHERE \"Id\"='{offer1}';"));
        Assert.Equal(AppOfferDeclined, await AppStatusAsync(req.Ranked[0]));

        var reqPage = await s.Central.GetAsync("27d-requisition-next-candidate", $"ui/hr-core/RecruitmentRequisitions/Details/{req.Id}");
        Assert.Contains("المرشح التالي بالجدارة", reqPage);
        Assert.Contains(req.Names[1], reqPage);
        Assert.Contains("(رتبة 2)", reqPage);
        var offerPage = await s.Central.GetAsync("27d-offer-next-candidate", OfferDetails(offer1));
        Assert.Contains("المرشح التالي بالجدارة", offerPage);
        Assert.Contains($"RecruitmentApplications/Details/{req.Ranked[1]}#create-offer", offerPage);

        var offer2 = await CreateOfferAsync(s, "27d-offer-rank-2", req.Ranked[1], bounds.Mid, req.Grade);
        Assert.Equal($"{OfferDraft}|false", await Sql(
            $"SELECT \"Status\"::text || '|' || \"IsMeritOverride\"::text FROM \"OfferLetters\" WHERE \"Id\"='{offer2}';"));
        Assert.Equal(AppOfferExtended, await AppStatusAsync(req.Ranked[1]));

        // AC-27h: administrative revoke → back to Ranked.
        await s.Central.PostAsync("27h-revoke-admin-error", $"ui/hr-core/OfferLetters/Revoke/{offer2}", OfferDetails(offer2),
            [new("id", offer2), new("Reason", "خطأ إداري في تاريخ المباشرة"), new("RevertToRanked", "true")]);
        Assert.Equal(OfferRevoked, await OfferStatusAsync(offer2));
        Assert.Equal(AppRanked, await AppStatusAsync(req.Ranked[1]));
        Assert.Equal("", await Sql($"SELECT COALESCE(\"ActiveOfferLetterId\"::text,'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[1]}';"));

        s.Recorder.WriteAfter(new { requisitionId = req.Id, offer1, offer2, grade = req.Grade, bounds = new { bounds.Min, bounds.Max } });
        await WriteDbChecksAsync(s, req);
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac27_b_exception_offer_needs_reason_and_three_distinct_approvers()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("27b", "AC-HCM-27b exception (merit override) offer with three distinct approvers");
        Skip.If(s.Approver2 is null, s.Approver2Missing);
        WriteScenarioSteps(s.Recorder, """
            Personas: central-a (maker), br1-mgr (step 1 «مراجعة HR»), a-management-approver (step 2), a-management-approver-2 (step 3).
            1. Published requisition with ranks 1 and 2 (test-only ranking precondition).
            2. AC-27b (negative): an offer for rank 2 without a merit override reason is rejected.
            3. AC-27b (positive): an offer for rank 2 with IsMeritOverride and a 30+ character reason → RECRUITMENT-OFFER-OVERRIDE-APPROVAL.
            4. br1-mgr approves step 1 and a-management-approver step 2; a-management-approver is refused on step 3 (distinct approvers).
            5. a-management-approver-2 approves step 3 → Issued.
            """);

        var req = await CreateRankedRequisitionAsync(s, rankedCount: 2, withAssessed: false);
        var bounds = await RequireGradeBoundsAsync(s, req);

        await s.Central.PostAsync("27b-offer-rank-2-no-reason", "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(req.Ranked[1]),
            OfferForm(req.Ranked[1], bounds.Mid, req.Grade));
        ExpectedFlash.Add("المرشح ليس الأعلى رتبة المتاح (الرتبة المتاحة: 1)");
        Assert.Equal(0, await OfferCountAsync(req.Ranked[1]));

        var form = OfferForm(req.Ranked[1], bounds.Mid, req.Grade);
        form.Add(new("IsMeritOverride", "true"));
        form.Add(new("MeritOverrideReason", OverrideReason));
        var offer = await CreateOfferAsync(s, "27b-offer-rank-2-override", req.Ranked[1], form);
        Assert.Equal($"true|{OverrideReason}", await Sql(
            $"SELECT \"IsMeritOverride\"::text || '|' || \"MeritOverrideReason\" FROM \"OfferLetters\" WHERE \"Id\"='{offer}';"));

        await s.Central.PostAsync("27b-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offer), [new("id", offer)]);
        Assert.Equal(OfferPending, await OfferStatusAsync(offer));
        Assert.Equal("RECRUITMENT-OFFER-OVERRIDE-APPROVAL", await WorkflowCodeAsync(offer));

        await ApproveAsync(s.Br1, "27b-step-1", offer);
        await ApproveAsync(s.Approver, "27b-step-2", offer);
        Assert.Equal(3, await Scalar($"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"=(SELECT \"WorkflowInstanceId\" FROM \"OfferLetters\" WHERE \"Id\"='{offer}');"));

        await ApproveAsync(s.Approver, "27b-step-3-same-approver", offer);
        ExpectedFlash.Add("لا يجوز لنفس المعتمد اعتماد أكثر من خطوة في عرض استثنائي.");
        Assert.Equal(OfferPending, await OfferStatusAsync(offer));

        await ApproveAsync(s.Approver2!, "27b-step-3", offer);
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer));
        Assert.Equal("Completed", await Sql($"SELECT \"Status\" FROM \"WorkflowInstances\" WHERE \"Id\"=(SELECT \"WorkflowInstanceId\" FROM \"OfferLetters\" WHERE \"Id\"='{offer}');"));

        s.Recorder.WriteAfter(new { requisitionId = req.Id, offer, workflow = "RECRUITMENT-OFFER-OVERRIDE-APPROVAL" });
        await WriteDbChecksAsync(s, req, offer);
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac27_e_offer_expiry_job_and_expired_offer_cannot_be_accepted()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("27e", "AC-HCM-27e offer expiry job");
        WriteScenarioSteps(s.Recorder, """
            1. Published requisition with rank 1; its offer is issued through the two-step workflow.
            2. Test-only time travel: the offer's ExpiryDate is set to yesterday.
            3. AC-27e (negative): accepting the expired offer is rejected (offer stays Issued).
            4. The hr-core container is restarted so RecruitmentOfferExpiryJob runs its first pass (one minute after start).
            5. AC-27e (positive): the offer becomes Expired and the application OfferDeclined («انتهاء صلاحية العرض»).
            """);

        var req = await CreateRankedRequisitionAsync(s, rankedCount: 1, withAssessed: false);
        var bounds = await RequireGradeBoundsAsync(s, req);
        var offer = await CreateOfferAsync(s, "27e-offer", req.Ranked[0], bounds.Mid, req.Grade);
        await SubmitAndApproveAsync(s, "27e", offer, expectedWorkflow: "RECRUITMENT-OFFER-APPROVAL", s.Br1, s.Approver);
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer));

        await Sql($"UPDATE \"OfferLetters\" SET \"ExpiryDate\" = now() - interval '1 day' WHERE \"Id\"='{offer}';");

        await s.Central.PostAsync("27e-accept-expired", "ui/hr-core/OfferLetters/AcceptOffer", OfferDetails(offer), [new("id", offer)]);
        ExpectedFlash.Add("انتهت صلاحية العرض ولا يمكن قبوله.");
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer));

        await RestartHrCoreAsync(s, "27e");
        var expired = await WaitUntilAsync(s, "offerExpirySeconds", TimeSpan.FromMinutes(5),
            async () => await OfferStatusAsync(offer) == OfferExpired);
        Assert.True(expired, "RecruitmentOfferExpiryJob did not expire the offer within 5 minutes of restart.");
        Assert.Equal(AppOfferDeclined, await AppStatusAsync(req.Ranked[0]));
        Assert.Contains("انتهاء صلاحية العرض", await Sql($"SELECT COALESCE(\"Notes\",'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[0]}';"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{offer}' AND \"Action\"='ExpireOffer';"));

        await WaitForHrCoreAsync(s.Central);
        await WriteDbChecksAsync(s, req, offer);
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac27_f_g_hire_moves_ranked_siblings_to_reserve_list_which_expires()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("27f-g", "AC-HCM-27f/g hire → reserve list → reserve expiry");
        WriteScenarioSteps(s.Recorder, """
            1. Published requisition with ranks 1..3 and one Assessed application.
            2. Rank 1's offer is issued (two-step workflow) and accepted; central-a hires the candidate through the Hire form.
            3. AC-27f: ranks 2 and 3 become ReserveListed with ReserveUntil = today + 6 months; the Assessed application is Rejected.
            4. Test-only time travel: rank 2's ReserveUntil is set to yesterday.
            5. The hr-core container is restarted so RecruitmentReserveExpiryJob runs its first pass (two minutes after start).
            6. AC-27g: rank 2 becomes Rejected («انتهاء صلاحية قائمة الاحتياط»); rank 3 stays ReserveListed.
            """);

        var req = await CreateRankedRequisitionAsync(s, rankedCount: 3, withAssessed: true);
        var bounds = await RequireGradeBoundsAsync(s, req);
        var offer = await CreateOfferAsync(s, "27f-offer", req.Ranked[0], bounds.Mid, req.Grade);
        await SubmitAndApproveAsync(s, "27f", offer, expectedWorkflow: "RECRUITMENT-OFFER-APPROVAL", s.Br1, s.Approver);
        await s.Central.PostAsync("27f-accept", "ui/hr-core/OfferLetters/AcceptOffer", OfferDetails(offer), [new("id", offer)]);
        Assert.Equal(OfferAccepted, await OfferStatusAsync(offer));
        Assert.Equal(AppOfferAccepted, await AppStatusAsync(req.Ranked[0]));

        var nationalId = "01" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
        await s.Central.PostAsync("27f-hire", "ui/hr-core/RecruitmentApplications/Hire", $"ui/hr-core/RecruitmentApplications/Hire/{req.Ranked[0]}",
        [
            new("ApplicationId", req.Ranked[0]),
            new("Mode", "CreateNew"),
            new("NationalId", nationalId),
            new("FirstName", "مرشح"),
            new("LastName", "الجدارة"),
            new("BirthDate", DateTime.UtcNow.Date.AddYears(-28).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("Gender", "Male"),
            new("PrimaryOrgUnitId", req.OrgUnitId),
            new("ExpectedStartDate", DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("EmploymentCategory", "PERMANENT")
        ]);
        Assert.Equal(AppHired, await AppStatusAsync(req.Ranked[0]));

        var expectedUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var reserved in req.Ranked.Skip(1))
        {
            Assert.Equal(AppReserveListed, await AppStatusAsync(reserved));
            Assert.Equal($"{expectedUntil}|true", await Sql(
                $"SELECT \"ReserveUntil\"::text || '|' || (\"ReserveListedAt\" IS NOT NULL)::text FROM \"RecruitmentApplications\" WHERE \"Id\"='{reserved}';"));
        }
        Assert.Equal(AppRejected, await AppStatusAsync(req.Assessed!));
        var rank2Page = await s.Central.GetAsync("27f-reserve-badge", AppDetails(req.Ranked[1]));
        Assert.Contains("قائمة احتياط", rank2Page);
        Assert.Contains("حتى " + DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), rank2Page);

        await Sql($"UPDATE \"RecruitmentApplications\" SET \"ReserveUntil\" = current_date - 1 WHERE \"Id\"='{req.Ranked[1]}';");
        await RestartHrCoreAsync(s, "27g");
        var rejected = await WaitUntilAsync(s, "reserveExpirySeconds", TimeSpan.FromMinutes(6),
            async () => await AppStatusAsync(req.Ranked[1]) == AppRejected);
        Assert.True(rejected, "RecruitmentReserveExpiryJob did not reject the expired reserve within 6 minutes of restart.");
        Assert.Contains("انتهاء صلاحية قائمة الاحتياط", await Sql($"SELECT COALESCE(\"Notes\",'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{req.Ranked[1]}';"));
        Assert.Equal(AppReserveListed, await AppStatusAsync(req.Ranked[2]));

        await WaitForHrCoreAsync(s.Central);
        await WriteDbChecksAsync(s, req, offer);
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── Scenario building blocks ──────────────────────────────────────────

    private sealed record Session(EvidenceRecorder Recorder, Ui Central, Ui Br1, Ui Approver, Ui? Approver2, string Approver2Missing);

    private sealed record RankedRequisition(
        string Id, string PositionId, string OrgUnitId, string Grade, IReadOnlyList<string> Ranked, IReadOnlyList<string> Names, string? Assessed);

    private sealed record GradeBounds(decimal Min, decimal Max)
    {
        public decimal Mid => Math.Round((Min + Max) / 2m, 0);
    }

    private static async Task<Session> StartAsync(string tag, string scenario)
    {
        var centralPersona = AcceptanceHarness.RequirePersona("central-a");
        var br1Persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var approverPersona = AcceptanceHarness.RequirePersona("a-management-approver");
        var approver2Persona = AcceptanceHarness.LoadPersonas().FirstOrDefault(p => p.Id == "a-management-approver-2" && p.Enabled);
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(centralPersona);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        ScenarioTags.AddOrUpdate(recorder, tag);
        ExpectedFlash.Clear();
        recorder.WriteMetadata(scenario, $"{centralPersona.Id}+{br1Persona.Id}+{approverPersona.Id}+{approver2Persona?.Id ?? "-"}");

        Ui? approver2 = null;
        var approver2Missing = "Persona a-management-approver-2 is missing from personas.json (run scripts/prepare-keycloak-import.ps1).";
        if (approver2Persona is not null)
        {
            try
            {
                approver2 = new Ui(await CreateClientAsync(approver2Persona, correlationId), recorder, approver2Persona.Id, tag);
            }
            catch (Exception ex)
            {
                approver2Missing = "Persona a-management-approver-2 cannot sign in — re-sync Keycloak (scripts/sync-keycloak-dev-realm.ps1): " + ex.Message;
            }
        }

        return new Session(
            recorder,
            new Ui(await CreateClientAsync(centralPersona, correlationId), recorder, "central-a", tag),
            new Ui(await CreateClientAsync(br1Persona, correlationId), recorder, "br1-mgr", tag),
            new Ui(await CreateClientAsync(approverPersona, correlationId), recorder, "a-management-approver", tag),
            approver2,
            approver2Missing);
    }

    /// <summary>Bearer client; cookies and redirects are handled by <see cref="Ui"/> (see AC-HCM-26).</summary>
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

    /// <summary>
    /// UI: requisition on a free graded BR1 position → submit → br1-mgr + a-management-approver → criteria → publish;
    /// HR applications. Test-only SQL precondition: the first <paramref name="rankedCount"/> applications are Ranked 1..n.
    /// </summary>
    private static async Task<RankedRequisition> CreateRankedRequisitionAsync(Session s, int rankedCount, bool withAssessed)
    {
        var positionId = await FindFreeGradedBr1PositionAsync(s.Central);
        Skip.If(positionId is null, "No graded vacant BR1 funded position without a blocking requisition is left in agency A.");
        var (grade, orgUnitId) = await PositionGradeAsync(positionId!);

        await s.Central.PostAsync("create-requisition", "ui/hr-core/RecruitmentRequisitions/Create",
            "ui/hr-core/RecruitmentRequisitions/Create", CreateRequisitionForm(positionId!));
        var reqId = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(reqId, out _), $"Requisition was not created on position {positionId}.");
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";
        await s.Central.PostAsync("submit-requisition", $"ui/hr-core/RecruitmentRequisitions/Submit/{reqId}", details, []);
        await s.Br1.PostAsync("approve-requisition-1", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-27 step 1")]);
        await s.Approver.PostAsync("approve-requisition-2", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-27 step 2")]);
        Assert.Equal("Approved", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));
        await s.Central.PostAsync("criteria-v1", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details, CriteriaForm(reqId));
        await s.Central.PostAsync("publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));

        var ranked = new List<string>();
        var names = new List<string>();
        for (var rank = 1; rank <= rankedCount; rank++)
        {
            var (appId, name) = await CreateApplicationAsync(s.Central, reqId, $"رتبة{rank}");
            await Sql(
                $"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"={rank}, \"AssessmentScore\"={95 - rank * 5}, " +
                $"\"RankedAt\"=now(), \"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
            ranked.Add(appId);
            names.Add(name);
        }

        string? assessed = null;
        if (withAssessed)
        {
            (assessed, _) = await CreateApplicationAsync(s.Central, reqId, "مقيم");
            await Sql($"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppAssessed}, \"AssessmentScore\"=50 WHERE \"Id\"='{assessed}';");
        }

        s.Recorder.WriteBefore(new { requisitionId = reqId, positionId, grade, ranked, assessed });
        return new RankedRequisition(reqId, positionId!, orgUnitId, grade, ranked, names, assessed);
    }

    /// <summary>Payroll bounds of the position grade; skips when HR.Core cannot reach Payroll (S2S precondition).</summary>
    private static async Task<GradeBounds> RequireGradeBoundsAsync(Session s, RankedRequisition req)
    {
        var page = await s.Central.GetAsync("payroll-grade-hint", AppDetails(req.Ranked[0]));
        Skip.If(page.Contains("تعذّر التحقق من حدود الدرجة", StringComparison.Ordinal),
            "HR.Core cannot reach the Payroll grade lookup (Integration:PayrollBaseUrl, svc-hr-core secret or payroll.grades.read scope). " +
            "Rebuild hr-core-web/payroll-web from this branch and run scripts/sync-keycloak-dev-realm.ps1.");
        Assert.Contains($"حدود الدرجة {req.Grade} في سلّم الرواتب الساري", page);

        var row = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            $"SELECT g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g JOIN \"SalaryScales\" sc ON sc.\"Id\"=g.\"SalaryScaleId\" " +
            $"WHERE g.\"TenantId\"='{TenantA}' AND g.\"GradeCode\"='{req.Grade}' AND sc.\"IsActive\" AND sc.\"EffectiveFrom\" <= now() " +
            "AND (sc.\"EffectiveTo\" IS NULL OR sc.\"EffectiveTo\" >= now()) ORDER BY sc.\"EffectiveFrom\" DESC LIMIT 1;");
        var parts = row.Split('|');
        Assert.True(parts.Length == 2, $"Payroll grade {req.Grade} not found for agency A.");
        return new GradeBounds(
            decimal.Parse(parts[0], CultureInfo.InvariantCulture),
            decimal.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static Task<string> CreateOfferAsync(Session s, string step, string appId, decimal salary, string grade) =>
        CreateOfferAsync(s, step, appId, OfferForm(appId, salary, grade));

    private static async Task<string> CreateOfferAsync(Session s, string step, string appId, List<KeyValuePair<string, string>> form)
    {
        await s.Central.PostAsync(step, "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(appId), form);
        var offerId = await Sql(
            $"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{appId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offerId, out _), $"{step}: offer was not created for application {appId}.");
        return offerId;
    }

    private static async Task SubmitAndApproveAsync(Session s, string step, string offerId, string expectedWorkflow, Ui step1, Ui step2)
    {
        await s.Central.PostAsync($"{step}-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offerId), [new("id", offerId)]);
        Assert.Equal(OfferPending, await OfferStatusAsync(offerId));
        Assert.Equal(expectedWorkflow, await WorkflowCodeAsync(offerId));
        await ApproveAsync(step1, $"{step}-approve-1", offerId);
        await ApproveAsync(step2, $"{step}-approve-2", offerId);
    }

    private static Task ApproveAsync(Ui ui, string step, string offerId) =>
        ui.PostAsync(step, $"ui/hr-core/OfferLetters/ApproveWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", $"AC-HCM-27 {step}")]);

    private static async Task RestartHrCoreAsync(Session s, string step)
    {
        var hrCore = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-core-web");
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "restart", hrCore);
        s.Recorder.CaptureHttp($"{step}-restart-hr-core", "DOCKER", $"restart {hrCore}", exit, output);
        Assert.Equal(0, exit);
    }

    private static async Task<bool> WaitUntilAsync(Session s, string timing, TimeSpan timeout, Func<Task<bool>> condition)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            try
            {
                if (await condition())
                {
                    s.Recorder.RecordTiming(timing, (DateTime.UtcNow - started).TotalSeconds);
                    return true;
                }
            }
            catch (Exception) when (DateTime.UtcNow - started < timeout)
            {
                // psql may fail while the container restarts; keep polling.
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        return false;
    }

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

    private static async Task<string?> FindFreeGradedBr1PositionAsync(Ui central)
    {
        var json = await central.GetAsync("vacant-positions", $"ui/hr-core/api/recruitment/vacant-positions?orgUnitId={Br1Scope}");
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            if (await BlockingCountAsync(id) != 0)
                continue;
            var (grade, _) = await PositionGradeAsync(id);
            if (Regex.IsMatch(grade, "^G([1-9]|10)$"))
                return id;
        }

        return null;
    }

    private static async Task<(string Grade, string OrgUnitId)> PositionGradeAsync(string positionId)
    {
        var row = await Sql($"SELECT COALESCE(\"GradeCode\",'') || '|' || \"OrgUnitId\" FROM \"FundedPositions\" WHERE \"Id\"='{positionId}';");
        var parts = row.Split('|');
        return (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static async Task<(string Id, string Name)> CreateApplicationAsync(Ui central, string reqId, string label)
    {
        var name = $"AC27 {label} {Guid.NewGuid():N}"[..24];
        await central.PostAsync("create-application", "ui/hr-core/RecruitmentApplications/Create", "ui/hr-core/RecruitmentApplications/Index",
            [new("ApplicantFullName", name), new("RequisitionId", reqId), new("Notes", "AC-HCM-27")]);
        var appId = await Sql($"SELECT \"Id\" FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{reqId}' AND \"ApplicantFullName\"='{name}';");
        Assert.True(Guid.TryParse(appId, out _), "HR application was not created.");
        return (appId, name);
    }

    private static async Task WriteDbChecksAsync(Session s, RankedRequisition req, string? offerId = null)
    {
        s.Recorder.WriteDbChecks(new
        {
            applications = await Sql(
                $"SELECT string_agg(COALESCE(\"Rank\"::text,'-') || ':' || \"Status\" || ':' || COALESCE(\"ReserveUntil\"::text,''), ',' ORDER BY \"Rank\" NULLS LAST) " +
                $"FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{req.Id}';"),
            offers = await Sql(
                "SELECT string_agg(o.\"OfferNumber\" || ':' || o.\"Status\" || ':' || o.\"IsMeritOverride\" || ':' || COALESCE(o.\"GradeMinSalary\"::text,'') || '-' || COALESCE(o.\"GradeMaxSalary\"::text,''), ',' ORDER BY o.\"CreatedAt\") " +
                $"FROM \"OfferLetters\" o JOIN \"RecruitmentApplications\" a ON a.\"Id\"=o.\"RecruitmentApplicationId\" WHERE a.\"RequisitionId\"='{req.Id}';"),
            workflow = offerId is null ? null : await WorkflowCodeAsync(offerId),
            requisition = await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{req.Id}';"),
            expectedRejections = ExpectedFlash
        });
    }

    // ── Forms ─────────────────────────────────────────────────────────────

    private static string AppDetails(string appId) => $"ui/hr-core/RecruitmentApplications/Details/{appId}";

    private static string OfferDetails(string offerId) => $"ui/hr-core/OfferLetters/Details/{offerId}";

    private static List<KeyValuePair<string, string>> OfferForm(string appId, decimal salary, string grade) =>
    [
        new("ApplicationId", appId),
        new("OfferedSalary", salary.ToString(CultureInfo.InvariantCulture)),
        new("GradeCode", grade),
        new("ProposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("ValidityDays", "14")
    ];

    private static List<KeyValuePair<string, string>> CreateRequisitionForm(string positionId) =>
    [
        new("ScopeOrgUnitId", Br1Scope),
        new("FundedPositionId", positionId),
        new("JobTitle", "وظيفة AC-HCM-27"),
        new("JustificationNotes", "PLAN-27 acceptance"),
        new("LegalSourceId", Syr001LegislationIds.Law50_2004),
        new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)),
        new("PublishingChannel", "PublicOnly")
    ];

    private static List<KeyValuePair<string, string>> CriteriaForm(string reqId)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", reqId), new("Title", "AC-HCM-27") };
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

    /// <summary>Each scenario keeps its own steps file; steps.md concatenates them (scenarios share the AC-HCM-27 folder).</summary>
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
    /// Expected rejection messages, written to db-checks.json. Flash (TempData) messages are not observable through the
    /// gateway (see AC-HCM-26), so every negative case is asserted on the database state.
    /// </summary>
    private static readonly List<string> ExpectedFlash = [];

    // ── DB helpers ────────────────────────────────────────────────────────

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);

    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);

    private static async Task<int> AppStatusAsync(string appId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';");

    private static async Task<int> OfferStatusAsync(string offerId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{offerId}';");

    private static Task<long> OfferCountAsync(string appId) =>
        Scalar($"SELECT COUNT(*) FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{appId}';");

    private static Task<string> WorkflowCodeAsync(string offerId) =>
        Sql("SELECT d.\"Code\" FROM \"OfferLetters\" o JOIN \"WorkflowInstances\" i ON i.\"Id\"=o.\"WorkflowInstanceId\" " +
            $"JOIN \"WorkflowDefinitions\" d ON d.\"Id\"=i.\"DefinitionId\" WHERE o.\"Id\"='{offerId}';");

    private static Task<long> BlockingCountAsync(string positionId) =>
        Scalar($"SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\" IN ({string.Join(",", BlockingStatuses.Select(s => $"'{s}'"))});");

    /// <summary>Gateway UI client for one persona: antiforgery-aware form posts with evidence capture (as in AC-HCM-26).</summary>
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
            return WebUtility.HtmlDecode(body);
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
            var resp = await FollowRedirectsAsync(posted);
            var body = await resp.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "POST", "/" + path, (int)resp.StatusCode,
                new { fields = fields.Select(f => $"{f.Key}={f.Value}"), postStatus, redirect, final = resp.RequestMessage?.RequestUri?.AbsolutePath, alerts = Alerts(body) });
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
