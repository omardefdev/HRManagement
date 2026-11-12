using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-IAM-51a–f (PLAN-51) on the prod-like stack, through the gateway, and straight to HR.Core for the service accounts. a: self-service
/// reaches its own controllers only, whatever other role the employee holds; b: no user reaches an internal route, and the directory is for
/// staff and services; c: the gateway's audit log is for its owners, each agency its own rows; d: the writes name their policy (a reader
/// and the management approver write nothing); e: a service account with <c>hr-core.workflow</c> starts a workflow and writes no employee;
/// f: agency onboarding is the platform administrator's, and <c>apply-file</c> is gone. Nothing stops or recreates a container.
/// </summary>
[RequiredAcceptance]
public class AcIam51ExplicitPolicyTests
{
    private const string Ac = "AC-IAM-51";
    private const string EssSupervisory = "a-ess-supervisory";
    private const string EssApprover = "a-ess-approver";
    private const string Employee = "employee-a";
    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string TenantB = AcceptanceSeedIds.AgencyBId.ToString("D");

    private static Task<string> DbSql(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    /// <summary>A bearer client through the gateway, no cookies, no redirects, its own correlation id.</summary>
    private static async Task<(HttpClient Http, string Correlation)> BearerAsync(string persona)
    {
        var correlation = Guid.NewGuid().ToString("N");
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(AcceptanceHarness.RequirePersona(persona), correlation);
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlation);
        return (http, correlation);
    }

    private static async Task<HttpStatusCode> StatusAsync(EvidenceRecorder r, string step, string persona, HttpMethod method, string path)
    {
        var (http, _) = await BearerAsync(persona);
        using (http)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
                request.Content = new FormUrlEncodedContent([new("x", "1")]);
            using var response = await http.SendAsync(request);
            r.CaptureHttp(step, method.Method, "/" + path, (int)response.StatusCode);
            return response.StatusCode;
        }
    }

    // ---------------------------------------------------------------- 51a

    private static readonly Regex Stylesheet = new("href=\"(/ui/(?:ess|hr-core)/[^\"?]+\\.css)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [SkippableFact]
    public async Task Ac51a_self_service_reaches_its_own_controllers_only_whatever_else_the_employee_holds()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "51a", "AC-IAM-51a: /ui/ess passes the self-service controllers only; an employee with a read or approval role reaches nothing else through it",
            EssSupervisory, EssApprover, Employee, "central-a");
        s.Steps("""
            1. a-ess-supervisory (hr-employee + hr-core-supervisory), a-ess-approver (hr-employee + hr-management-approver) and employee-a open
               /ui/ess/EssPortal and /ui/hr-core/EssPortal (200) and a stylesheet each page links (200).
            2. Negative: a-ess-supervisory POST /ui/ess/Shifts/Create → 404 (Denied:EssNotExposed in the gateway's audit), POST
               /ui/hr-core/Shifts/Create/Ess → 403; no Shift row is added (psql).
            3. Negative: a-ess-approver POST /ui/ess/Delegations/Create and /ui/ess/EmployeeCorrections/Approve/{id} → 404; the correction
               central-a requested stays Pending.
            4. The approver's own path (PLAN-50) still works: a-ess-approver approves the same correction on /ui/hr-core/EmployeeCorrections/Approve.
            """);

        var pages = new List<object>();
        foreach (var persona in new[] { EssSupervisory, EssApprover, Employee })
        {
            foreach (var path in new[] { "ui/ess/EssPortal", "ui/hr-core/EssPortal" })
            {
                var body = await s[persona].GetAsync($"51a-{persona}-{path.Replace('/', '-')}", path);
                var css = Stylesheet.Match(body);
                Assert.True(css.Success, $"51a: {persona} {path}: the page links no stylesheet of its segment");
                var (http, _) = await BearerAsync(persona);
                using (http)
                {
                    using var asset = await http.GetAsync(css.Groups[1].Value.TrimStart('/'));
                    Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
                    pages.Add(new { persona, path, stylesheet = css.Groups[1].Value, status = (int)asset.StatusCode });
                }
            }
        }

        var shiftsBefore = await Sql("SELECT count(*)::text FROM \"Shifts\";");
        var (essHttp, essCorrelation) = await BearerAsync(EssSupervisory);
        HttpStatusCode essCreate;
        using (essHttp)
        {
            using var response = await essHttp.PostAsync("ui/ess/Shifts/Create", new FormUrlEncodedContent([new("Code", "IAM51-ESS"), new("NameAr", "x")]));
            essCreate = response.StatusCode;
            s.Recorder.CaptureHttp("51a-ess-shifts-create", "POST", "/ui/ess/Shifts/Create", (int)essCreate);
        }

        var hrCoreCreate = await StatusAsync(s.Recorder, "51a-hr-core-shifts-create-ess", EssSupervisory, HttpMethod.Post, "ui/hr-core/Shifts/Create/Ess");
        Assert.Equal(HttpStatusCode.NotFound, essCreate);
        Assert.Equal(HttpStatusCode.Forbidden, hrCoreCreate);
        Assert.Equal(shiftsBefore, await Sql("SELECT count(*)::text FROM \"Shifts\";"));
        var decision = await UntilAuditAsync(essCorrelation, "\"Decision\"");
        Assert.Equal(GatewayEssOptions.NotExposedDecision, decision);

        var employee = await Sql($"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA}' AND \"CurrentStatus\"=0 ORDER BY \"NationalId\", \"Id\" OFFSET 13 LIMIT 1;");
        var middle = await Sql($"SELECT COALESCE(\"MiddleName\", '') FROM \"Employees\" WHERE \"Id\"='{employee}';");
        var reason = $"AC-IAM-51a {Guid.NewGuid():N}"[..30];
        await s["central-a"].PostAsync("51a-correction", "ui/hr-core/EmployeeCorrections/RequestCorrection", "ui/hr-core/Delegations/Create",
        [
            new("employeeId", employee), new("reason", reason),
            new("changesJson", JsonSerializer.Serialize(new Dictionary<string, string> { ["MiddleName"] = middle }))
        ]);
        var correction = await Sql($"SELECT \"Id\" FROM \"EmployeeCorrections\" WHERE \"Reason\"='{reason}';");
        Assert.True(Guid.TryParse(correction, out _), "51a: the correction was not requested.");

        var delegationsBefore = await Sql("SELECT count(*)::text FROM \"ApprovalDelegations\";");
        var essDelegation = await StatusAsync(s.Recorder, "51a-ess-delegations-create", EssApprover, HttpMethod.Post, "ui/ess/Delegations/Create");
        var essApprove = await StatusAsync(s.Recorder, "51a-ess-correction-approve", EssApprover, HttpMethod.Post, $"ui/ess/EmployeeCorrections/Approve/{correction}");
        Assert.Equal(HttpStatusCode.NotFound, essDelegation);
        Assert.Equal(HttpStatusCode.NotFound, essApprove);
        Assert.Equal(delegationsBefore, await Sql("SELECT count(*)::text FROM \"ApprovalDelegations\";"));
        Assert.Equal("0", await Sql($"SELECT \"Status\"::text FROM \"EmployeeCorrections\" WHERE \"Id\"='{correction}';"));

        await s[EssApprover].PostAsync("51a-own-path-approve", $"ui/hr-core/EmployeeCorrections/Approve/{correction}", "ui/hr-core/Delegations/Create", []);
        Assert.Equal("1", await Sql($"SELECT \"Status\"::text FROM \"EmployeeCorrections\" WHERE \"Id\"='{correction}';"));

        s.Checks(new
        {
            pages,
            essShiftsCreate = (int)essCreate,
            essAuditDecision = decision,
            hrCoreShiftsCreateEss = (int)hrCoreCreate,
            essDelegationsCreate = (int)essDelegation,
            essCorrectionApprove = (int)essApprove,
            correction,
            approvedOnOwnPath = true
        });
        s.Recorder.WriteResult("Pass");
    }

    /// <summary>A column of the gateway's audit row of a correlation id, once the queue has written it (within 30 s).</summary>
    private static async Task<string> UntilAuditAsync(string correlation, string column)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var value = await DbSql("gateway_audit_db",
                $"SELECT COALESCE({column}::text, '') FROM gateway_audit_entries WHERE \"CorrelationId\"='{correlation}' ORDER BY \"Id\" DESC LIMIT 1;");
            if (!string.IsNullOrEmpty(value) || DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
                return value;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    // ---------------------------------------------------------------- 51b

    [SkippableFact]
    public async Task Ac51b_no_user_reaches_an_internal_route_and_the_directory_is_for_staff_and_services()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "51b", "AC-IAM-51b: lifecycle flags for the service only; the directory for staff and services, not the employee or the marker",
            Employee, "a-exam-marker", "a-payroll-poster");
        s.Steps("""
            1. svc-hr-contracts (hr-core.lookup) reads a colleague's lifecycle flags straight from HR.Core → 200.
            2. Negative: employee-a → /ui/ess/api/internal/employees/{colleague}/lifecycle-flags → 404, /api/hr-core/api/internal/... → 403.
            3. Negative: employee-a and a-exam-marker → /api/hr-core/api/lookup/employees?q= → 403. a-payroll-poster → the same picker → 200.
            """);
        var colleague = await Sql($"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA}' AND \"CurrentStatus\"=0 ORDER BY \"NationalId\", \"Id\" OFFSET 3 LIMIT 1;");
        using var service = await AcceptanceHarness.CreateServiceClientAsync(
            ServiceClientIds.HrContracts, ServiceScopes.HrCoreLookup, s.Recorder, Guid.Parse(TenantA));
        using var flags = await service.GetAsync($"api/internal/employees/{colleague}/lifecycle-flags");
        s.Recorder.CaptureHttp("51b-service-flags", "GET", "/api/internal/employees/{id}/lifecycle-flags", (int)flags.StatusCode);
        Assert.Equal(HttpStatusCode.OK, flags.StatusCode);

        var essFlags = await StatusAsync(s.Recorder, "51b-employee-ess-flags", Employee, HttpMethod.Get, $"ui/ess/api/internal/employees/{colleague}/lifecycle-flags");
        var apiFlags = await StatusAsync(s.Recorder, "51b-employee-api-flags", Employee, HttpMethod.Get, $"api/hr-core/api/internal/employees/{colleague}/lifecycle-flags");
        var employeeLookup = await StatusAsync(s.Recorder, "51b-employee-lookup", Employee, HttpMethod.Get, "api/hr-core/api/lookup/employees?q=a");
        var markerLookup = await StatusAsync(s.Recorder, "51b-marker-lookup", "a-exam-marker", HttpMethod.Get, "api/hr-core/api/lookup/employees?q=a");
        var posterLookup = await StatusAsync(s.Recorder, "51b-poster-lookup", "a-payroll-poster", HttpMethod.Get, "api/hr-core/api/lookup/employees?q=a");
        Assert.Equal(HttpStatusCode.NotFound, essFlags);
        Assert.Equal(HttpStatusCode.Forbidden, apiFlags);
        Assert.Equal(HttpStatusCode.Forbidden, employeeLookup);
        Assert.Equal(HttpStatusCode.Forbidden, markerLookup);
        Assert.Equal(HttpStatusCode.OK, posterLookup);

        s.Checks(new
        {
            serviceFlags = (int)flags.StatusCode,
            employeeEssFlags = (int)essFlags,
            employeeApiFlags = (int)apiFlags,
            employeeLookup = (int)employeeLookup,
            markerLookup = (int)markerLookup,
            posterLookup = (int)posterLookup
        });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 51c

    private static async Task<int> AuditSearchCountAsync(EvidenceRecorder r, string step, string persona, string correlation)
    {
        var (http, _) = await BearerAsync(persona);
        using (http)
        {
            using var response = await http.GetAsync($"ops/audit-search?correlationId={correlation}&timeRange=1h");
            r.CaptureHttp(step, "GET", "/ops/audit-search", (int)response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("items").GetArrayLength();
        }
    }

    [SkippableFact]
    public async Task Ac51c_the_gateways_audit_is_for_its_owners_and_each_agency_sees_its_own_rows()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "51c", "AC-IAM-51c: b-auditor sees agency B only, platform-admin both; employee-a and central-a are refused",
            "b-auditor", "platform-admin", Employee, "central-a");
        s.Steps("""
            1. Setup: one request as a persona of agency A (central-a) and one as a persona of agency B (b-auditor), each with its own
               correlation id; psql shows both rows in gateway_audit_entries with their agency.
            2. b-auditor: /Home/Audit (200); /ops/audit-search finds agency B's correlation and not agency A's.
            3. platform-admin: /ops/audit-search finds both.
            4. Negative: employee-a and central-a → /Home/Audit and /ops/audit-search → 403.
            """);

        var (aHttp, aCorrelation) = await BearerAsync("central-a");
        using (aHttp)
        using (await aHttp.GetAsync("ui/hr-core/Employees")) { }
        var (bHttp, bCorrelation) = await BearerAsync("b-auditor");
        using (bHttp)
        using (await bHttp.GetAsync("api/hr-core/api/lookup/employees?q=x")) { }

        Assert.Equal(TenantA, await UntilAuditAsync(aCorrelation, "\"TenantId\""));
        Assert.Equal(TenantB, await UntilAuditAsync(bCorrelation, "\"TenantId\""));

        var auditPage = await StatusAsync(s.Recorder, "51c-b-auditor-audit-page", "b-auditor", HttpMethod.Get, "Home/Audit");
        Assert.Equal(HttpStatusCode.OK, auditPage);
        var bSeesB = await AuditSearchCountAsync(s.Recorder, "51c-b-auditor-own", "b-auditor", bCorrelation);
        var bSeesA = await AuditSearchCountAsync(s.Recorder, "51c-b-auditor-other", "b-auditor", aCorrelation);
        var platformA = await AuditSearchCountAsync(s.Recorder, "51c-platform-a", "platform-admin", aCorrelation);
        var platformB = await AuditSearchCountAsync(s.Recorder, "51c-platform-b", "platform-admin", bCorrelation);
        Assert.True(bSeesB >= 1, "51c: b-auditor does not see agency B's row");
        Assert.Equal(0, bSeesA);
        Assert.True(platformA >= 1 && platformB >= 1, $"51c: platform-admin sees A {platformA}, B {platformB}");

        // The gateway's own pages refuse the way its cookie scheme does: 403, or 302 to its access-denied page (never to the page itself).
        var refused = new Dictionary<string, string>();
        foreach (var persona in new[] { Employee, "central-a" })
        {
            foreach (var path in new[] { "Home/Audit", "ops/audit-search" })
            {
                var (http, _) = await BearerAsync(persona);
                using (http)
                {
                    using var response = await http.GetAsync(path);
                    var location = response.Headers.Location?.ToString() ?? string.Empty;
                    s.Recorder.CaptureHttp($"51c-{persona}-{path.Replace('/', '-')}", "GET", "/" + path, (int)response.StatusCode);
                    refused[$"{persona} {path}"] = $"{(int)response.StatusCode} {location}".Trim();
                    Assert.True(response.StatusCode == HttpStatusCode.Forbidden
                                || (response.StatusCode == HttpStatusCode.Found && location.Contains("/Account/AccessDenied", StringComparison.Ordinal)),
                        $"51c: {persona} on {path} answered {(int)response.StatusCode} {location}");
                }
            }
        }

        s.Checks(new { aCorrelation, bCorrelation, auditPage = (int)auditPage, bSeesB, bSeesA, platformA, platformB, refused });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 51d

    [SkippableFact]
    public async Task Ac51d_writes_name_their_policy_a_reader_and_the_approver_write_nothing()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "51d", "AC-IAM-51d: central-a creates a shift; a-supervisory-reader and a-management-approver are refused on writes",
            "central-a", "a-supervisory-reader", "a-management-approver");
        s.Steps("""
            1. central-a creates a Shift through the gateway (the row exists, psql).
            2. Negative: a-supervisory-reader POST /ui/hr-core/Shifts/Create → 403; a-management-approver POST /ui/hr-contracts/Contracts/Create
               → 403. No row is added.
            """);
        var code = $"I51{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await s["central-a"].PostAsync("51d-shift", "ui/hr-core/Shifts/Create", "ui/hr-core/Shifts/Create",
        [
            new("Code", code), new("NameAr", "وردية اختبار AC-IAM-51"), new("ShiftType", "0"),
            new("StartTime", "08:00"), new("EndTime", "16:00"), new("IsActive", "true")
        ]);
        Assert.Equal("1", await Sql($"SELECT count(*)::text FROM \"Shifts\" WHERE \"Code\"='{code}';"));

        var shiftsBefore = await Sql("SELECT count(*)::text FROM \"Shifts\";");
        var contractsBefore = await DbSql("hr_contracts_db", "SELECT count(*)::text FROM \"Contracts\";");
        var reader = await StatusAsync(s.Recorder, "51d-reader-shift", "a-supervisory-reader", HttpMethod.Post, "ui/hr-core/Shifts/Create");
        var approver = await StatusAsync(s.Recorder, "51d-approver-contract", "a-management-approver", HttpMethod.Post, "ui/hr-contracts/Contracts/Create");
        Assert.Equal(HttpStatusCode.Forbidden, reader);
        Assert.Equal(HttpStatusCode.Forbidden, approver);
        Assert.Equal(shiftsBefore, await Sql("SELECT count(*)::text FROM \"Shifts\";"));
        Assert.Equal(contractsBefore, await DbSql("hr_contracts_db", "SELECT count(*)::text FROM \"Contracts\";"));

        s.Checks(new { shift = code, readerShift = (int)reader, approverContract = (int)approver });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 51e

    [SkippableFact]
    public async Task Ac51e_a_workflow_service_account_starts_a_custody_workflow_and_writes_no_employee()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "51e", "AC-IAM-51e: svc-admin-logistics starts a custody workflow and cannot create an employee", "a-logistics-maker");
        s.Steps("""
            1. svc-admin-logistics (hr-core.workflow) starts an asset assignment approval on POST api/workflow-instances straight to HR.Core
               (201); the instance exists (psql) and is cancelled again by its owner (cleanup).
            2. Negative: the same service account POST /Employees/Create → 403; the employee count does not move.
            """);
        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        var employee = Guid.Parse(await Sql($"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA}' AND \"CurrentStatus\"=0 ORDER BY \"NationalId\", \"Id\" OFFSET 15 LIMIT 1;"));
        var assignment = Guid.NewGuid();
        // As Admin.Logistics starts one: the requester travels in X-Acting-User (AC-WF-43e does the same).
        var (makerToken, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona("a-logistics-maker"));
        var maker = Guid.Parse(AcceptanceHarness.ParseJwt(makerToken).Subject);
        using var logistics = await AcceptanceHarness.CreateServiceClientAsync(
            ServiceClientIds.AdminLogistics, ServiceScopes.HrCoreWorkflow, s.Recorder, AcceptanceSeedIds.AgencyAId, maker);
        using var start = await logistics.PostAsJsonAsync("api/workflow-instances", new WorkflowApprovalStartRequest(
            "AssetAssignment",
            assignment,
            "ASSET-ASSIGN-APPROVAL",
            $"AssetAssignment:{assignment:D}",
            [],
            null,
            "AC-IAM-51e",
            $"/ui/admin-logistics/Assets/Details/{asset:D}",
            new Dictionary<string, string> { ["assetId"] = asset.ToString("D"), ["employeeId"] = employee.ToString("D") },
            employee));
        var body = await start.Content.ReadAsStringAsync();
        s.Recorder.CaptureHttp("51e-start", "POST", "/api/workflow-instances", (int)start.StatusCode);
        Assert.True(start.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"51e: start answered {(int)start.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        var instance = doc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("1", await Sql($"SELECT count(*)::text FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));

        var employeesBefore = await Sql("SELECT count(*)::text FROM \"Employees\";");
        using var create = await logistics.PostAsync("Employees/Create", new FormUrlEncodedContent(
            [new("FirstName", "AC"), new("LastName", "IAM51"), new("NationalId", "51051051051")]));
        s.Recorder.CaptureHttp("51e-service-employee-create", "POST", "/Employees/Create", (int)create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(employeesBefore, await Sql("SELECT count(*)::text FROM \"Employees\";"));

        using var cancel = await logistics.PostAsJsonAsync($"api/workflow-instances/{instance:D}/cancel", new { reason = "AC-IAM-51e cleanup" });
        s.Recorder.CaptureHttp("51e-cleanup-cancel", "POST", "/api/workflow-instances/{id}/cancel", (int)cancel.StatusCode);

        s.Checks(new { instance, start = (int)start.StatusCode, serviceEmployeeCreate = (int)create.StatusCode, cleanupCancel = (int)cancel.StatusCode });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 51f

    [SkippableFact]
    public async Task Ac51f_agency_onboarding_is_the_platform_administrators_and_reads_no_file()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "51f", "AC-IAM-51f: platform-admin dry-runs an onboarding (200, audited); central-a refused; apply-file gone",
            "platform-admin", "central-a");
        s.Steps("""
            1. platform-admin POST /api/hr-core/api/onboarding/agencies/apply?dryRun=true with the acceptance manifest → 200, and the gateway's
               audit holds the request.
            2. Negative: central-a (hr-core) → the same request → 403.
            3. Negative: apply-file → 404 for central-a, whom the gateway lets through to HR.Core, which has no such route any more.
            """);
        var manifest = await File.ReadAllTextAsync(Path.Combine(AcceptanceHarness.RepoRoot, "seed-packs", "examples", "agency-acceptance-test.json"));
        const string apply = "api/hr-core/api/onboarding/agencies/apply?dryRun=true";

        var (platform, platformCorrelation) = await BearerAsync("platform-admin");
        HttpStatusCode platformStatus;
        using (platform)
        {
            using var response = await platform.PostAsync(apply, new StringContent(manifest, Encoding.UTF8, "application/json"));
            platformStatus = response.StatusCode;
            s.Recorder.CaptureHttp("51f-platform-apply", "POST", "/" + apply, (int)platformStatus);
        }

        Assert.Equal(HttpStatusCode.OK, platformStatus);
        Assert.Equal("200", await UntilAuditAsync(platformCorrelation, "\"StatusCode\""));

        var (central, _) = await BearerAsync("central-a");
        HttpStatusCode centralStatus;
        HttpStatusCode fileStatus;
        using (central)
        {
            using (var response = await central.PostAsync(apply, new StringContent(manifest, Encoding.UTF8, "application/json")))
                centralStatus = response.StatusCode;
            using (var response = await central.PostAsync("api/hr-core/api/onboarding/agencies/apply-file?path=seed-packs/examples/agency-acceptance-test.json&dryRun=true",
                       new StringContent(string.Empty)))
                fileStatus = response.StatusCode;
        }

        s.Recorder.CaptureHttp("51f-central-apply", "POST", "/" + apply, (int)centralStatus);
        s.Recorder.CaptureHttp("51f-central-apply-file", "POST", "/api/hr-core/api/onboarding/agencies/apply-file", (int)fileStatus);
        Assert.Equal(HttpStatusCode.Forbidden, centralStatus);
        Assert.Equal(HttpStatusCode.NotFound, fileStatus);

        s.Checks(new { platformDryRun = (int)platformStatus, platformCorrelation, centralApply = (int)centralStatus, applyFile = (int)fileStatus });
        s.Recorder.WriteResult("Pass");
    }

    /// <summary>The persona file holds the two personas of this plan, each with an employee of agency A and the two roles it needs.</summary>
    [Fact]
    public void The_two_personas_of_the_plan_are_in_the_catalog()
    {
        foreach (var (id, role) in new[] { (EssSupervisory, HrManagementRoles.HrCoreSupervisory), (EssApprover, HrManagementRoles.HrManagementApprover) })
        {
            var persona = AcceptancePersonaCatalog.Personas.Single(p => p.Id == id);
            Assert.Equal(TenantA, persona.StorageTenantId.ToString("D"));
            Assert.NotNull(persona.Employee);
            Assert.Contains(HrManagementRoles.HrEmployee, persona.Definition.Roles);
            Assert.Contains(role, persona.Definition.Roles);
        }
    }
}
