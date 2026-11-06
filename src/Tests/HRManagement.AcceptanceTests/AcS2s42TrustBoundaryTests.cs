using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-42 (AC-S2S-42a..i) on the prod-like stack with docker-compose.acceptance-s2s42.yml: remote enforcement fails closed, the enforce
/// endpoint is for services, the gateway proof cannot be forged or moved, a placeholder stops the boot, erasing has its own scope,
/// official numbers belong to their owners, and the legal-hold gate refuses when it cannot check. It stops hr-core-web and recreates
/// containers, so it runs alone, last on its stack (the s2s42 group of tests/acceptance/ci-scope.json).
/// </summary>
[RequiredAcceptance]
[Collection(AcS2s42Collection.Name)]
public class AcS2s42TrustBoundaryTests
{
    private const string Ac = "AC-S2S-42";
    private const string DeniedCategory = "اختبار-رفض";
    private const string Logistics = "svc-admin-logistics";
    private const int InStorage = 1; // AssetStatus.InStorage, stored as a number
    private const int ActiveEmployee = 0; // EmploymentStatus.Active, stored as a number
    private const string PendingApproval = "0"; // AssetAssignmentStatus.PendingApproval, stored as a number
    private static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    private static readonly Guid TenantB = AcceptanceSeedIds.AgencyBId;
    private static readonly string[] ComposeFiles =
    [
        "docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml", "docker-compose.full-stack-gateway.yml",
        "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml", "docker-compose.acceptance-s2s42.yml"
    ];

    private static readonly string HrCoreDirect =
        (Environment.GetEnvironmentVariable("HR_CORE_DIRECT_URL") ?? "http://localhost:6210").TrimEnd('/') + "/";

    private const string LogisticsDirect = "http://localhost:6290/";

    // ------------------------------------------------------------------------------------------------ helpers

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<string> Sql(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    private static Task<long> Count(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);

    private static async Task<(int Exit, string Output)> ComposeAsync(string assetsMode, params string[] arguments)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AcceptanceHarness.RepoRoot
        };
        foreach (var argument in new[] { "compose", "-p", "hrm-acceptance" })
            psi.ArgumentList.Add(argument);
        foreach (var file in ComposeFiles)
        {
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(file);
        }

        foreach (var argument in new[] { "--profile", "full-stack" }.Concat(arguments))
            psi.ArgumentList.Add(argument);

        psi.Environment["S2S42_ASSETS_MODE"] = assetsMode;
        // The compose files need these; acceptance-local-env.ps1 sets them for a shell, this reads the same git-ignored files.
        psi.Environment["ACCEPTANCE_SVC_CLIENT_SECRET"] = AcceptanceHarness.ResolveServiceClientSecret("svc-any") ?? "";
        var keysPath = Path.Combine(AcceptanceHarness.RepoRoot, ".acceptance", "gateway-proof-keys.json");
        if (File.Exists(keysPath))
        {
            using var keys = JsonDocument.Parse(await File.ReadAllTextAsync(keysPath));
            psi.Environment["ACCEPTANCE_GATEWAY_PROOF_PRIVATE_KEY_B64"] = keys.RootElement.GetProperty("privateKeyB64").GetString();
            psi.Environment["ACCEPTANCE_GATEWAY_PROOF_PUBLIC_KEY_B64"] = keys.RootElement.GetProperty("publicKeyB64").GetString();
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }

    /// <summary>The acceptance services have no container health check, so ready means /health/ready answers.</summary>
    private static async Task WaitReadyAsync(string baseUrl, string service)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(5);
        do
        {
            try
            {
                using var response = await http.GetAsync(baseUrl + "health/ready");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Still starting.
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        } while (DateTime.UtcNow < deadline);

        Assert.Fail($"{service} is not ready.");
    }

    /// <summary>The job recreates these two just before this class, and nothing else waits for them.</summary>
    private static async Task StackReadyAsync()
    {
        await WaitReadyAsync(HrCoreDirect, "hr-core-web");
        await WaitReadyAsync(LogisticsDirect, "admin-logistics-web");
    }

    private static async Task<string> CurrentModeAsync(string service)
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service);
        var (_, environment) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "inspect", "-f", "{{range .Config.Env}}{{println .}}{{end}}", container);
        return Regex.Match(environment, @"(?m)^PolicyEngine__Assets\.Custody=(\w+)").Groups[1].Value;
    }

    /// <summary>hr-core-web and admin-logistics-web with this custody mode, ready. The seed publishes the deny rule on their start.</summary>
    private static async Task SetAssetsModeAsync(string mode)
    {
        if (await CurrentModeAsync("hr-core-web") == mode && await CurrentModeAsync("admin-logistics-web") == mode)
            return;

        var (exit, output) = await ComposeAsync(mode, "up", "-d", "--no-deps", "--no-build", "--force-recreate", "hr-core-web", "admin-logistics-web");
        Assert.True(exit == 0, output);
        await WaitReadyAsync(HrCoreDirect, "hr-core-web");
        await WaitReadyAsync(LogisticsDirect, "admin-logistics-web");
    }

    private static async Task StopAsync(string service)
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service);
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "stop", container);
        Assert.True(exit == 0, output);
    }

    private static async Task StartAsync(string service)
    {
        // ps -a: the helper's lookup sees running containers only, and this one is stopped.
        var (_, names) = await AcceptanceMessagingHelper.RunProcessAsync(
            "docker", "ps", "-a",
            "--filter", "label=com.docker.compose.project=hrm-acceptance",
            "--filter", $"label=com.docker.compose.service={service}",
            "--format", "{{.Names}}");
        var container = names.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).First();
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "start", container);
        Assert.True(exit == 0, output);
        await WaitReadyAsync(HrCoreDirect, service);
    }

    private static async Task<HttpResponseMessage> PostUiFormAsync(HttpClient http, string pagePath, string actionPath, IDictionary<string, string>? fields = null)
    {
        var page = await http.GetAsync(pagePath);
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {pagePath} -> {(int)page.StatusCode}");
        var form = new Dictionary<string, string>(fields ?? new Dictionary<string, string>())
        {
            ["__RequestVerificationToken"] = AcMsg40AtomicPublishingTests.RequireAntiforgeryToken(html)
        };
        return await http.PostAsync(actionPath, new FormUrlEncodedContent(form));
    }

    /// <summary>A new undelivered asset of agency A: the setup is SQL, the action under test goes through the gateway.</summary>
    internal static async Task<Guid> InsertAssetAsync(string category, bool privileged = false)
    {
        var id = Guid.NewGuid();
        await Sql("admin_db", $"""
            INSERT INTO "Assets" ("Id","TenantId","TagNumber","Name","Category","Status","IsPrivileged","Value","IsLegacyEntry","LegacyEntryReason","IsCancelled","CreatedAt","UpdatedAt")
            VALUES ('{id:D}','{TenantA:D}','S2S42-{id:N}','أصل اختبار S2S42','{category}',{InStorage},{(privileged ? "true" : "false")},1000,true,'أصل اختبار قبول PLAN-42',false,now(),now());
            SELECT 1;
            """);
        return id;
    }

    private static Task<long> AssignmentsAsync(Guid asset) =>
        Count("admin_db", $"SELECT COUNT(*) FROM \"AssetAssignments\" WHERE \"AssetId\"='{asset:D}';");

    private static async Task<Guid> EmployeeOfLogisticsScopeAsync()
    {
        var text = await Sql("hr_core_db",
            $"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA:D}' AND \"CurrentStatus\"={ActiveEmployee} ORDER BY \"CreatedAt\" LIMIT 1;");
        Assert.True(Guid.TryParse(text, out var id), "No active employee in agency A.");
        return id;
    }

    internal static async Task<(HttpResponseMessage Response, string Error)> AssignAsync(HttpClient http, Guid asset, Guid employee, string? evidence = null)
    {
        var fields = new Dictionary<string, string> { ["employeeId"] = employee.ToString("D"), ["handoverCondition"] = "Good" };
        if (evidence is not null)
            fields["evidenceRef"] = evidence;
        var details = $"ui/admin-logistics/Assets/Details/{asset:D}";
        var response = await PostUiFormAsync(http, details, $"ui/admin-logistics/Assets/AssignToEmployee/{asset:D}", fields);
        var page = await http.GetAsync(details);
        return (response, WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
    }

    /// <summary>The alert boxes of a page, for a failure message.</summary>
    internal static string Alerts(string html) =>
        string.Join(" | ", Regex.Matches(html, @"class=""[^""]*\balert\b[^""]*""[^>]*>(.*?)</div>", RegexOptions.Singleline)
            .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", " ").Trim()));

    /// <summary>A name=value jar, as a browser keeps: a CookieContainer does not send a Secure cookie over http://localhost, and the flash message lives in one.</summary>
    private sealed class CookieJar : DelegatingHandler
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookies.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            var response = await base.SendAsync(request, cancellationToken);
            if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                return response;

            foreach (var header in setCookies)
            {
                var pair = header.Split(';', 2)[0].Split('=', 2);
                if (pair.Length != 2)
                    continue;
                if (pair[1].Length == 0 || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                    _cookies.Remove(pair[0].Trim());
                else
                    _cookies[pair[0].Trim()] = pair[1].Trim();
            }

            return response;
        }
    }

    internal static async Task<HttpClient> UiClientAsync(string persona)
    {
        var correlation = Guid.NewGuid().ToString("D");
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(AcceptanceHarness.RequirePersona(persona), correlation);
        var http = new HttpClient(new CookieJar { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false } })
        {
            BaseAddress = new Uri(HRManagement.E2E.E2eEnv.BaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlation);
        return http;
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static PolicyEnforceRequest AssignmentRequest(Guid entity) => new()
    {
        Domain = PolicyDomains.AssetsCustody,
        SubjectType = "AssetAssignment",
        Action = "Assign",
        EntityId = entity,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["isPrivileged"] = "true",
            ["value"] = "15000",
            ["action"] = "Assign"
        }
    };

    private static async Task<Guid> PersonaIdAsync(string persona)
    {
        var (token, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona(persona));
        return Guid.Parse(AcceptanceHarness.ParseJwt(token).Subject);
    }

    // ------------------------------------------------------------------------------------------------ 42a

    [SkippableFact]
    public async Task Ac42a_Enforce_a_denial_stays_a_denial_and_an_outage_blocks_with_a_clear_message()
    {
        var r = Recorder("42a", "Enforce: an allowed handover, a rule's denial, and an outage that blocks", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        await SetAssetsModeAsync("Enforce");
        using var http = await UiClientAsync("a-logistics-maker");
        var employee = await EmployeeOfLogisticsScopeAsync();

        var plain = await InsertAssetAsync("حواسيب");
        var (allowed, allowedPage) = await AssignAsync(http, plain, employee);
        r.CaptureHttp("assign-allowed", "POST", $"/ui/admin-logistics/Assets/AssignToEmployee/{plain:D}", (int)allowed.StatusCode);
        Assert.True(await AssignmentsAsync(plain) == 1, Alerts(allowedPage));
        var assignment = await Sql("admin_db", $"SELECT \"Id\" FROM \"AssetAssignments\" WHERE \"AssetId\"='{plain:D}';");
        Assert.Equal(1, await Count("hr_core_db",
            $"SELECT COUNT(*) FROM \"PolicyEnforcementLedgers\" WHERE \"EntityId\"='{assignment}' AND \"Mode\"='Enforce' AND \"CallerClientId\"='{Logistics}';"));

        var denied = await InsertAssetAsync(DeniedCategory);
        var (_, deniedPage) = await AssignAsync(http, denied, employee);
        Assert.Contains("ACC-S2S42-DENY", deniedPage, StringComparison.Ordinal);
        Assert.Equal(0, await AssignmentsAsync(denied));

        var outage = await InsertAssetAsync("حواسيب");
        var statusBefore = await Sql("admin_db", $"SELECT \"Status\" FROM \"Assets\" WHERE \"Id\"='{outage:D}';");
        await StopAsync("hr-core-web");
        string outagePage;
        try
        {
            (_, outagePage) = await AssignAsync(http, outage, employee);
        }
        finally
        {
            await StartAsync("hr-core-web");
        }

        Assert.Contains("تعذّر التحقق من سياسة Assets.Custody", outagePage, StringComparison.Ordinal);
        Assert.Equal(0, await AssignmentsAsync(outage));
        Assert.Equal(statusBefore, await Sql("admin_db", $"SELECT \"Status\" FROM \"Assets\" WHERE \"Id\"='{outage:D}';"));

        r.WriteScenarioDbChecks(new { plain, assignment, denied, deniedAssignments = 0, outage, outageAssignments = 0, statusBefore });
        r.WriteScenarioSteps(
            "1. Enforce on both sides. a-logistics-maker hands over a normal asset: one assignment, one ledger row with Mode Enforce, CallerClientId svc-admin-logistics and EntityId = the assignment.\n" +
            "2. Negative: an asset of the denied category: the page shows the rule's reason, no assignment.\n" +
            "3. Negative: hr-core-web stopped: the page shows the failure message, no assignment, the asset is unchanged (psql).");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42b

    [SkippableFact]
    public async Task Ac42b_Shadow_changes_nothing_and_an_outage_does_not_block()
    {
        var r = Recorder("42b", "Shadow: the Off behaviour with a Shadow ledger row, and an outage that only warns", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        await SetAssetsModeAsync("Shadow");
        try
        {
            using var http = await UiClientAsync("a-logistics-maker");
            var employee = await EmployeeOfLogisticsScopeAsync();

            // A privileged asset in the denied category: the server would deny, and Off asks for evidence and the old custody path.
            var privileged = await InsertAssetAsync(DeniedCategory, privileged: true);
            var (_, withoutEvidence) = await AssignAsync(http, privileged, employee);
            Assert.Equal(0, await AssignmentsAsync(privileged));
            Assert.Contains("دليل", withoutEvidence, StringComparison.Ordinal);

            var (_, _) = await AssignAsync(http, privileged, employee, evidence: "AC-S2S-42b");
            Assert.Equal(1, await AssignmentsAsync(privileged));
            var status = await Sql("admin_db", $"SELECT \"Status\" FROM \"AssetAssignments\" WHERE \"AssetId\"='{privileged:D}';");
            Assert.Equal(PendingApproval, status);
            var assignment = await Sql("admin_db", $"SELECT \"Id\" FROM \"AssetAssignments\" WHERE \"AssetId\"='{privileged:D}';");
            Assert.True(await Count("hr_core_db",
                $"SELECT COUNT(*) FROM \"PolicyEnforcementLedgers\" WHERE \"EntityId\"='{assignment}' AND \"Mode\"='Shadow';") >= 1);

            var plain = await InsertAssetAsync("حواسيب");
            await StopAsync("hr-core-web");
            long assignmentsDuringOutage;
            try
            {
                await AssignAsync(http, plain, employee);
                assignmentsDuringOutage = await AssignmentsAsync(plain);
            }
            finally
            {
                await StartAsync("hr-core-web");
            }

            Assert.Equal(1, assignmentsDuringOutage);
            var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("admin-logistics-web");
            var (_, logs) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "logs", "--since", "10m", container);
            Assert.Contains("policy_client_failure", logs, StringComparison.Ordinal);

            r.WriteScenarioDbChecks(new { privileged, status, assignment, plain, assignmentsDuringOutage, warningInLog = true });
            r.WriteScenarioSteps(
                "1. Shadow on both sides. A privileged asset in the denied category: the handover asks for evidence and goes to PendingApproval as in Off, though the server would deny; a Shadow ledger row exists.\n" +
                "2. Negative: hr-core-web stopped: a normal handover goes on, and admin-logistics-web logs policy_client_failure.");
            r.WriteResult("Pass");
        }
        finally
        {
            await SetAssetsModeAsync("Enforce");
        }
    }

    // ------------------------------------------------------------------------------------------------ 42c

    [SkippableFact]
    public async Task Ac42c_Enforce_is_for_the_owning_service_and_the_server_derives_what_matters()
    {
        var r = Recorder("42c", "The internal enforce endpoint: service token, ownership, spoofed body, allow-list", "b-branch-manager");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        await SetAssetsModeAsync("Enforce");
        var actor = await PersonaIdAsync("b-branch-manager");
        using var service = await AcceptanceHarness.CreateServiceClientAsync(Logistics, ServiceScopes.HrCoreWorkflow, r, TenantB, actor);

        var entity = Guid.NewGuid();
        var spoofed = AssignmentRequest(entity);
        spoofed.ActorUserId = Guid.NewGuid();
        spoofed.ActorUserName = "spoofed";
        spoofed.Mode = PolicyEnforcementMode.Off;
        var ok = await service.PostAsync("api/internal/policies/enforce", Json(spoofed));
        var body = await ok.Content.ReadAsStringAsync();
        r.CaptureHttp("enforce-service", "POST", "/api/internal/policies/enforce", (int)ok.StatusCode, JsonSerializer.Deserialize<object>(body));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        // PLAN-43: the decision says an approval is needed and no instance starts here: the owner starts it through Start.
        using (var doc = JsonDocument.Parse(body))
        {
            Assert.Contains(doc.RootElement.GetProperty("decision").GetProperty("effects").EnumerateArray(), e =>
                e.ValueKind == JsonValueKind.String
                    ? e.GetString() == nameof(PolicyEffect.TriggerWorkflow)
                    : e.GetInt32() == (int)PolicyEffect.TriggerWorkflow);
            Assert.True(!doc.RootElement.TryGetProperty("workflowInstanceId", out var none) || none.ValueKind == JsonValueKind.Null);
        }
        var instances = await Sql("hr_core_db", $"SELECT count(*) FROM \"WorkflowInstances\" WHERE \"EntityId\"='{entity:D}';");
        Assert.Equal("0", instances);
        Assert.Equal(actor.ToString("D"), await Sql("hr_core_db", $"SELECT \"ActorUserId\" FROM \"PolicyEnforcementLedgers\" WHERE \"EntityId\"='{entity:D}';"));

        using var user = await UiClientAsync("b-branch-manager");
        var oldRoute = await user.PostAsync("api/hr-core/api/policies/enforce", Json(AssignmentRequest(Guid.NewGuid())));
        var internalRoute = await user.PostAsync("api/hr-core/api/internal/policies/enforce", Json(AssignmentRequest(Guid.NewGuid())));
        r.CaptureHttp("user-old-route", "POST", "/api/hr-core/api/policies/enforce", (int)oldRoute.StatusCode);
        r.CaptureHttp("user-internal-route", "POST", "/api/hr-core/api/internal/policies/enforce", (int)internalRoute.StatusCode);
        Assert.True(oldRoute.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden, $"{(int)oldRoute.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, internalRoute.StatusCode);

        var foreign = await service.PostAsync("api/internal/policies/enforce",
            Json(new PolicyEnforceRequest { Domain = PolicyDomains.ContractsLeave, SubjectType = "LeaveRequest", Action = "Create" }));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var offer = AssignmentRequest(Guid.NewGuid());
        offer.SubjectType = "OfferLetter";
        var wrongSubject = await service.PostAsync("api/internal/policies/enforce", Json(offer));
        Assert.Equal(HttpStatusCode.BadRequest, wrongSubject.StatusCode);

        r.WriteScenarioDbChecks(new { entity, instances, actor, oldRoute = (int)oldRoute.StatusCode, internalRoute = (int)internalRoute.StatusCode, foreign = (int)foreign.StatusCode, wrongSubject = (int)wrongSubject.StatusCode });
        r.WriteScenarioSteps(
            "1. svc-admin-logistics posts enforce for an AssetAssignment with X-Acting-User and a spoofed actor and mode: 200, the TriggerWorkflow effect in the decision, no instance, and the ledger names the acting user.\n" +
            "2. Negative: b-branch-manager: the old route does not exist and the internal one is 403.\n" +
            "3. Negative: svc-admin-logistics on Contracts.Leave: 403. SubjectType OfferLetter: 400.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42d

    private static string HmacV1(string secret, string ts, string method, string path) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}\n{method}\n{path}")));

    private sealed class CapturedProof
    {
        public required string Proof { get; init; }
        public required string Timestamp { get; init; }
        public required string Nonce { get; init; }
    }

    /// <summary>What a proof looks like when the gateway really made it: a request through it, read back from the module's log is not possible, so the test signs nothing itself and replays a header set made for another token from the gateway's own container.</summary>
    private static async Task<(int Status, string Body)> CurlInGatewayAsync(string path, params (string Name, string Value)[] headers)
    {
        var gateway = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-gateway");
        var arguments = new List<string> { "exec", gateway, "curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", "--max-time", "15" };
        foreach (var (name, value) in headers)
        {
            arguments.Add("-H");
            arguments.Add($"{name}: {value}");
        }

        arguments.Add($"http://hr-core-web:8080{path}");
        var (_, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", arguments.ToArray());
        var code = Regex.Match(output, @"\d{3}\s*$");
        return (code.Success ? int.Parse(code.Value.Trim()) : 0, output);
    }

    [SkippableFact]
    public async Task Ac42d_Only_the_gateway_makes_a_proof_and_it_works_once_with_its_token()
    {
        var r = Recorder("42d", "Gateway proof v2: a real request passes; v1, a moved proof and a replay do not", "central-a");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        using var http = await UiClientAsync("central-a");
        var viaGateway = await http.GetAsync("ui/hr-core/");
        r.CaptureHttp("ui-through-gateway", "GET", "/ui/hr-core/", (int)viaGateway.StatusCode);
        Assert.True((int)viaGateway.StatusCode is >= 200 and < 400, $"{(int)viaGateway.StatusCode}");

        const string path = "/api/lookup/org-units";
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (token, _) = await AcceptanceHarness.LoginAsync(persona);
        var (otherToken, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona("b-branch-manager"));

        var noProof = await CurlInGatewayAsync(path, ("Authorization", $"Bearer {token}"));
        Assert.Equal(403, noProof.Status);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var v1 = await CurlInGatewayAsync(path,
            ("Authorization", $"Bearer {token}"),
            (GatewayCallerProof.ProofHeader, HmacV1("old-shared-secret-0123456789abcdef0123", ts, "GET", path)),
            (GatewayCallerProof.TimestampHeader, ts),
            (GatewayCallerProof.PathHeader, path));
        Assert.Equal(403, v1.Status);

        // A real v2 proof: made here with the gateway's own key for this one check, read from the git-ignored key file of the stack.
        var keysPath = Path.Combine(AcceptanceHarness.RepoRoot, ".acceptance", "gateway-proof-keys.json");
        AcceptanceGate.RequireData(r, File.Exists(keysPath), "مفاتيح الإثبات غير موجودة", "No .acceptance/gateway-proof-keys.json.");
        using var keys = JsonDocument.Parse(await File.ReadAllTextAsync(keysPath));
        using var key = GatewayProofKeyLoader.LoadPrivate(keys.RootElement.GetProperty("privateKeyB64").GetString()!);
        using var signed = new HttpRequestMessage(HttpMethod.Get, "http://hr-core-web:8080" + path);
        signed.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        GatewayCallerProof.SignV2(signed, key, path);
        var proof = new[] { GatewayCallerProof.ProofHeader, GatewayCallerProof.TimestampHeader, GatewayCallerProof.NonceHeader, GatewayCallerProof.PathHeader }
            .Select(h => (h, signed.Headers.GetValues(h).Single()))
            .ToArray();

        var moved = await CurlInGatewayAsync(path, [("Authorization", $"Bearer {otherToken}"), .. proof]);
        Assert.Equal(403, moved.Status);

        var first = await CurlInGatewayAsync(path, [("Authorization", $"Bearer {token}"), .. proof]);
        Assert.Equal(200, first.Status);
        var replay = await CurlInGatewayAsync(path, [("Authorization", $"Bearer {token}"), .. proof]);
        Assert.Equal(403, replay.Status);

        r.WriteScenarioDbChecks(new { viaGateway = (int)viaGateway.StatusCode, noProof = noProof.Status, v1 = v1.Status, moved = moved.Status, first = first.Status, replay = replay.Status });
        r.WriteScenarioSteps(
            "1. A UI request through the gateway passes with its v2 proof.\n" +
            "2. Negative, from inside the network to hr-core-web with a valid user token: no proof 403; an HMAC v1 proof 403; a v2 proof made for another token 403.\n" +
            "3. A v2 proof with its own token passes once (200), and the same proof again is 403.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42e

    [SkippableFact]
    public async Task Ac42e_A_placeholder_public_key_stops_the_container_and_names_the_key()
    {
        var r = Recorder("42e", "hr-core-web with the right public key is ready; with a placeholder it exits", "platform");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        await WaitReadyAsync(HrCoreDirect, "hr-core-web");
        using var ready = new HttpClient { BaseAddress = new Uri(HrCoreDirect) };
        var response = await ready.GetAsync("health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var shell = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync(
            shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AcceptanceHarness.RepoRoot, "scripts", "s2s042-placeholder-boot-check.ps1"));

        Assert.True(exit == 0, output);
        Assert.Contains("GatewayCallerProof:PublicKeyPem", output, StringComparison.Ordinal);
        Assert.DoesNotContain("USE_ENV_OR_SECRET_MANAGER", output.Replace("value not shown", ""), StringComparison.Ordinal);

        r.WriteScenarioDbChecks(new { ready = (int)response.StatusCode, placeholderCheck = "exit non-zero, key named, value not printed" });
        r.WriteScenarioSteps(
            "1. hr-core-web with the stack's public key answers /health/ready.\n" +
            "2. Negative: scripts/s2s042-placeholder-boot-check.ps1 runs the image in Production with the placeholder as the public key: the container exits non-zero and the log names the key without the value.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42f

    [SkippableFact]
    public async Task Ac42f_Erasing_needs_its_own_scope()
    {
        var r = Recorder("42f", "svc-compliance erases with dsar.subject.erase; dsar.subject.read alone is refused", "svc-compliance");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        var source = await Sql("hr_core_db",
            $"SELECT \"Id\" FROM \"CitizenApplications\" WHERE \"TenantId\"='{TenantA:D}' AND \"NationalIdHash\" IS NOT NULL AND \"AnonymizedAt\" IS NULL ORDER BY \"CreatedAt\" LIMIT 1;");
        AcceptanceGate.RequireData(r, Guid.TryParse(source, out _), "لا يوجد طلب مواطن", "No citizen application in agency A.");

        // A copy with a random id: the seed's ids share their first eight characters, which the anonymized national id is made of.
        var application = Guid.NewGuid().ToString("D");
        await Sql("hr_core_db", $"""
            CREATE TEMP TABLE s2s42_copy AS SELECT * FROM "CitizenApplications" WHERE "Id"='{source}';
            UPDATE s2s42_copy SET "Id"='{application}', "NationalId"='S2S42-{application[..8]}', "NationalIdHash"=md5('{application}'), "ReferenceNumber"='S2S42-{application[..8]}', "PromotedRecruitmentApplicationId"=NULL;
            INSERT INTO "CitizenApplications" SELECT * FROM s2s42_copy;
            SELECT 1;
            """);
        var before = await Sql("hr_core_db", $"SELECT md5(row_to_json(a)::text) FROM \"CitizenApplications\" a WHERE \"Id\"='{application}';");

        using var read = await AcceptanceHarness.CreateServiceClientAsync("svc-compliance", ServiceScopes.DsarSubjectRead, r, TenantA);
        var refused = await read.PostAsync($"api/internal/dsar-subjects/candidates/{application}/anonymize", null);
        r.CaptureHttp("anonymize-read-scope", "POST", "/api/internal/dsar-subjects/candidates/{id}/anonymize", (int)refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(before, await Sql("hr_core_db", $"SELECT md5(row_to_json(a)::text) FROM \"CitizenApplications\" a WHERE \"Id\"='{application}';"));

        using var erase = await AcceptanceHarness.CreateServiceClientAsync(
            "svc-compliance", $"{ServiceScopes.DsarSubjectRead} {ServiceScopes.DsarSubjectErase}", r, TenantA);
        var allowed = await erase.PostAsync($"api/internal/dsar-subjects/candidates/{application}/anonymize", null);
        r.CaptureHttp("anonymize-erase-scope", "POST", "/api/internal/dsar-subjects/candidates/{id}/anonymize", (int)allowed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.NotEqual(before, await Sql("hr_core_db", $"SELECT md5(row_to_json(a)::text) FROM \"CitizenApplications\" a WHERE \"Id\"='{application}';"));

        r.WriteScenarioDbChecks(new { application, refused = (int)refused.StatusCode, allowed = (int)allowed.StatusCode, unchangedAfterRefusal = true });
        r.WriteScenarioSteps(
            "1. A copy of a seeded application with a random id (setup by psql). Negative: svc-compliance with dsar.subject.read alone: 403, and the row is unchanged (psql).\n" +
            "2. With dsar.subject.erase: 200, and the row changed.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42g

    private static StringContent Number(CodeEntityKind kind, Guid? tenant = null) =>
        Json(new { entityKind = (int)kind, tenantId = tenant });

    [SkippableFact]
    public async Task Ac42g_Numbers_belong_to_their_owner_in_the_tenant_of_the_request()
    {
        var r = Recorder("42g", "120 parallel numbers for the owner; users, other kinds and a tenant in the body are refused or ignored", "central-a");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        const int batch = 120;
        using var service = await AcceptanceHarness.CreateServiceClientAsync(Logistics, ServiceScopes.HrCoreWorkflow, r, TenantA);
        var counterSql = $"SELECT COALESCE(SUM(\"LastValue\"),0) FROM \"CodeSequenceCounters\" WHERE \"TenantId\"='{TenantA:D}' AND \"EntityKind\"='AssetTag';";
        var issuedBefore = await Count("hr_core_db", $"SELECT COUNT(*) FROM \"CodeIssuances\" WHERE \"IssuedByClientId\"='{Logistics}';");

        var codes = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(Enumerable.Range(0, batch), new ParallelOptions { MaxDegreeOfParallelism = 30 }, async (_, _) =>
        {
            var response = await service.PostAsync("api/internal/codes/generate", Number(CodeEntityKind.AssetTag));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            codes.Add(doc.RootElement.GetProperty("code").GetString()!);
        });
        Assert.Equal(batch, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(issuedBefore + batch, await Count("hr_core_db", $"SELECT COUNT(*) FROM \"CodeIssuances\" WHERE \"IssuedByClientId\"='{Logistics}';"));

        using var user = await UiClientAsync("central-a");
        var counter = await Count("hr_core_db", counterSql);
        var oldRoute = await user.PostAsync("api/hr-core/api/codes/generate", Number(CodeEntityKind.AssetTag));
        var internalRoute = await user.PostAsync("api/hr-core/api/internal/codes/generate", Number(CodeEntityKind.AssetTag));
        r.CaptureHttp("user-old-route", "POST", "/api/hr-core/api/codes/generate", (int)oldRoute.StatusCode);
        r.CaptureHttp("user-internal-route", "POST", "/api/hr-core/api/internal/codes/generate", (int)internalRoute.StatusCode);
        Assert.True(oldRoute.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{(int)oldRoute.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, internalRoute.StatusCode);
        Assert.Equal(counter, await Count("hr_core_db", counterSql));

        var foreign = await service.PostAsync("api/internal/codes/generate", Number(CodeEntityKind.AdministrativeDecision));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var otherTenantCounter = $"SELECT COALESCE(SUM(\"LastValue\"),0) FROM \"CodeSequenceCounters\" WHERE \"TenantId\"='{TenantB:D}' AND \"EntityKind\"='AssetTag';";
        var otherBefore = await Count("hr_core_db", otherTenantCounter);
        var spoofed = await service.PostAsync("api/internal/codes/generate", Number(CodeEntityKind.AssetTag, TenantB));
        Assert.Equal(HttpStatusCode.OK, spoofed.StatusCode);
        Assert.Equal(otherBefore, await Count("hr_core_db", otherTenantCounter));
        Assert.Equal(counter + 1, await Count("hr_core_db", counterSql));

        r.WriteScenarioDbChecks(new { batch, unique = codes.Distinct().Count(), oldRoute = (int)oldRoute.StatusCode, internalRoute = (int)internalRoute.StatusCode, foreign = (int)foreign.StatusCode, otherBefore });
        r.WriteScenarioSteps(
            "1. 120 parallel numbers for svc-admin-logistics on the internal route: 120 unique, each with IssuedByClientId.\n" +
            "2. Negative: central-a: api/codes/generate does not exist, the internal route is 403, the counter does not move (psql).\n" +
            "3. Negative: AdministrativeDecision for svc-admin-logistics: 403. TenantId of agency B in the body: the number is in agency A and B's counter stands still.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42h

    private static async Task<string> FreeEmployeeAsync()
    {
        var used = await Sql("hr_contracts_db", "SELECT COALESCE(string_agg(DISTINCT \"EmployeeId\"::text, ','), '') FROM \"SeparationRecords\";");
        var exclude = string.Join(",", used.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => $"'{id}'").DefaultIfEmpty("'00000000-0000-0000-0000-000000000000'"));
        return await Sql("hr_core_db",
            "SELECT e.\"Id\" FROM \"Employees\" e JOIN \"EmploymentRecords\" r ON r.\"EmployeeId\"=e.\"Id\" " +
            $"WHERE e.\"TenantId\"='{TenantA:D}' AND r.\"EndDate\" IS NULL AND e.\"CurrentStatus\"={ActiveEmployee} AND e.\"LegalHold\"=false AND e.\"Id\" NOT IN ({exclude}) ORDER BY e.\"CreatedAt\" LIMIT 1;");
    }

    private static Task<HttpResponseMessage> CreateSeparationAsync(HttpClient http, string employee) =>
        PostUiFormAsync(http, "ui/hr-contracts/SeparationWizard/Step1", "ui/hr-contracts/SeparationWizard/Step1", new Dictionary<string, string>
        {
            ["EmployeeId"] = employee,
            ["SeparationType"] = "Resignation",
            ["EffectiveDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Reason"] = "AC-S2S-42h"
        });

    private static Task<long> SeparationsAsync(string employee) =>
        Count("hr_contracts_db", $"SELECT COUNT(*) FROM \"SeparationRecords\" WHERE \"EmployeeId\"='{employee}';");

    private static Task<HttpResponseMessage> PublishAsync(HttpClient http, string record) =>
        PostUiFormAsync(http, $"ui/hr-contracts/SeparationRecords/Details/{record}", $"ui/hr-contracts/SeparationRecords/PublishSeparation/{record}");

    private static Task<string> PublishedAsync(string record) =>
        Sql("hr_contracts_db", $"SELECT \"SeparationEventPublished\" FROM \"SeparationRecords\" WHERE \"Id\"='{record}';");

    [SkippableFact]
    public async Task Ac42h_The_legal_hold_gate_refuses_at_creation_and_publication_also_when_it_cannot_check()
    {
        var r = Recorder("42h", "Creation and publication pass without a hold, stop with one, and stop when HR.Core is down", "a-contracts-maker, a-contracts-checker");
        AcceptanceGate.RequireEnabled(r);
        await StackReadyAsync();
        using var maker = await UiClientAsync("a-contracts-maker");
        using var checker = await UiClientAsync("a-contracts-checker");

        var free = await FreeEmployeeAsync();
        AcceptanceGate.RequireData(r, Guid.TryParse(free, out _), "لا يوجد موظف بلا انفصال", "No law50 employee of agency A without a separation (scripts/acceptance-reset.ps1).");
        await CreateSeparationAsync(maker, free);
        Assert.Equal(1, await SeparationsAsync(free));
        var record = await Sql("hr_contracts_db", $"SELECT \"Id\" FROM \"SeparationRecords\" WHERE \"EmployeeId\"='{free}';");

        // The wizard made the checklist. The operator's steps and the seed's empty regime are not what this scenario tests.
        await Sql("hr_core_db", $"UPDATE \"EmploymentRecords\" SET \"LegalRegimeId\"='law50' WHERE \"EmployeeId\"='{free}' AND \"EndDate\" IS NULL; SELECT 1;");
        await Sql("hr_contracts_db", $"UPDATE \"SeparationChecklists\" SET \"IsCompleted\"=true WHERE \"SeparationRecordId\"='{record}'; SELECT 1;");
        await PublishAsync(checker, record);
        var detailsAfterPublish = await checker.GetAsync($"ui/hr-contracts/SeparationRecords/Details/{record}");
        Assert.True(await PublishedAsync(record) == "t", Alerts(WebUtility.HtmlDecode(await detailsAfterPublish.Content.ReadAsStringAsync())));

        var held = await FreeEmployeeAsync();
        await Sql("hr_core_db", $"UPDATE \"Employees\" SET \"LegalHold\"=true, \"LegalHoldReason\"='AC-S2S-42h' WHERE \"Id\"='{held}'; SELECT 1;");
        string outageRecord;
        try
        {
            var created = await CreateSeparationAsync(maker, held);
            Assert.Contains("حجز قانوني", WebUtility.HtmlDecode(await created.Content.ReadAsStringAsync()), StringComparison.Ordinal);
            Assert.Equal(0, await SeparationsAsync(held));

            // A record made before the hold cannot be published under it.
            await Sql("hr_core_db", $"UPDATE \"Employees\" SET \"LegalHold\"=false WHERE \"Id\"='{held}'; SELECT 1;");
            await CreateSeparationAsync(maker, held);
            outageRecord = await Sql("hr_contracts_db", $"SELECT \"Id\" FROM \"SeparationRecords\" WHERE \"EmployeeId\"='{held}';");
            await Sql("hr_core_db", $"UPDATE \"Employees\" SET \"LegalHold\"=true WHERE \"Id\"='{held}'; SELECT 1;");
            await PublishAsync(checker, outageRecord);
            Assert.Equal("f", await PublishedAsync(outageRecord));
        }
        finally
        {
            await Sql("hr_core_db", $"UPDATE \"Employees\" SET \"LegalHold\"=false, \"LegalHoldReason\"=NULL WHERE \"Id\"='{held}'; SELECT 1;");
        }

        await StopAsync("hr-core-web");
        string page;
        string publishedDuringOutage;
        try
        {
            await PublishAsync(checker, outageRecord);
            publishedDuringOutage = await PublishedAsync(outageRecord);
            var details = await checker.GetAsync($"ui/hr-contracts/SeparationRecords/Details/{outageRecord}");
            page = WebUtility.HtmlDecode(await details.Content.ReadAsStringAsync());
        }
        finally
        {
            await StartAsync("hr-core-web");
        }

        Assert.Equal("f", publishedDuringOutage);
        Assert.Contains("تعذّر التحقق من الحجز القانوني", page, StringComparison.Ordinal);

        r.WriteScenarioDbChecks(new { free, record, held, outageRecord, publishedDuringOutage });
        r.WriteScenarioSteps(
            "1. An employee with no hold: creation passes; publication passes once the checklist is complete and the record names law50 (setup by psql).\n" +
            "2. Negative: LegalHold=true: creation is refused and no record exists; a record made before the hold cannot be published.\n" +
            "3. Negative: hr-core-web stopped: publication is refused with the failure message and the record stays unpublished.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 42i

    [SkippableFact]
    public async Task Ac42i_Every_deployment_references_its_own_secret()
    {
        var r = Recorder("42i", "Static proof: per-service secrets in the manifests", "platform");
        AcceptanceGate.RequireEnabled(r);
        var deploy = Path.Combine(AcceptanceHarness.RepoRoot, "deploy");
        var shared = Directory.GetFiles(deploy, "*", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("hr-app-secrets", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(shared);

        var k8s = Path.Combine(deploy, "k8s", "base");
        var deployments = Directory.GetDirectories(k8s).Where(d => File.Exists(Path.Combine(d, "deployment.yaml"))).ToList();
        foreach (var directory in deployments)
        {
            var name = Path.GetFileName(directory);
            Assert.Contains($"name: {name}-secrets", await File.ReadAllTextAsync(Path.Combine(directory, "deployment.yaml")));
        }

        var kubectl = await AcceptanceMessagingHelper.RunProcessAsync("kubectl", "kustomize", Path.Combine(deploy, "k8s", "overlays", "production-ha"));
        var rendered = kubectl.ExitCode == 0;
        if (rendered)
            Assert.DoesNotContain("hr-app-secrets", kubectl.Output, StringComparison.Ordinal);

        r.WriteScenarioDbChecks(new { deployments = deployments.Count, sharedSecretMentions = shared.Count, kubectlRendered = rendered });
        r.WriteScenarioSteps(
            "1. Each of the 15 Deployments references <name>-secrets (S2s042K8sPerServiceSecretsTests holds the full rules).\n" +
            "2. Negative: no file under deploy mentions hr-app-secrets; kubectl kustomize of production-ha agrees where kubectl exists.");
        r.WriteResult("Pass");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcS2s42Collection
{
    public const string Name = "AcS2s42";
}
