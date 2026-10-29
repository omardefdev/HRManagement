using System.Globalization;
using static HRManagement.E2E.AcceptanceStack;

namespace HRManagement.E2E;

/// <summary>
/// AC-HCM-33c (PLAN-33 P4): the whole recruitment path through the gateway on the acceptance stack (lab profile) —
/// requisition and criteria → two-step approval → publication → public application with a PDF → screening, conflict of
/// interest, interview, two assessors, ranking → offer with two-step approval → candidate OTP sign-in and acceptance on
/// /careers/my → hire → HR.Contracts appointment and contract with the offer terms, one correlation id end to end.
/// Every stage also proves one refusal. Steps are HTTP requests through the gateway (the same requests the UI makes);
/// Playwright browsers are not used because the HR screens here are server-rendered forms (see PLAN-33 final report).
/// Run: RUN_PLAYWRIGHT_E2E=true, GATEWAY_BASE_URL=http://localhost:6200, KEYCLOAK_BASE_URL=http://localhost:6388,
/// KEYCLOAK_HR_GATEWAY_CLIENT_SECRET (from .env); stack from scripts/acceptance-up.ps1.
/// </summary>
public class RecruitmentToHireJourneyE2ETests
{
    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task Recruitment_requisition_to_hire_journey_on_the_acceptance_profile()
    {
        E2eEnv.RequireEnabled();
        var labScale = await ContainerEnvAsync("hr-core-web", "NationalScope__IsLabScale");
        Assert.True(labScale is null or "true",
            $"hr-core-web runs with NationalScope__IsLabScale={labScale}: recreate it without docker-compose.acceptance-prodlike.yml.");
        await RecruitmentJourney.RunAsync("AC-HCM-33", "33c", prodLike: false);
    }
}

/// <summary>
/// PLAN-33 decision 2: the same journey with production-like recruitment settings (docker-compose.acceptance-prodlike.yml:
/// IsLabScale=false, policy engine Off as in Production). Excluded from CI until PLAN-25 closes G-02. It asserts the real
/// outcome: today it fails at the first approval because no workflow instance is created, and its evidence
/// (acceptance-evidence/AC-HCM-33/prodlike) records that stop point.
/// </summary>
public class RecruitmentToHireJourneyProdLikeE2ETests
{
    [Trait("Category", "JourneyProdLike")]
    [SkippableFact]
    public async Task Recruitment_requisition_to_hire_journey_with_production_like_settings()
    {
        E2eEnv.RequireEnabled();
        var labScale = await ContainerEnvAsync("hr-core-web", "NationalScope__IsLabScale");
        Skip.IfNot(labScale == "false",
            "hr-core-web is not in the prod-like profile — recreate it with docker-compose.acceptance-prodlike.yml (see the file header).");
        await RecruitmentJourney.RunAsync("AC-HCM-33/prodlike", "prodlike", prodLike: true);
    }
}

internal static class RecruitmentJourney
{
    private const string Plan = """
        Personas: central-a (HR, creator), br1-mgr (step 1, hr-core, RIF), a-management-approver (step 2), an anonymous citizen.
        1. central-a: requisition on a free AC33-POOL position (RIF, G5), criteria v1, submit → PendingApproval + RECRUITMENT-REQUISITION-APPROVAL.
           Refusal: a-management-approver gets no approve form at step 1 («هذه الخطوة تتطلب دور «hr-core».»; server-side role check = G-04, PLAN-25).
        2. br1-mgr step 1 → a-management-approver step 2 → Approved → publish with a closing date 10 days ahead.
        3. Refusal: with the closing date moved to yesterday (test-only SQL, restored) the citizen's application is refused.
           Citizen applies on /careers with profile, consent and a PDF CV (File.Storage + ClamAV) → CitizenApplication + RecruitmentApplication.
        4. central-a: screen → COI → shortlist → interview (scheduled ahead, moved into the past by test-only SQL as in AC-HCM-26) →
           attended → scores by central-a and br1-mgr → Assessed → rank 1 → Ranked (no committee: RequiresCommitteeApproval=false).
           Refusal: an offer for the not-yet-ranked application is refused.
        5. central-a: offer at the G5 mid salary → submit → br1-mgr → a-management-approver → Issued. Refusal: the creator's own approval is refused.
           Refusal: hire before acceptance is refused.
        6. Citizen: OTP sign-in (code read from the FileDrop message); refusal: a wrong code opens no session; accepts on /careers/my.
        7. central-a: hire as a new employee (the citizen's national id).
        8. HR.Contracts: Appointment transaction for the employee and a contract with the offer grade and salary; the X-Correlation-ID of the
           run is echoed by the gateway on the hire and appears in the HR.Core and HR.Contracts logs (Ac15 pattern).
        """;

    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();
    private const int AppUnderReview = 1, AppShortlisted = 2, AppHired = 4, AppInterview = 5, AppAssessed = 6, AppRanked = 7, AppOfferExtended = 8, AppOfferAccepted = 9;
    private const int OfferPending = 1, OfferIssued = 2, OfferAccepted = 3;

    public static async Task RunAsync(string folder, string key, bool prodLike)
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var ev = new JourneyEvidence(folder, key, correlationId,
            prodLike ? "AC-HCM-33 requisition-to-hire journey, production-like settings (G-02 probe)" : "AC-HCM-33c requisition-to-hire journey (acceptance profile)");
        try
        {
            await RunCoreAsync(ev, correlationId);
            ev.Result("Pass", null, Plan);
        }
        catch (Exception ex) when (ex is not SkipException)
        {
            ev.Result("Fail", $"Stopped at «{ev.Stage}»: {ex.Message.Split('\n')[0]}", Plan);
            throw;
        }
    }

    private static async Task RunCoreAsync(JourneyEvidence ev, string corr)
    {
        var central = await SignInAsync("central-a", corr, ev);
        var br1 = await SignInAsync("br1-mgr", corr, ev);
        var approver = await SignInAsync("a-management-approver", corr, ev);
        var citizenUi = Anonymous(corr, ev, "citizen");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // 1 ── requisition + criteria + submit
        ev.Enter("1 requisition");
        var position = await CoreSql(
            "SELECT p.\"Id\" || '|' || p.\"OrgUnitId\" || '|' || p.\"GradeCode\" FROM \"FundedPositions\" p WHERE p.\"PositionCode\" LIKE 'AC33-POOL-%' " +
            "AND p.\"Status\"='Vacant' AND NOT EXISTS (SELECT 1 FROM \"RecruitmentRequisitions\" r WHERE r.\"FundedPositionId\"=p.\"Id\" " +
            "AND r.\"Status\" IN ('Draft','PendingApproval','Approved','Published')) ORDER BY p.\"PositionCode\" LIMIT 1;");
        Skip.If(position.Split('|').Length != 3, "No free AC33-POOL position — run scripts/acceptance-reset.ps1 (PLAN-33 recruitment states).");
        var (positionId, orgUnit, grade) = (position.Split('|')[0], position.Split('|')[1], position.Split('|')[2]);
        await central.PostAsync("1-create", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", orgUnit), new("FundedPositionId", positionId), new("JobTitle", $"محلل بيانات رحلة {suffix}"),
            new("JustificationNotes", "PLAN-33 E2E requisition-to-hire"), new("LegalSourceId", "SYR-LAW-50-2004"),
            new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)), new("PublishingChannel", "PublicOnly")
        ]);
        var req = await CoreSql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(req, out _), "The requisition was not created.");
        ev.Before(new { requisitionId = req, positionId, orgUnit, grade, correlationId = corr });
        var reqPage = $"ui/hr-core/RecruitmentRequisitions/Details/{req}";
        await central.PostAsync("1-criteria", "ui/hr-core/RecruitmentAssessmentCriteria/Create", reqPage, CriteriaForm(req));
        await central.PostAsync("1-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{req}", reqPage, []);
        var submitted = await CoreSql($"SELECT \"Status\" || '|' || COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{req}';");
        ev.Note($"after submit: {submitted}");
        Assert.StartsWith("PendingApproval|", submitted);
        Assert.True(Guid.TryParse(submitted.Split('|')[1], out _),
            "G-02: the submitted requisition has no approval workflow instance (NationalScope:IsLabScale=false with the policy engine Off) — " +
            "it can never be approved. PLAN-25 must bind the approval workflow in production.");
        var approverView = await approver.GetAsync("1-approver-step1-view", reqPage);
        Assert.Contains("هذه الخطوة تتطلب دور «hr-core».", approverView.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveWorkflow", approverView.Body, StringComparison.Ordinal);

        // 2 ── two-step approval + publish
        ev.Enter("2 approval and publication");
        await br1.PostAsync("2-approve-1", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{req}", reqPage, [new("RequisitionId", req), new("Comments", "E2E step 1")]);
        await approver.PostAsync("2-approve-2", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{req}", reqPage, [new("RequisitionId", req), new("Comments", "E2E step 2")]);
        Assert.Equal("Approved", await CoreSql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{req}';"));
        var closing = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await central.PostAsync("2-publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{req}", reqPage, [new("applicationClosingDate", closing)]);
        var published = await CoreSql($"SELECT \"Status\" || '|' || COALESCE(\"PublicJobPostingId\"::text,'') || '|' || \"RequiresCommitteeApproval\"::text FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{req}';");
        Assert.StartsWith("Published|", published);
        var posting = published.Split('|')[1];
        Assert.Equal("false", published.Split('|')[2]);

        // 3 ── public application (refused after the closing date, then accepted)
        ev.Enter("3 public application");
        var nationalId = "33" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
        var fullName = $"مرشح رحلة التوظيف {suffix}";
        var apply = ApplyFields(posting, fullName, nationalId, $"e2e-{suffix}@test.sy");
        var openPage = await citizenUi.GetAsync("3-posting-open", $"careers/{posting}");
        Assert.Contains("__RequestVerificationToken", openPage.Body, StringComparison.Ordinal);
        await CoreSql($"UPDATE \"RecruitmentRequisitions\" SET \"ApplicationClosingDate\"=current_date - 1 WHERE \"Id\"='{req}'; " +
                      $"UPDATE \"PublicJobPostings\" SET \"ClosingDate\"=current_date - 1 WHERE \"Id\"='{posting}';");
        try
        {
            var closedPage = await citizenUi.GetAsync("3-posting-closed", $"careers/{posting}", requireOk: false);
            Assert.DoesNotContain("name=\"CvFile\"", closedPage.Body, StringComparison.Ordinal);
            // Server-side refusal too: post with the token taken while the posting was open.
            var late = await citizenUi.PostAsync("3-apply-after-closing", "careers/apply", $"careers/{posting}", apply,
                [("CvFile", "cv.pdf", "application/pdf", Pdf)], tokenHtml: openPage.Body);
            Assert.NotEqual("/careers/submitted", late.FinalPath);
            Assert.Equal("0", await CoreSql($"SELECT COUNT(*) FROM \"CitizenApplications\" WHERE \"NationalIdHash\"='{Sha256Hex(nationalId)}';"));
        }
        finally
        {
            await CoreSql($"UPDATE \"RecruitmentRequisitions\" SET \"ApplicationClosingDate\"='{closing}' WHERE \"Id\"='{req}'; " +
                          $"UPDATE \"PublicJobPostings\" SET \"ClosingDate\"='{closing}' WHERE \"Id\"='{posting}';");
        }

        var applied = await citizenUi.PostAsync("3-apply", "careers/apply", $"careers/{posting}", apply, [("CvFile", "cv.pdf", "application/pdf", Pdf)]);
        Assert.Equal("/careers/submitted", applied.FinalPath);
        var citizenRow = (await WaitForAsync(() => CoreSql(
                "SELECT \"Id\" || '|' || \"ReferenceNumber\" || '|' || COALESCE(\"PromotedRecruitmentApplicationId\"::text,'') FROM \"CitizenApplications\" " +
                $"WHERE \"NationalIdHash\"='{Sha256Hex(nationalId)}';"), r => r.Split('|').Length == 3 && r.Split('|')[2].Length > 0,
            TimeSpan.FromSeconds(30), "citizen application")).Split('|');
        var (citizen, reference, app) = (citizenRow[0], citizenRow[1], citizenRow[2]);
        Assert.Equal("1", await CoreSql($"SELECT COUNT(*) FROM \"CandidateDocuments\" WHERE \"RecruitmentApplicationId\"='{app}';"));
        var appPage = $"ui/hr-core/RecruitmentApplications/Details/{app}";

        // 4 ── screening, COI, interview, two assessors, ranking
        ev.Enter("4 selection");
        await central.PostAsync("4-screen", "ui/hr-core/RecruitmentApplications/SetStatus", appPage, [new("id", app), new("status", "UnderReview")]);
        Assert.Equal($"{AppUnderReview}", await AppStatusAsync(app));
        await central.PostAsync("4-offer-before-ranking", "ui/hr-core/RecruitmentApplications/CreateOffer", appPage, OfferForm(app, 1, grade));
        Assert.Equal("0", await CoreSql($"SELECT COUNT(*) FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{app}';"));
        await central.PostAsync("4-coi", "ui/hr-core/RecruitmentApplications/DeclareCoi", appPage, [new("id", app)]);
        await central.PostAsync("4-shortlist", "ui/hr-core/RecruitmentApplications/SetStatus", appPage, [new("id", app), new("status", "Shortlisted")]);
        Assert.Equal($"{AppShortlisted}", await AppStatusAsync(app));
        const string panel = "a0000001-0000-4000-8000-000000000201"; // br1-mgr's employee (AcceptanceSeedIds.EmpBr1Id), as in AC-HCM-26
        await central.PostAsync("4-schedule", "ui/hr-core/RecruitmentApplications/ScheduleInterview", appPage,
        [
            new("ApplicationId", app), new("ScheduledAt", DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)),
            new("Mode", "InPerson"), new("Location", "قاعة المقابلات"), new("PanelEmployeeIds", panel), new("PanelNotes", "E2E")
        ]);
        Assert.Equal($"{AppInterview}", await AppStatusAsync(app));
        var slot = await CoreSql($"SELECT \"Id\" FROM \"RecruitmentInterviewSlots\" WHERE \"RecruitmentApplicationId\"='{app}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        await CoreSql($"UPDATE \"RecruitmentInterviewSlots\" SET \"ScheduledAt\" = now() - interval '1 hour' WHERE \"Id\"='{slot}';");
        ev.Note("test-only SQL: interview slot moved one hour into the past (as AC-HCM-26)");
        await central.PostAsync("4-complete", "ui/hr-core/RecruitmentApplications/CompleteInterview", appPage,
            [new("applicationId", app), new("slotId", slot), new("outcome", "Attended"), new("notes", "حضر المرشح")]);
        await central.PostAsync("4-score-1", "ui/hr-core/RecruitmentApplications/SubmitScores", appPage, ScoreForm(app, "80", "70", "90"));
        await br1.PostAsync("4-score-2", "ui/hr-core/RecruitmentApplications/SubmitScores", appPage, ScoreForm(app, "60", "50", "70"));
        Assert.Equal($"{AppAssessed}", await AppStatusAsync(app));
        await central.PostAsync("4-rank", "ui/hr-core/RecruitmentApplications/AssignRank", appPage, [new("ApplicationId", app), new("Rank", "1")]);
        Assert.Equal($"{AppRanked}|1", await CoreSql($"SELECT \"Status\"::text || '|' || \"Rank\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{app}';"));

        // 5 ── offer and its approval
        ev.Enter("5 offer");
        var bounds = (await SqlAsync("payroll_db",
            "SELECT g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g JOIN \"SalaryScales\" sc ON sc.\"Id\"=g.\"SalaryScaleId\" " +
            $"WHERE g.\"TenantId\"='a0000001-0000-4000-8000-000000000001' AND g.\"GradeCode\"='{grade}' AND sc.\"IsActive\" AND sc.\"EffectiveFrom\" <= now() " +
            "AND (sc.\"EffectiveTo\" IS NULL OR sc.\"EffectiveTo\" >= now()) ORDER BY sc.\"EffectiveFrom\" DESC LIMIT 1;")).Split('|');
        Assert.Equal(2, bounds.Length);
        var salary = Math.Round((decimal.Parse(bounds[0], CultureInfo.InvariantCulture) + decimal.Parse(bounds[1], CultureInfo.InvariantCulture)) / 2m, 0);
        await central.PostAsync("5-create-offer", "ui/hr-core/RecruitmentApplications/CreateOffer", appPage, OfferForm(app, salary, grade));
        var offer = await CoreSql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{app}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offer, out _), "The offer was not created.");
        var offerPage = $"ui/hr-core/OfferLetters/Details/{offer}";
        await central.PostAsync("5-submit", "ui/hr-core/OfferLetters/SubmitOffer", offerPage, [new("id", offer)]);
        Assert.Equal($"{OfferPending}|1", await OfferStateAsync(offer));
        await central.PostAsync("5-creator-approve", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offer}", offerPage, [new("OfferId", offer), new("Comments", "self")]);
        Assert.Equal($"{OfferPending}|1", await OfferStateAsync(offer));
        await br1.PostAsync("5-approve-1", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offer}", offerPage, [new("OfferId", offer), new("Comments", "E2E step 1")]);
        await approver.PostAsync("5-approve-2", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offer}", offerPage, [new("OfferId", offer), new("Comments", "E2E step 2")]);
        Assert.Equal($"{OfferIssued}", await CoreSql($"SELECT \"Status\"::text FROM \"OfferLetters\" WHERE \"Id\"='{offer}';"));
        var hirePage = $"ui/hr-core/RecruitmentApplications/Hire/{app}";
        var hireForm = HireForm(app, nationalId, orgUnit);
        var early = await central.GetAsync("5-hire-page-before-acceptance", hirePage, requireOk: false);
        if (early.Status == System.Net.HttpStatusCode.OK && early.Body.Contains("__RequestVerificationToken", StringComparison.Ordinal))
            await central.PostAsync("5-hire-before-acceptance", "ui/hr-core/RecruitmentApplications/Hire", hirePage, hireForm, tokenHtml: early.Body);
        Assert.Equal($"{AppOfferExtended}|", await CoreSql($"SELECT \"Status\"::text || '|' || COALESCE(\"HiredEmployeeId\"::text,'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{app}';"));

        // 6 ── candidate OTP sign-in and acceptance
        ev.Enter("6 candidate acceptance");
        var login = await citizenUi.PostAsync("6-otp-request", "careers/my/login", "careers/my/login",
            [new("ReferenceNumber", reference), new("NationalId", nationalId)]);
        var challenge = await WaitForAsync(() => CoreSql(
                $"SELECT COALESCE((SELECT \"Id\"::text FROM \"CandidateOtpChallenges\" WHERE \"CitizenApplicationId\"='{citizen}' ORDER BY \"CreatedAt\" DESC LIMIT 1),'')"),
            r => r.Length > 0, TimeSpan.FromSeconds(30), "OTP challenge");
        var code = await ReadOtpAsync(challenge, TimeSpan.FromSeconds(60));
        var wrong = code == "000000" ? "111111" : "000000";
        var refused = await citizenUi.PostAsync("6-otp-wrong", "careers/my/verify", "careers/my/login",
            [new("ReferenceNumber", reference), new("Code", wrong)], tokenHtml: login.Body);
        Assert.NotEqual("/careers/my", refused.FinalPath);
        Assert.False(citizenUi.HasCookie("careers_session"), "A wrong code must not open a session.");
        var verified = await citizenUi.PostAsync("6-otp-verify", "careers/my/verify", "careers/my/login",
            [new("ReferenceNumber", reference), new("Code", code)], tokenHtml: refused.Body.Contains("__RequestVerificationToken", StringComparison.Ordinal) ? refused.Body : login.Body);
        Assert.Equal("/careers/my", verified.FinalPath);
        Assert.True(citizenUi.HasCookie("careers_session"));
        await citizenUi.PostAsync("6-accept", "careers/my/offer/accept", "careers/my", [new("Acknowledge", "true"), new("FullName", fullName)]);
        Assert.Equal($"{OfferAccepted}|Candidate|{AppOfferAccepted}", await CoreSql(
            "SELECT o.\"Status\"::text || '|' || COALESCE(o.\"AcceptedBy\",'') || '|' || a.\"Status\"::text FROM \"OfferLetters\" o " +
            $"JOIN \"RecruitmentApplications\" a ON a.\"Id\"=o.\"RecruitmentApplicationId\" WHERE o.\"Id\"='{offer}';"));

        // 7 ── hire
        ev.Enter("7 hire");
        var hired = await central.PostAsync("7-hire", "ui/hr-core/RecruitmentApplications/Hire", hirePage, hireForm);
        var hireRow = (await CoreSql($"SELECT \"Status\"::text || '|' || COALESCE(\"HiredEmployeeId\"::text,'') FROM \"RecruitmentApplications\" WHERE \"Id\"='{app}';")).Split('|');
        Assert.Equal($"{AppHired}", hireRow[0]);
        var employee = hireRow[1];
        Assert.True(Guid.TryParse(employee, out _), "No HiredEmployeeId after the hire.");

        // 8 ── HR.Contracts appointment, contract terms, correlation id
        ev.Enter("8 contracts and correlation");
        var txn = (await WaitForAsync(() => ContractsSql(
                "SELECT COALESCE((SELECT \"Id\" || '|' || COALESCE(\"RecruitmentApplicationId\"::text,'') || '|' || COALESCE(\"OfferLetterId\"::text,'') " +
                $"FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{employee}' AND \"TransactionType\"='Appointment' LIMIT 1),'')"),
            r => r.Length > 0, TimeSpan.FromMinutes(2), "HR.Contracts Appointment transaction")).Split('|');
        Assert.Equal(app, txn[1]);
        Assert.Equal(offer, txn[2]);
        var contract = (await WaitForAsync(() => ContractsSql(
                "SELECT COALESCE((SELECT COALESCE(\"GradeCode\",'') || '|' || COALESCE(\"AgreedBaseSalary\"::text,'') || '|' || COALESCE(\"SourceOfferLetterId\"::text,'') " +
                $"FROM \"Contracts\" WHERE \"EmployeeId\"='{employee}' LIMIT 1),'')"),
            r => r.Length > 0, TimeSpan.FromMinutes(2), "HR.Contracts contract")).Split('|');
        Assert.Equal(grade, contract[0]);
        Assert.Equal(salary, decimal.Parse(contract[1], CultureInfo.InvariantCulture));
        Assert.Equal(offer, contract[2]);
        Assert.Equal(corr, hired.CorrelationHeader);
        // Ac15 pattern: the gateway audit chain holds this run's correlation for the HR.Core hire and for HR.Contracts (the hire
        // continues in the HireWizard). HR.Contracts' message consumer applies the X-Correlation-ID header to its execution
        // context but persists or logs it nowhere on the success path — recorded as a PLAN-33 follow-up; the consumer side
        // effect is tied to this run by the application/offer lineage asserted above.
        var audited = (await SqlAsync("gateway_audit_db",
            "SELECT COUNT(*)::text || '|' || COUNT(*) FILTER (WHERE \"Path\"='/ui/hr-core/RecruitmentApplications/Hire')::text || '|' || " +
            $"COUNT(*) FILTER (WHERE \"Path\" LIKE '/ui/hr-contracts/%')::text FROM gateway_audit_entries WHERE \"CorrelationId\"='{corr}';")).Split('|');
        Assert.True(int.Parse(audited[1], CultureInfo.InvariantCulture) >= 1, "The hire request is not in the gateway audit under the run's correlation id.");
        Assert.True(int.Parse(audited[2], CultureInfo.InvariantCulture) >= 1, "No HR.Contracts request in the gateway audit under the run's correlation id.");

        ev.DbChecks(new
        {
            requisitionId = req, postingId = posting, applicationId = app, citizenApplicationId = citizen, offerId = offer, salary, grade,
            employeeId = employee, appointmentTransactionId = txn[0], contract = new { grade = contract[0], salary = contract[1], sourceOffer = contract[2] },
            correlation = new
            {
                id = corr, gatewayEcho = hired.CorrelationHeader, gatewayAuditRows = audited[0], hireRows = audited[1], hrContractsRows = audited[2],
                consumerSideEffect = "Appointment transaction + contract linked by RecruitmentApplicationId/OfferLetterId",
                gap = "HR.Contracts does not persist or log the message correlation id on the success path (follow-up)"
            }
        });
        var audit = await CoreSql(
            "SELECT COALESCE(string_agg(\"EntityType\" || '|' || \"Action\" || '|' || to_char(\"CreatedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS'), E'\\n' ORDER BY \"CreatedAt\"),'') " +
            $"FROM \"AuditLogs\" WHERE \"EntityId\" IN ('{req}','{app}','{offer}');");
        ev.Audit(new { explicitAuditQuery = true, source = "hr_core_db.AuditLogs", correlationId = corr, rows = audit.Split('\n', StringSplitOptions.RemoveEmptyEntries) });
        ev.After(new { requisition = await CoreSql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{req}';"), application = AppHired, offer = OfferAccepted, employee });
        ev.Enter("done");
    }

    private static Task<string> AppStatusAsync(string app) =>
        CoreSql($"SELECT \"Status\"::text FROM \"RecruitmentApplications\" WHERE \"Id\"='{app}';");

    private static Task<string> OfferStateAsync(string offer) =>
        CoreSql("SELECT o.\"Status\"::text || '|' || COALESCE(i.\"CurrentStepLevel\"::text,'') FROM \"OfferLetters\" o " +
                $"LEFT JOIN \"WorkflowInstances\" i ON i.\"Id\"=o.\"WorkflowInstanceId\" WHERE o.\"Id\"='{offer}';");

    private static List<KeyValuePair<string, string>> CriteriaForm(string req)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", req), new("Title", "معايير رحلة E2E") };
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

    private static List<KeyValuePair<string, string>> ApplyFields(string posting, string fullName, string nationalId, string email) =>
    [
        new("PublicJobPostingId", posting), new("FullName", fullName), new("NationalId", nationalId), new("Email", email),
        new("PreferredChannel", "Email"), new("NotificationConsent", "true"),
        new("BirthDate", DateTime.UtcNow.Date.AddYears(-28).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("Specialization", "نظم معلومات"), new("GraduationYear", (DateTime.UtcNow.Year - 6).ToString(CultureInfo.InvariantCulture)),
        new("CoverNote", "تقديم آلي لرحلة E2E (PLAN-33)"), new("PrivacyConsent", "true"), new("Website", string.Empty),
        new("EducationLevelCode", "BACH"), new("YearsOfExperience", "4")
    ];

    private static List<KeyValuePair<string, string>> ScoreForm(string app, string interview, string exam, string experience) =>
    [
        new("ApplicationId", app), new("Scores[interview]", interview), new("Scores[exam]", exam), new("Scores[experience]", experience),
        new("Notes", "E2E"), new("NoConflictDeclared", "true")
    ];

    private static List<KeyValuePair<string, string>> OfferForm(string app, decimal salary, string grade) =>
    [
        new("ApplicationId", app), new("OfferedSalary", salary.ToString(CultureInfo.InvariantCulture)), new("GradeCode", grade),
        new("ProposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), new("ValidityDays", "14")
    ];

    private static List<KeyValuePair<string, string>> HireForm(string app, string nationalId, string orgUnit) =>
    [
        new("ApplicationId", app), new("Mode", "CreateNew"), new("NationalId", nationalId), new("FirstName", "مرشح"), new("LastName", "رحلة التوظيف"),
        new("BirthDate", DateTime.UtcNow.Date.AddYears(-28).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), new("Gender", "Female"),
        new("PrimaryOrgUnitId", orgUnit), new("ExpectedStartDate", DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        new("EmploymentCategory", "Permanent")
    ];
}
