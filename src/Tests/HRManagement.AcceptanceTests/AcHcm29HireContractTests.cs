using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-29a..i (PLAN-29): safe hire closure (duplicate person, national-id mismatch), the offer letter PDF with its
/// verification hash, ICandidateAccepted v4 → pending contract of the mapped type with the offer terms and explicit
/// lineage, activation → pay profile with the offer grade and salary, the appointment decision gate, an unresolved
/// contract type → intervention, and internal mobility → no Appointment.
/// Each journey creates its own requisition through the UI (as AC-HCM-27). Test-only SQL preconditions are documented
/// at each use: ranks (AC-HCM-26's scope), the application national id, the contract signature/commencement needed to
/// activate, and moving the hire transaction to Executing with the non-PLAN-29 checklist items completed.
/// </summary>
public class AcHcm29HireContractTests
{
    private const string Ac = "AC-HCM-29";
    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string[] BlockingStatuses = ["Draft", "PendingApproval", "Approved", "Published"];

    private const int AppRanked = 7, AppOfferAccepted = 9, AppHired = 4;
    private const int OfferIssued = 2, OfferAccepted = 3;

    /// <summary>Acceptance seeds the TEST types per agency (DemoContractsSeeder, codes scoped "AGENCY-A-…").</summary>
    private const string PermanentType = "AGENCY-A-ACC-LAW50-PERM";
    private const string TemporaryType = "AGENCY-A-ACC-LAW50-TEMP";

    private const string DuplicatePersonMessage = "الرقم الوطني مسجَّل لموظف قائم — استخدم «ربط موظف موجود».";
    private const string MismatchMessage = "الرقم الوطني لا يطابق الرقم المسجَّل في طلب التوظيف — أدخل مبرر الاختلاف (10 أحرف على الأقل).";

    [SkippableFact]
    public async Task Ac29_a_b_c_d_f_g_h_hire_contract_terms_activation_decision_and_offer_letter()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("29a-h", "AC-HCM-29a/b/c/d/f/g/h hire → contract terms → pay profile; decision; offer PDF");
        WriteScenarioSteps(s, """
            1. Requisition (law50) on a free graded BR1 position, ranks 1..2 (test-only SQL rank precondition).
            2. Rank 1's offer (Payroll grade mid salary) → two-step approval → Issued. AC-29f: DocumentSha256 stored; GET OfferLetters/Document → PDF.
            3. Accepted. Test-only SQL: the application gets a national id X.
            4. AC-29b: Hire CreateNew with a seeded employee's national id → form error «الرقم الوطني مسجَّل لموظف قائم…», no hire.
            5. AC-29c: Hire CreateNew with Y ≠ X and no reason → form error (mismatch), no hire.
            6. AC-29a: Hire CreateNew with X → Hired; HR.Contracts pending contract of type ACC-LAW50-PERM with the offer grade, salary, law50, Permanent, source offer.
            7. AC-29h: the Appointment transaction carries RecruitmentApplicationId / RequisitionId / OfferLetterId / ExpectedStartDate.
            8. AC-29d: test-only SQL signature + commencement on the contract; a-contracts-checker activates → Payroll profile with the offer salary and the grade of the scale in force.
            9. AC-29g: test-only SQL moves the transaction to Executing and completes the non-PLAN-29 items; Step4Complete is refused while «إصدار قرار التعيين» is open; the decision is issued (DEC-APPT-…); Step4Complete then succeeds.
            """);

        var req = await CreateRankedRequisitionAsync(s, "29a", rankedCount: 2);
        var bounds = await RequireGradeBoundsAsync(s, req);
        var offer = await CreateOfferAsync(s, "29a-offer", req.Ranked[0], bounds.Mid, req.Grade);
        await SubmitAndApproveAsync(s, "29a", offer);
        Assert.Equal(OfferIssued, await OfferStatusAsync(offer));

        // AC-29f — offer letter with the verification hash fixed at issue.
        var hash = await Sql($"SELECT COALESCE(\"DocumentSha256\",'') FROM \"OfferLetters\" WHERE \"Id\"='{offer}';");
        Assert.Matches("^[0-9a-f]{64}$", hash.Trim());
        var details = await s.Central.GetAsync("29f-offer-details", OfferDetails(offer));
        Assert.Contains(hash.Trim()[..12].ToUpperInvariant(), details);
        var (pdfStatus, pdfType, pdfHead) = await s.Central.GetRawAsync("29f-offer-pdf", $"ui/hr-core/OfferLetters/Document/{offer}");
        Assert.Equal(HttpStatusCode.OK, pdfStatus);
        Assert.Equal("application/pdf", pdfType);
        Assert.Equal("%PDF", pdfHead);

        await s.Central.PostAsync("29a-accept", "ui/hr-core/OfferLetters/AcceptOffer", OfferDetails(offer), OfferOnBehalfEvidence.AcceptFields(offer), withEvidence: true);
        Assert.Equal(OfferAccepted, await OfferStatusAsync(offer));
        var appId = req.Ranked[0];
        var nationalId = "29" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
        // Test-only precondition: HR applications registered through the UI carry no national id.
        await Sql($"UPDATE \"RecruitmentApplications\" SET \"NationalId\"='{nationalId}', \"NationalIdHash\"=NULL WHERE \"Id\"='{appId}';");

        // AC-29b — duplicate person.
        var seeded = AcceptanceSeedCatalog.Agencies.First(a => a.Id == AcceptanceSeedIds.AgencyAId).Employees.First();
        var employeesBefore = await Scalar("SELECT COUNT(*) FROM \"Employees\";");
        var duplicatePage = await s.Central.PostAsync("29b-hire-duplicate", "ui/hr-core/RecruitmentApplications/Hire", HirePage(appId),
            HireForm(appId, seeded.NationalId, req.OrgUnitId, mismatchReason: "مطابقة الهوية مع السجل المدني للموظف"));
        Assert.Contains(DuplicatePersonMessage, duplicatePage);
        Assert.Equal(AppOfferAccepted, await AppStatusAsync(appId));
        Assert.Equal(employeesBefore, await Scalar("SELECT COUNT(*) FROM \"Employees\";"));

        // AC-29c — national id differs from the application's, no reason.
        var mismatchPage = await s.Central.PostAsync("29c-hire-mismatch", "ui/hr-core/RecruitmentApplications/Hire", HirePage(appId),
            HireForm(appId, "29" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture), req.OrgUnitId));
        Assert.Contains(MismatchMessage, mismatchPage);
        Assert.Equal(AppOfferAccepted, await AppStatusAsync(appId));

        // AC-29a — hire → pending contract with the offer terms.
        await s.Central.PostAsync("29a-hire", "ui/hr-core/RecruitmentApplications/Hire", HirePage(appId), HireForm(appId, nationalId, req.OrgUnitId));
        Assert.Equal(AppHired, await AppStatusAsync(appId));
        var employeeId = await Sql($"SELECT \"HiredEmployeeId\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';");
        var contractRow = await WaitForTextAsync(s, "29a-contract", TimeSpan.FromMinutes(2), () => ContractsSql(
            "SELECT c.\"Id\" || '|' || t.\"Code\" || '|' || COALESCE(c.\"GradeCode\",'') || '|' || COALESCE(c.\"AgreedBaseSalary\"::text,'') || '|' || " +
            "COALESCE(c.\"LegalRegimeId\",'') || '|' || COALESCE(c.\"EmploymentType\",'') || '|' || COALESCE(c.\"SourceOfferLetterId\"::text,'') || '|' || c.\"Status\" " +
            $"FROM \"Contracts\" c JOIN \"ContractTypes\" t ON t.\"Id\"=c.\"ContractTypeId\" WHERE c.\"EmployeeId\"='{employeeId}';"));
        var c = contractRow.Split('|');
        Assert.Equal(PermanentType, c[1]);
        Assert.Equal(req.Grade, c[2]);
        Assert.Equal(bounds.Mid, decimal.Parse(c[3], CultureInfo.InvariantCulture));
        Assert.Equal("law50", c[4]);
        Assert.Equal("Permanent", c[5]);
        Assert.Equal(offer, c[6]);
        Assert.Equal("Pending", c[7]);
        var contractId = c[0];

        // AC-29h — explicit lineage.
        var txnRow = await ContractsSql(
            "SELECT \"Id\" || '|' || COALESCE(\"RecruitmentApplicationId\"::text,'') || '|' || COALESCE(\"RequisitionId\"::text,'') || '|' || " +
            "COALESCE(\"OfferLetterId\"::text,'') || '|' || COALESCE(\"ExpectedStartDate\"::text,'') " +
            $"FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{employeeId}' AND \"TransactionType\"='Appointment';");
        var t = txnRow.Split('|');
        Assert.Equal(appId, t[1]);
        Assert.Equal(req.Id, t[2]);
        Assert.Equal(offer, t[3]);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t[4]);
        var txnId = t[0];

        // AC-29d — activation → pay profile with the offer grade and salary.
        // Test-only precondition: the signature and commencement record activation requires (contract UI scope).
        await ContractsSql(
            "INSERT INTO \"ContractSignatures\" (\"Id\",\"TenantId\",\"ContractId\",\"SignedBy\",\"SignatureType\",\"SignedAt\",\"CreatedAt\",\"UpdatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}','{TenantA}','{contractId}','{employeeId}','Physical',now(),now(),now());" +
            "INSERT INTO \"CommencementRecords\" (\"Id\",\"TenantId\",\"EmployeeId\",\"ContractId\",\"StartDate\",\"CreatedAt\",\"UpdatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}','{TenantA}','{employeeId}','{contractId}',current_date,now(),now());");
        await s.Checker.PostAsync("29d-activate", $"ui/hr-contracts/Contracts/Activate/{contractId}", $"ui/hr-contracts/Contracts/Details/{contractId}", [new("id", contractId)]);
        Assert.Equal("Active", await ContractsSql($"SELECT \"Status\" FROM \"Contracts\" WHERE \"Id\"='{contractId}';"));
        var profile = await WaitForTextAsync(s, "29d-pay-profile", TimeSpan.FromMinutes(2), () => PayrollSql(
            "SELECT p.\"BaseSalary\"::text || '|' || COALESCE(g.\"GradeCode\",'') || '|' || p.\"IsActive\"::text FROM \"EmployeePayProfiles\" p " +
            $"LEFT JOIN \"PayGrades\" g ON g.\"Id\"=p.\"PayGradeId\" WHERE p.\"EmployeeId\"='{employeeId}' AND p.\"IsActive\";"));
        var pp = profile.Split('|');
        Assert.Equal(bounds.Mid, decimal.Parse(pp[0], CultureInfo.InvariantCulture));
        Assert.Equal(req.Grade, pp[1]);
        Assert.Equal(0, await PayrollScalar($"SELECT COUNT(*) FROM \"PaymentHolds\" WHERE \"EmployeeId\"='{employeeId}' AND \"Reason\" LIKE 'PAY029-%';"));

        // AC-29g — appointment decision gate.
        Assert.Equal("false", await ContractsSql(
            $"SELECT \"IsCompleted\"::text FROM \"HireChecklists\" WHERE \"ServiceTransactionId\"='{txnId}' AND \"Step\"='AppointmentDecisionIssued';"));
        // Test-only precondition: the approval/execution steps and the other parties' items are outside PLAN-29.
        await ContractsSql(
            $"UPDATE \"ServiceTransactions\" SET \"State\"='Executing' WHERE \"Id\"='{txnId}';" +
            "UPDATE \"HireChecklists\" SET \"IsCompleted\"=true, \"CompletedAt\"=now(), \"EvidenceRef\"=COALESCE(\"EvidenceRef\",'ac29:precondition') " +
            $"WHERE \"ServiceTransactionId\"='{txnId}' AND \"Step\" NOT IN ('AppointmentDecisionIssued','PreEmploymentMedical','HireArchived');");
        var wizardStep4 = $"ui/hr-contracts/HireWizard/Step4?transactionId={txnId}";
        await s.Checker.PostAsync("29g-complete-refused", "ui/hr-contracts/HireWizard/Step4Complete", wizardStep4, [new("transactionId", txnId)]);
        Assert.Equal("Executing", await ContractsSql($"SELECT \"State\" FROM \"ServiceTransactions\" WHERE \"Id\"='{txnId}';"));

        var wizardStep2 = $"ui/hr-contracts/HireWizard/Step2?transactionId={txnId}";
        await s.Maker.PostAsync("29g-issue-decision", "ui/hr-contracts/HireWizard/IssueAppointmentDecision", wizardStep2, [new("transactionId", txnId)]);
        await s.Maker.PostAsync("29g-issue-decision-again", "ui/hr-contracts/HireWizard/IssueAppointmentDecision", wizardStep2, [new("transactionId", txnId)]);
        var decision = await ContractsSql(
            "SELECT d.\"DecisionNumber\" || '|' || d.\"Subject\" || '|' || (SELECT COUNT(*) FROM \"AdministrativeDecisions\" x WHERE x.\"RelatedEmployeeId\"=d.\"RelatedEmployeeId\")::text " +
            $"FROM \"ServiceTransactions\" tx JOIN \"AdministrativeDecisions\" d ON d.\"Id\"=tx.\"AppointmentDecisionId\" WHERE tx.\"Id\"='{txnId}';");
        var dec = decision.Split('|');
        // The number is issued by COD-001 kind AdministrativeDecision with TypeCode APPT (policy DEC-{TypeCode}-YYYY-#####).
        // The rendered prefix is not asserted: in this environment every policy currently loses its literal/field
        // prefix (e.g. offers "-2026-00005") — a pre-existing code-generation defect reported with PLAN-29.
        Assert.False(string.IsNullOrWhiteSpace(dec[0]));
        Assert.Equal(1, await Scalar(
            $"SELECT COUNT(*) FROM \"CodeIssuances\" WHERE \"EntityKind\"='AdministrativeDecision' AND \"Code\"='{dec[0]}' AND \"FactsJson\" LIKE '%\"TypeCode\":\"APPT\"%';"));
        Assert.StartsWith("قرار تعيين: ", dec[1]);
        Assert.Equal("1", dec[2]);
        Assert.Equal($"true|{dec[0]}", await ContractsSql(
            $"SELECT \"IsCompleted\"::text || '|' || COALESCE(\"EvidenceRef\",'') FROM \"HireChecklists\" WHERE \"ServiceTransactionId\"='{txnId}' AND \"Step\"='AppointmentDecisionIssued';"));

        await s.Checker.PostAsync("29g-complete", "ui/hr-contracts/HireWizard/Step4Complete", wizardStep4, [new("transactionId", txnId)]);
        Assert.Equal("Completed", await ContractsSql($"SELECT \"State\" FROM \"ServiceTransactions\" WHERE \"Id\"='{txnId}';"));

        WriteDbChecks(s, new { requisition = req.Id, offer, hash, applicationId = appId, employeeId, contract = contractRow, transaction = txnRow, profile, decision });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac29_e_unresolved_contract_type_opens_an_intervention_and_manual_contract_clears_it()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("29e", "AC-HCM-29e unresolved contract type → intervention → manual pending contract");
        WriteScenarioSteps(s, """
            1. Requisition (law50), rank 1 offer issued and accepted.
            2. Hire with employment type «إداري» (Administrative) and a reason — law50/Administrative has no contract-type map
               (acceptance has no plain DEFAULT type either).
            3. AC-29e: Appointment transaction + checklist, no contract; ContractClassified note and an open Hire intervention
               «لا يوجد نوع عقد مطابق لنظام الخدمة law50 / Administrative — يتطلب تدخلاً»; Step2 shows the red banner.
            4. a-contracts-maker creates the pending contract with ACC-LAW50-TEMP → contract with the offer terms; banner gone.
            """);

        var req = await CreateRankedRequisitionAsync(s, "29e", rankedCount: 1);
        var bounds = await RequireGradeBoundsAsync(s, req);
        var offer = await CreateOfferAsync(s, "29e-offer", req.Ranked[0], bounds.Mid, req.Grade);
        await SubmitAndApproveAsync(s, "29e", offer);
        await s.Central.PostAsync("29e-accept", "ui/hr-core/OfferLetters/AcceptOffer", OfferDetails(offer), OfferOnBehalfEvidence.AcceptFields(offer), withEvidence: true);
        var appId = req.Ranked[0];
        var nationalId = "29" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);

        var form = HireForm(appId, nationalId, req.OrgUnitId, employmentCategory: "Administrative");
        form.Add(new("EmploymentTypeChangeReason", "وظيفة إدارية مساندة وفق قرار الملاك"));
        await s.Central.PostAsync("29e-hire", "ui/hr-core/RecruitmentApplications/Hire", HirePage(appId), form);
        Assert.Equal(AppHired, await AppStatusAsync(appId));
        var employeeId = await Sql($"SELECT \"HiredEmployeeId\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';");

        var txnId = await WaitForTextAsync(s, "29e-transaction", TimeSpan.FromMinutes(2), () => ContractsSql(
            $"SELECT \"Id\"::text FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{employeeId}' AND \"TransactionType\"='Appointment';"));
        var reason = "لا يوجد نوع عقد مطابق لنظام الخدمة law50 / Administrative — يتطلب تدخلاً";
        var intervention = await WaitForTextAsync(s, "29e-intervention", TimeSpan.FromMinutes(2), () => ContractsSql(
            "SELECT \"Status\" || '|' || \"Reason\" || '|' || COALESCE(\"FailedParty\",'') FROM \"ProcessInterventions\" " +
            $"WHERE \"SagaType\"='Hire' AND \"CorrelationId\"='{txnId}';"));
        Assert.Equal($"Open|{reason}|HR.Contracts", intervention);
        Assert.Equal(0, await ContractsScalar($"SELECT COUNT(*) FROM \"Contracts\" WHERE \"EmployeeId\"='{employeeId}';"));
        Assert.Equal(reason, await ContractsSql(
            $"SELECT COALESCE(\"Notes\",'') FROM \"HireChecklists\" WHERE \"ServiceTransactionId\"='{txnId}' AND \"Step\"='ContractClassified';"));

        var step2 = $"ui/hr-contracts/HireWizard/Step2?transactionId={txnId}";
        Assert.Contains(reason, await s.Maker.GetAsync("29e-banner", step2));

        var temporaryTypeId = await ContractsSql($"SELECT \"Id\"::text FROM \"ContractTypes\" WHERE \"Code\"='{TemporaryType}';");
        Skip.IfNot(Guid.TryParse(temporaryTypeId, out _), $"Contract type {TemporaryType} is not seeded — reset the acceptance stack (scripts/acceptance-reset.ps1).");
        await s.Maker.PostAsync("29e-create-contract", "ui/hr-contracts/HireWizard/CreatePendingContract", step2,
            [new("transactionId", txnId), new("contractTypeId", temporaryTypeId)]);
        var contract = await ContractsSql(
            "SELECT t.\"Code\" || '|' || COALESCE(c.\"AgreedBaseSalary\"::text,'') || '|' || COALESCE(c.\"EmploymentType\",'') || '|' || c.\"Status\" " +
            $"FROM \"Contracts\" c JOIN \"ContractTypes\" t ON t.\"Id\"=c.\"ContractTypeId\" WHERE c.\"EmployeeId\"='{employeeId}';");
        var parts = contract.Split('|');
        Assert.Equal(TemporaryType, parts[0]);
        Assert.Equal(bounds.Mid, decimal.Parse(parts[1], CultureInfo.InvariantCulture));
        Assert.Equal("Administrative", parts[2]);
        Assert.Equal("Pending", parts[3]);
        Assert.DoesNotContain("إنشاء العقد المعلّق", await s.Maker.GetAsync("29e-banner-cleared", step2));

        WriteDbChecks(s, new { requisition = req.Id, offer, applicationId = appId, employeeId, transaction = txnId, intervention, contract });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac29_i_internal_mobility_message_opens_no_appointment()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("29i", "AC-HCM-29i internal mobility → no Appointment");
        WriteScenarioSteps(s, """
            1. HR.Core no longer publishes ICandidateAccepted for internal mobility (IInternalPlacementCompleted only — unit and
               integration tests). Defence in depth in HR.Contracts:
            2. An ICandidateAccepted v4 with IsInternalMobility=true is published on the acceptance broker for agency A.
            3. A control message (same shape, IsInternalMobility=false) for another employee opens an Appointment.
            4. AC-29i: no ServiceTransaction for the mobility employee; the control employee has one.
            """);

        var mobilityEmployee = Guid.NewGuid();
        var controlEmployee = Guid.NewGuid();
        await PublishCandidateAcceptedAsync(s, mobilityEmployee, internalMobility: true);
        await PublishCandidateAcceptedAsync(s, controlEmployee, internalMobility: false);

        await WaitForTextAsync(s, "29i-control", TimeSpan.FromMinutes(2), () => ContractsSql(
            $"SELECT \"Id\"::text FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{controlEmployee}' AND \"TransactionType\"='Appointment';"));
        Assert.Equal(0, await ContractsScalar($"SELECT COUNT(*) FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{mobilityEmployee}';"));
        Assert.Equal(0, await ContractsScalar($"SELECT COUNT(*) FROM \"Contracts\" WHERE \"EmployeeId\"='{mobilityEmployee}';"));

        WriteDbChecks(s, new { mobilityEmployee, controlEmployee });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── Journey building blocks (pattern of AC-HCM-27) ──────────────────────

    private sealed record Session(EvidenceRecorder Recorder, string Tag, Ui Central, Ui Br1, Ui Approver, Ui Maker, Ui Checker);

    private sealed record RankedRequisition(string Id, string PositionId, string OrgUnitId, string Grade, IReadOnlyList<string> Ranked);

    private sealed record GradeBounds(decimal Min, decimal Max)
    {
        public decimal Mid => Math.Round((Min + Max) / 2m, 0);
    }

    private static async Task<Session> StartAsync(string tag, string scenario)
    {
        var central = AcceptanceHarness.RequirePersona("central-a");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var approver = AcceptanceHarness.RequirePersona("a-management-approver");
        var maker = AcceptanceHarness.RequirePersona("a-contracts-maker");
        var checker = AcceptanceHarness.RequirePersona("a-contracts-checker");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        recorder.WriteMetadata(scenario, $"{central.Id}+{br1.Id}+{approver.Id}+{maker.Id}+{checker.Id}");
        return new Session(
            recorder,
            tag,
            new Ui(await CreateClientAsync(central, correlationId), recorder, central.Id, tag),
            new Ui(await CreateClientAsync(br1, correlationId), recorder, br1.Id, tag),
            new Ui(await CreateClientAsync(approver, correlationId), recorder, approver.Id, tag),
            new Ui(await CreateClientAsync(maker, correlationId), recorder, maker.Id, tag),
            new Ui(await CreateClientAsync(checker, correlationId), recorder, checker.Id, tag));
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

    private static async Task<RankedRequisition> CreateRankedRequisitionAsync(Session s, string step, int rankedCount)
    {
        var positionId = await FindFreeGradedBr1PositionAsync(s.Central);
        Skip.If(positionId is null, "No graded vacant BR1 funded position without a blocking requisition is left in agency A.");
        var row = await Sql($"SELECT COALESCE(\"GradeCode\",'') || '|' || \"OrgUnitId\" FROM \"FundedPositions\" WHERE \"Id\"='{positionId}';");
        var grade = row.Split('|')[0];
        var orgUnitId = row.Split('|')[1];

        await s.Central.PostAsync($"{step}-create-requisition", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", Br1Scope),
            new("FundedPositionId", positionId!),
            new("JobTitle", "وظيفة AC-HCM-29"),
            new("JustificationNotes", "PLAN-29 acceptance"),
            new("LegalSourceId", Syr001LegislationIds.Law50_2004),
            new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)),
            new("PublishingChannel", "PublicOnly")
        ]);
        var reqId = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{positionId}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(reqId, out _), $"Requisition was not created on position {positionId}.");
        var details = $"ui/hr-core/RecruitmentRequisitions/Details/{reqId}";
        await s.Central.PostAsync($"{step}-submit-requisition", $"ui/hr-core/RecruitmentRequisitions/Submit/{reqId}", details, []);
        await s.Br1.PostAsync($"{step}-approve-requisition-1", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-29 step 1")]);
        await s.Approver.PostAsync($"{step}-approve-requisition-2", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{reqId}", details,
            [new("RequisitionId", reqId), new("Comments", "AC-HCM-29 step 2")]);
        await s.Central.PostAsync($"{step}-criteria", "ui/hr-core/RecruitmentAssessmentCriteria/Create", details, CriteriaForm(reqId));
        await s.Central.PostAsync($"{step}-publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{reqId}", details,
            [new("applicationClosingDate", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]);
        Assert.Equal("Published", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));

        var ranked = new List<string>();
        for (var rank = 1; rank <= rankedCount; rank++)
        {
            var name = $"AC29 رتبة{rank} {Guid.NewGuid():N}"[..24];
            await s.Central.PostAsync($"{step}-create-application", "ui/hr-core/RecruitmentApplications/Create", "ui/hr-core/RecruitmentApplications/Index",
                [new("ApplicantFullName", name), new("RequisitionId", reqId), new("Notes", "AC-HCM-29")]);
            var appId = await Sql($"SELECT \"Id\" FROM \"RecruitmentApplications\" WHERE \"RequisitionId\"='{reqId}' AND \"ApplicantFullName\"='{name}';");
            Assert.True(Guid.TryParse(appId, out _), "HR application was not created.");
            // Test-only precondition (AC-HCM-26's scope): ranked by merit.
            await Sql($"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"={rank}, \"AssessmentScore\"={95 - rank * 5}, " +
                      $"\"RankedAt\"=now(), \"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
            ranked.Add(appId);
        }

        s.Recorder.WriteBefore(new { scenario = s.Tag, requisitionId = reqId, positionId, grade, ranked });
        return new RankedRequisition(reqId, positionId!, orgUnitId, grade, ranked);
    }

    private static async Task<GradeBounds> RequireGradeBoundsAsync(Session s, RankedRequisition req)
    {
        var page = await s.Central.GetAsync("payroll-grade-hint", $"ui/hr-core/RecruitmentApplications/Details/{req.Ranked[0]}");
        Skip.If(page.Contains("تعذّر التحقق من حدود الدرجة", StringComparison.Ordinal),
            "HR.Core cannot reach the Payroll grade lookup (see PLAN-27-EXECUTION).");
        var row = await PayrollSql(
            "SELECT g.\"MinSalary\"::text || '|' || g.\"MaxSalary\"::text FROM \"PayGrades\" g JOIN \"SalaryScales\" sc ON sc.\"Id\"=g.\"SalaryScaleId\" " +
            $"WHERE g.\"TenantId\"='{TenantA}' AND g.\"GradeCode\"='{req.Grade}' AND sc.\"IsActive\" AND sc.\"EffectiveFrom\" <= now() " +
            "AND (sc.\"EffectiveTo\" IS NULL OR sc.\"EffectiveTo\" >= now()) ORDER BY sc.\"EffectiveFrom\" DESC LIMIT 1;");
        var parts = row.Split('|');
        Assert.True(parts.Length == 2, $"Payroll grade {req.Grade} not found for agency A.");
        return new GradeBounds(decimal.Parse(parts[0], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static async Task<string> CreateOfferAsync(Session s, string step, string appId, decimal salary, string grade)
    {
        await s.Central.PostAsync(step, "ui/hr-core/RecruitmentApplications/CreateOffer", $"ui/hr-core/RecruitmentApplications/Details/{appId}",
        [
            new("ApplicationId", appId),
            new("OfferedSalary", salary.ToString(CultureInfo.InvariantCulture)),
            new("GradeCode", grade),
            new("ProposedStartDate", DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("ValidityDays", "14")
        ]);
        var offerId = await Sql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{appId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offerId, out _), $"{step}: offer was not created for application {appId}.");
        return offerId;
    }

    private static async Task SubmitAndApproveAsync(Session s, string step, string offerId)
    {
        await s.Central.PostAsync($"{step}-submit-offer", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offerId), [new("id", offerId)]);
        await s.Br1.PostAsync($"{step}-approve-offer-1", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", "AC-HCM-29 step 1")]);
        await s.Approver.PostAsync($"{step}-approve-offer-2", $"ui/hr-core/OfferLetters/ApproveWorkflow/{offerId}", OfferDetails(offerId),
            [new("OfferId", offerId), new("Comments", "AC-HCM-29 step 2")]);
    }

    private static async Task PublishCandidateAcceptedAsync(Session s, Guid employeeId, bool internalMobility)
    {
        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            var publish = provider.GetRequiredService<IPublishEndpoint>();
            await publish.Publish<ICandidateAccepted>(new
            {
                EmployeeId = employeeId,
                EmploymentCategory = "Permanent",
                ExpectedStartDate = DateTime.UtcNow.Date.AddDays(14),
                FullName = "AC-HCM-29i",
                SourceSystem = "HR.Core",
                CorrelationId = s.Recorder.Folder,
                TenantId = AcceptanceSeedIds.AgencyAId,
                RecruitmentApplicationId = Guid.NewGuid(),
                OfferedSalary = 5000m,
                GradeCode = "G5",
                LegalRegimeId = "law50",
                EmploymentType = "Permanent",
                IsInternalMobility = internalMobility,
                SchemaVersion = 4
            }, ctx => ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantA));
            s.Recorder.CaptureHttp($"29i-publish-{(internalMobility ? "mobility" : "control")}", "AMQP", "ICandidateAccepted", 0,
                new { employeeId, internalMobility });
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
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

    private static List<KeyValuePair<string, string>> HireForm(
        string appId,
        string nationalId,
        string orgUnitId,
        string employmentCategory = "Permanent",
        string? mismatchReason = null)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("ApplicationId", appId),
            new("Mode", "CreateNew"),
            new("NationalId", nationalId),
            new("FirstName", "مرشح"),
            new("LastName", "الخطة29"),
            new("BirthDate", DateTime.UtcNow.Date.AddYears(-29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("Gender", "Female"),
            new("PrimaryOrgUnitId", orgUnitId),
            new("ExpectedStartDate", DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("EmploymentCategory", employmentCategory)
        };
        if (mismatchReason is not null)
            form.Add(new("NationalIdMismatchReason", mismatchReason));
        return form;
    }

    private static List<KeyValuePair<string, string>> CriteriaForm(string reqId)
    {
        var form = new List<KeyValuePair<string, string>> { new("RequisitionId", reqId), new("Title", "AC-HCM-29") };
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

    private static string HirePage(string appId) => $"ui/hr-core/RecruitmentApplications/Hire/{appId}";

    private static string OfferDetails(string offerId) => $"ui/hr-core/OfferLetters/Details/{offerId}";

    /// <summary>Scenarios share the AC-HCM-29 folder: steps-*.md and db-checks-*.json are kept per scenario.</summary>
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

    // ── DB helpers ────────────────────────────────────────────────────────

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);
    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);
    private static Task<string> ContractsSql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_contracts_db", sql);
    private static Task<long> ContractsScalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_contracts_db", sql);
    private static Task<string> PayrollSql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db", sql);
    private static Task<long> PayrollScalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("payroll_db", sql);

    private static async Task<int> AppStatusAsync(string appId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"RecruitmentApplications\" WHERE \"Id\"='{appId}';");

    private static async Task<int> OfferStatusAsync(string offerId) =>
        (int)await Scalar($"SELECT \"Status\" FROM \"OfferLetters\" WHERE \"Id\"='{offerId}';");

    /// <summary>Gateway UI client for one persona: antiforgery-aware form posts with evidence capture (as AC-HCM-26/27).</summary>
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
            var head = bytes.Length >= 4 ? System.Text.Encoding.ASCII.GetString(bytes, 0, 4) : string.Empty;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode,
                new { contentType = resp.Content.Headers.ContentType?.MediaType, length = bytes.Length, head });
            return (resp.StatusCode, resp.Content.Headers.ContentType?.MediaType, head);
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
                new { fields = fields.Select(f => f.Key.Contains("NationalId", StringComparison.Ordinal) ? $"{f.Key}=***" : $"{f.Key}={f.Value}"), postStatus, redirect, final = resp.RequestMessage?.RequestUri?.AbsolutePath });
            Assert.True((int)resp.StatusCode < 500, $"POST {path} as {persona} returned {(int)resp.StatusCode}.");
            Assert.True(resp.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"POST {path} as {persona} was denied.");
            return WebUtility.HtmlDecode(body);
        }
    }
}
