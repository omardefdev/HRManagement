using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-39 (AC-OPS-39a..h) on the prod-like acceptance stack (<c>hrm-acceptance</c> with
/// docker-compose.acceptance-prodlike.yml), whose Keycloak was started on a new keycloak_db and imported the realm
/// rendered from docker/keycloak/realm-manifest.json + deploy/wiring/service-wiring.json with the Acceptance profile, and
/// whose keycloak-reconcile one-shot keeps it in step. P-4: the Production-profile assertions of 39a (password grant
/// refused, the bootstrap administrator's UPDATE_PASSWORD) run in Ops039KeycloakRealmImportTests (Testcontainers
/// Keycloak 24 + PostgreSQL); 39a here checks the stack's import and the rendered Production file. Scenarios that change
/// things (39d rotation, 39g lost ring, 39h missing path) restore their state and prove it; 39g and 39h run one-off
/// containers (39g on a clone of hr_core_db and an empty scratch ring), so the stack's data and key ring are never at
/// risk. Lab secrets only; no secret value is written to the evidence.
/// </summary>
[RequiredAcceptance]
[Collection(AcOps39Collection.Name)]
public class AcOps39KeycloakDataProtectionTests
{
    private const string Ac = "AC-OPS-39";
    private const string Project = "hrm-acceptance";
    private static string LabServiceSecret => AcceptanceHarness.ResolveServiceClientSecret("svc-any") ?? "";
    private const string LabKeycloakDbPassword = "acceptance-keycloak-db-lab-only";
    private const string Gateway = "http://localhost:6200";
    private static readonly string[] ComposeFiles =
    [
        "docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml",
        "docker-compose.full-stack-gateway.yml", "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml",
    ];

    private static readonly (string Service, string Port)[] Apps =
    [
        ("hr-gateway", "6200"), ("hr-core-web", "6210"), ("hr-contracts-web", "6220"), ("hr-health-web", "6230"),
        ("hr-movements-web", "6240"), ("hr-training-web", "6250"), ("payroll-web", "6260"), ("idcard-web", "6270"),
        ("records-archive-web", "6280"), ("admin-logistics-web", "6290"), ("it-infra-web", "6300"),
        ("dev-analytics-web", "6310"), ("compliance-web", "6320"), ("auth-directory-web", "6330"), ("file-storage-web", "6340"),
    ];

    private static readonly Regex FormAction = new("<form[^>]*action=\"(?<a>[^\"]+)\"", RegexOptions.IgnoreCase);
    private static readonly Regex HiddenInput = new("<input[^>]*type=\"hidden\"[^>]*name=\"(?<n>[^\"]+)\"[^>]*value=\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);

    private static string Root => AcceptanceHarness.RepoRoot;
    private static string KeycloakUrl => AcceptanceEnv.KeycloakBaseUrl;

    // ------------------------------------------------------------------------------------------------ helpers

    private static Dictionary<string, string> DotEnv()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(Root, ".env");
        if (!File.Exists(path)) return values;
        foreach (var line in File.ReadAllLines(path))
        {
            var m = Regex.Match(line, "^([A-Z][A-Z0-9_]*)=(.*)$");
            if (m.Success) values[m.Groups[1].Value] = m.Groups[2].Value.Trim().Trim('"');
        }
        return values;
    }

    private static string GatewayProofKey(string property)
    {
        var path = Path.Combine(Root, ".acceptance", "gateway-proof-keys.json");
        if (!File.Exists(path)) return "";
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty(property, out var s) ? s.GetString() ?? "" : "";
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string exe, string? stdin, TimeSpan? timeout, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = Root,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // The acceptance compose files need the gateway proof pair (as acceptance-local-env.ps1 sets it).
        psi.Environment["ACCEPTANCE_GATEWAY_PROOF_PRIVATE_KEY_B64"] = GatewayProofKey("privateKeyB64");
        psi.Environment["ACCEPTANCE_GATEWAY_PROOF_PUBLIC_KEY_B64"] = GatewayProofKey("publicKeyB64");
        psi.Environment["ACCEPTANCE_SVC_CLIENT_SECRET"] = LabServiceSecret;
        using var p = Process.Start(psi)!;
        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            return (-1, "timeout: " + await o + await e);
        }
        return (p.ExitCode, await o + await e);
    }

    private static Task<(int ExitCode, string Output)> ComposeAsync(TimeSpan? timeout, params string[] args)
    {
        var all = new List<string> { "compose", "-p", Project };
        foreach (var f in ComposeFiles) all.AddRange(["-f", f]);
        all.AddRange(["--profile", "full-stack"]);
        all.AddRange(args);
        return RunAsync("docker", null, timeout, [.. all]);
    }

    private static async Task<string> ContainerAsync(string service)
    {
        var (_, output) = await RunAsync("docker", null, null, "ps", "-a", "-q",
            "--filter", $"label=com.docker.compose.project={Project}", "--filter", $"label=com.docker.compose.service={service}");
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    private static async Task<string> AdminSqlAsync(string database, string sql)
    {
        var pg = await ContainerAsync("postgresql");
        var (exit, output) = await RunAsync("docker", null, null, "exec", pg, "psql", "-X", "-v", "ON_ERROR_STOP=1",
            "-U", DotEnv()["POSTGRES_USER"], "-d", database, "-At", "-c", sql);
        Assert.True(exit == 0, $"admin psql on {database} failed: {output}");
        return output.Trim();
    }

    /// <summary>psql as a login role over TCP to the service name; the password reaches psql through stdin.</summary>
    private static async Task<(int ExitCode, string Output)> RoleSqlAsync(string role, string password, string database, string sql)
    {
        var pg = await ContainerAsync("postgresql");
        return await RunAsync("docker", password, null, "exec", "-i", "-e", "Q=" + sql, pg, "sh", "-c",
            $"PGPASSWORD=\"$(cat)\" psql -X -h postgresql -v ON_ERROR_STOP=1 -U {role} -d {database} -At -c \"$Q\"");
    }

    private static async Task<string> AdminTokenAsync()
    {
        var env = DotEnv();
        using var http = new HttpClient();
        using var response = await http.PostAsync($"{KeycloakUrl}/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = env["KEYCLOAK_ADMIN"],
                ["password"] = env["KEYCLOAK_ADMIN_PASSWORD"],
            }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, "Keycloak admin token: " + (int)response.StatusCode);
        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonNode> AdminGetAsync(string path)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync());
        return JsonNode.Parse(await http.GetStringAsync($"{KeycloakUrl}/admin/realms/hrmanagement{path}"))!;
    }

    private static async Task<HttpResponseMessage> ClientTokenAsync(string clientId, string secret, string? scope = null)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret };
        if (!string.IsNullOrEmpty(scope)) form["scope"] = scope;
        using var http = new HttpClient();
        return await http.PostAsync($"{KeycloakUrl}/realms/hrmanagement/protocol/openid-connect/token", new FormUrlEncodedContent(form));
    }

    private static async Task<bool> HealthyAsync(string port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try { return (await http.GetAsync($"http://localhost:{port}/health/ready")).IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private static async Task<bool> WaitUntilAsync(TimeSpan timeout, Func<Task<bool>> probe)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await probe()) return true;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return await probe();
    }

    private static JsonDocument Matrix() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "deploy", "wiring", "service-wiring.json")));

    private static EvidenceRecorder Recorder(string key, string scenario)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "platform-operator (lab)");
        return recorder;
    }

    private static async Task RequireStackAsync(EvidenceRecorder recorder)
    {
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder,
            !string.IsNullOrEmpty(await ContainerAsync("keycloak-reconcile")) && !string.IsNullOrEmpty(await ContainerAsync("keycloak-db-role")),
            "مكدس القبول الشبيه بالإنتاج غير قائم", "The prod-like acceptance stack (docker-compose.acceptance-prodlike.yml, PLAN-39) is not running.");
    }

    /// <summary>
    /// A minimal browser for the http lab stack: one cookie jar for localhost (cookies do not depend on the port) and
    /// manual redirects. .NET's CookieContainer sends Keycloak's Version=1 cookies back in RFC 2965 form, which Keycloak
    /// does not read over plain http, so the login would restart.
    /// </summary>
    internal sealed class LabBrowser : DelegatingHandler
    {
        private readonly Dictionary<string, string> _jar = new(StringComparer.Ordinal);

        public LabBrowser() : base(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { }

        /// <summary>The gateway's cookies, as a person would copy them for a session-authenticated script.</summary>
        public string GatewayCookies => string.Join("; ", _jar.Where(kv => kv.Key.StartsWith(".AspNetCore.", StringComparison.Ordinal)).Select(kv => $"{kv.Key}={kv.Value}"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = request.Content?.Headers.ContentType;
            for (var hop = 0; ; hop++)
            {
                if (_jar.Count > 0)
                    request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _jar.Select(kv => $"{kv.Key}={kv.Value}")));
                var response = await base.SendAsync(request, cancellationToken);
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                {
                    foreach (var header in setCookies)
                    {
                        var pair = header.Split(';')[0];
                        var eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        var name = pair[..eq].Trim();
                        var value = pair[(eq + 1)..].Trim();
                        if (value.Length == 0 || header.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase)) _jar.Remove(name);
                        else _jar[name] = value;
                    }
                }

                var status = (int)response.StatusCode;
                if (status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is null || hop >= 15)
                {
                    response.RequestMessage = request;
                    return response;
                }

                var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(request.RequestUri!, response.Headers.Location);
                var keepMethod = status is 307 or 308;
                response.Dispose();
                var followed = new HttpRequestMessage(keepMethod ? request.Method : HttpMethod.Get, next);
                if (keepMethod && body is not null)
                {
                    followed.Content = new ByteArrayContent(body);
                    followed.Content.Headers.ContentType = contentType;
                }
                request = followed;
            }
        }
    }

    /// <summary>The browser sign-in through the gateway (code flow), as a person would: no password grant.</summary>
    internal static async Task<(HttpClient Client, string Page)> BrowserSignInAsync(E2ePersona persona, string startPath, LabBrowser? browser = null)
    {
        var client = new HttpClient(browser ?? new LabBrowser()) { Timeout = TimeSpan.FromSeconds(90) };
        var login = await client.GetAsync(Gateway + startPath);
        var html = await login.Content.ReadAsStringAsync();
        var action = WebUtility.HtmlDecode(FormAction.Match(html).Groups["a"].Value);
        Assert.Contains("login-actions/authenticate", action, StringComparison.Ordinal);
        var posted = await client.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = persona.Username,
            ["password"] = AcceptanceHarness.ResolvePassword(persona),
            ["credentialId"] = "",
        }));
        var body = await posted.Content.ReadAsStringAsync();
        if (body.Contains("signin-oidc", StringComparison.Ordinal))
        {
            var target = WebUtility.HtmlDecode(FormAction.Match(body).Groups["a"].Value);
            var fields = HiddenInput.Matches(body).ToDictionary(m => m.Groups["n"].Value, m => WebUtility.HtmlDecode(m.Groups["v"].Value));
            posted = await client.PostAsync(target, new FormUrlEncodedContent(fields));
            body = await posted.Content.ReadAsStringAsync();
        }
        var landed = posted.RequestMessage!.RequestUri!.ToString();
        var feedback = Regex.Match(body, "<title>(?<t>[^<]*)</title>|kc-feedback-text\">(?<f>[^<]*)<|id=\"input-error[^\"]*\"[^>]*>(?<e>[^<]*)<");
        Assert.True(posted.IsSuccessStatusCode && landed.StartsWith(Gateway, StringComparison.Ordinal),
            $"browser sign-in of {persona.Id} ended with {(int)posted.StatusCode} at {landed.Split('?')[0]} ({WebUtility.HtmlDecode(feedback.Value).Trim()})");
        return (client, body);
    }

    private static string Token(string html) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<v>[^\"]+)\"").Groups["v"].Value);

    private static string FirstOption(string html, string name)
    {
        var select = Regex.Match(html, $"<select[^>]*name=\"{Regex.Escape(name)}\"[^>]*>(?<o>.*?)</select>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(select.Success, $"no select {name} on the form");
        return Regex.Matches(select.Groups["o"].Value, "<option[^>]*value=\"(?<v>[^\"]+)\"").Select(m => m.Groups["v"].Value).First(v => v.Length > 0);
    }

    private static Dictionary<string, string> EmployeeForm(string html, string nationalId) => new()
    {
        ["__RequestVerificationToken"] = Token(html),
        ["NationalId"] = nationalId,
        ["FirstName"] = "مفتاح",
        ["MiddleName"] = "حماية",
        ["LastName"] = "تسعة وثلاثون",
        ["BirthDate"] = "1990-05-01",
        ["Gender"] = FirstOption(html, "Gender"),
        ["MaritalStatus"] = FirstOption(html, "MaritalStatus"),
        ["PrimaryOrgUnitId"] = FirstOption(html, "PrimaryOrgUnitId"),
        ["HireDate"] = "2020-01-01",
        ["Cadre"] = Regex.IsMatch(html, "name=\"Cadre\"") ? FirstOption(html, "Cadre") : "Executive",
    };

    private static string NewNationalId() => "39" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ 39a

    [SkippableFact]
    public async Task Ac39a_empty_keycloak_volume_imports_the_realm_from_the_manifest_without_intervention()
    {
        var r = Recorder("39a", "New Keycloak volume: realm imported from the manifest + matrix (Acceptance profile), production security settings");
        await RequireStackAsync(r);

        var realm = await AdminGetAsync("");
        Assert.Equal("external", (string?)realm["sslRequired"]);
        Assert.True((bool)realm["bruteForceProtected"]!);
        Assert.Equal(5, (int)realm["failureFactor"]!);
        Assert.Equal("length(12) and notUsername and passwordHistory(3)", (string?)realm["passwordPolicy"]);

        var clients = (await AdminGetAsync("/clients")).AsArray().ToDictionary(c => (string)c!["clientId"]!, c => c!);
        var scopes = (await AdminGetAsync("/client-scopes")).AsArray().Select(s => (string)s!["name"]!).ToHashSet();
        using var matrix = Matrix();
        var serviceClients = matrix.RootElement.GetProperty("services").EnumerateArray()
            .Where(s => s.GetProperty("serviceIdentity").GetBoolean()).Select(s => s.GetProperty("clientId").GetString()!).ToList();
        var matrixScopes = matrix.RootElement.GetProperty("scopes").EnumerateObject().Select(p => p.Name).ToList();
        foreach (var id in serviceClients.Append("hr-gateway-client").Append("directory-sync"))
            Assert.True(clients.ContainsKey(id), $"client {id} missing");
        Assert.All(matrixScopes, s => Assert.Contains(s, scopes));
        Assert.Equal(9, matrixScopes.Count);
        // The Acceptance exception (the harness signs personas in with the password grant).
        Assert.True((bool)clients["hr-gateway-client"]["directAccessGrantsEnabled"]!);
        Assert.Equal(61, (int)(await AdminGetAsync("/users/count")).GetValue<int>());

        var keycloak = await ContainerAsync("keycloak");
        var (_, logs) = await RunAsync("docker", null, null, "logs", keycloak);
        var imported = logs.Contains("Realm 'hrmanagement' imported", StringComparison.Ordinal);
        var (_, kcUser) = await RunAsync("docker", null, null, "inspect", "-f", "{{range .Config.Env}}{{println .}}{{end}}", keycloak);
        Assert.Contains("KC_DB_USERNAME=keycloak", kcUser);

        // The Production profile of the same sources (P-4: imported and exercised in Ops039KeycloakRealmImportTests).
        var outFile = Path.Combine(Path.GetTempPath(), $"ops039-prod-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output) = await RunAsync(OperatingSystem.IsWindows() ? "powershell" : "pwsh", null, null, "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", Path.Combine(Root, "scripts", "keycloak", "render-realm.ps1"), "-Profile", "Production", "-AppDomain", "hr.ops039.test", "-OutFile", outFile);
            Assert.True(exit == 0, output);
            var production = JsonNode.Parse(File.ReadAllText(outFile))!;
            var gateway = production["clients"]!.AsArray().Single(c => (string?)c!["clientId"] == "hr-gateway-client")!;
            Assert.False((bool)gateway["directAccessGrantsEnabled"]!);
            Assert.Equal("external", (string?)production["sslRequired"]);
            var admin = Assert.Single(production["users"]!.AsArray())!;
            Assert.Equal("UPDATE_PASSWORD", (string?)admin["requiredActions"]![0]);
        }
        finally
        {
            File.Delete(outFile);
            File.Delete(Path.Combine(Path.GetDirectoryName(outFile)!, "reconcile-plan.tsv"));
        }

        r.WriteScenarioSteps("1. Keycloak started on a new keycloak_db (as the keycloak role) imported the realm rendered with the Acceptance profile.\n" +
                             "2. Admin API: security fields, every matrix client + gateway + directory-sync, the 8 scopes, 61 personas.\n" +
                             "3. Production profile rendered from the same sources: no gateway password grant, sslRequired external, one bootstrap admin with UPDATE_PASSWORD " +
                             "(imported and exercised in Ops039KeycloakRealmImportTests: password grant -> unauthorized_client, update-password form at first login).");
        r.WriteScenarioDbChecks(new
        {
            sslRequired = "external",
            bruteForceProtected = true,
            failureFactor = 5,
            passwordPolicy = "length(12) and notUsername and passwordHistory(3)",
            clients = serviceClients.Count + 2,
            scopes = matrixScopes,
            gatewayPasswordGrantInAcceptance = true,
            importLogLineInCurrentContainer = imported,
            productionProfile = new { gatewayPasswordGrant = false, bootstrapAdminRequiredAction = "UPDATE_PASSWORD" },
        });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 39b

    [SkippableFact]
    public async Task Ac39b_every_service_client_gets_a_token_with_its_scopes_and_audiences()
    {
        var r = Recorder("39b", "client_credentials for every svc-* client of the matrix; aud holds every api-* of its scopes");
        await RequireStackAsync(r);

        using var matrix = Matrix();
        var audiences = matrix.RootElement.GetProperty("scopes").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetProperty("audiences").EnumerateArray().Select(a => a.GetString()!).ToList());
        var rows = new List<object>();
        foreach (var service in matrix.RootElement.GetProperty("services").EnumerateArray().Where(s => s.GetProperty("serviceIdentity").GetBoolean()))
        {
            var clientId = service.GetProperty("clientId").GetString()!;
            var scopes = service.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!).ToList();
            using var response = await ClientTokenAsync(clientId, LabServiceSecret, string.Join(' ', scopes));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{clientId}: {(int)response.StatusCode}");
            var jwt = new JwtSecurityToken(JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString());
            var aud = jwt.Audiences.ToHashSet(StringComparer.Ordinal);
            foreach (var scope in scopes)
                foreach (var audience in audiences[scope])
                    Assert.True(aud.Contains(audience), $"{clientId}: aud lacks {audience} ({scope})");
            rows.Add(new { clientId, scopes, aud = aud.Order(StringComparer.Ordinal) });
        }

        using var refused = await ClientTokenAsync("svc-payroll", LabServiceSecret, "dsar.subject.read");
        var refusedBody = await refused.Content.ReadAsStringAsync();
        Assert.Contains("invalid_scope", refusedBody);
        r.WriteScenarioSteps("1. Token per matrix service client with its scopes.\n2. Negative: svc-payroll asks for dsar.subject.read -> invalid_scope.");
        r.WriteScenarioDbChecks(new { tokens = rows, negative = new { client = "svc-payroll", scope = "dsar.subject.read", error = "invalid_scope" } });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 39c

    [SkippableFact]
    public async Task Ac39c_user_token_carries_the_claims_and_the_user_cannot_change_tenant_id()
    {
        var r = Recorder("39c", "User token: tenant_id, employee_id, scope_org_unit_id; the account console cannot change tenant_id");
        await RequireStackAsync(r);

        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona);
        var jwt = AcceptanceHarness.ParseJwt(token);
        string? Claim(string type) => jwt.Claims.FirstOrDefault(c => c.Type == type)?.Value;
        var user = (await AdminGetAsync($"/users?username={persona.Username}&exact=true&briefRepresentation=false")).AsArray().Single()!;
        string? Attribute(string name) => user["attributes"]?[name]?[0]?.GetValue<string>();
        foreach (var claim in new[] { "tenant_id", "employee_id", "scope_org_unit_id" })
        {
            Assert.False(string.IsNullOrEmpty(Claim(claim)), $"{claim} missing from the token");
            Assert.Equal(Attribute(claim), Claim(claim));
        }

        // Negative: the account REST API (the account console) with the user's own token tries another tenant.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var accountUrl = $"{KeycloakUrl}/realms/hrmanagement/account";
        var me = await http.GetAsync(accountUrl);
        var meBody = await me.Content.ReadAsStringAsync();
        int updateStatus;
        if (me.IsSuccessStatusCode)
        {
            var account = JsonNode.Parse(meBody)!.AsObject();
            account["attributes"] ??= new JsonObject();
            account["attributes"]!["tenant_id"] = new JsonArray("a0000002-0000-4000-8000-000000000001");
            using var update = await http.PostAsync(accountUrl, new StringContent(account.ToJsonString(), Encoding.UTF8, "application/json"));
            updateStatus = (int)update.StatusCode;
        }
        else
        {
            updateStatus = (int)me.StatusCode;
        }

        var after = (await AdminGetAsync($"/users?username={persona.Username}&exact=true&briefRepresentation=false")).AsArray().Single()!;
        Assert.Equal(Attribute("tenant_id"), after["attributes"]?["tenant_id"]?[0]?.GetValue<string>());
        var again = AcceptanceHarness.ParseJwt(await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona));
        Assert.Equal(Claim("tenant_id"), again.Claims.First(c => c.Type == "tenant_id").Value);

        r.WriteScenarioSteps("1. Password-grant token of br1-mgr (Acceptance exception): the three claims equal the user's attributes.\n" +
                             "2. Negative: POST /realms/hrmanagement/account with a changed tenant_id (user token).\n" +
                             "3. tenant_id unchanged in the admin API and in a new token (managed attribute, admin edit only).");
        r.WriteScenarioDbChecks(new { persona = persona.Id, claims = new[] { "tenant_id", "employee_id", "scope_org_unit_id" }, accountReadStatus = (int)me.StatusCode, accountUpdateStatus = updateStatus, tenantUnchanged = true });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 39d

    [SkippableFact]
    public async Task Ac39d_rotating_a_client_secret_through_reconcile_takes_effect_and_a_second_run_changes_nothing()
    {
        var r = Recorder("39d", "SVC_PAYROLL_CLIENT_SECRET rotated through keycloak-reconcile: new accepted, old refused, rerun 0 changes, restored");
        await RequireStackAsync(r);

        var rotated = "ops039-rotated-" + Guid.NewGuid().ToString("N");
        string rotateOutput = "", againOutput = "", restoreOutput = "";
        int oldStatus = 0;
        try
        {
            var (exit, output) = await ComposeAsync(TimeSpan.FromMinutes(5), "run", "--rm", "--no-deps", "-e", $"SVC_PAYROLL_CLIENT_SECRET={rotated}", "keycloak-reconcile");
            rotateOutput = output.Replace(rotated, "<rotated>", StringComparison.Ordinal);
            Assert.True(exit == 0, rotateOutput);
            Assert.Contains("client svc-payroll: secret set from SVC_PAYROLL_CLIENT_SECRET", output);
            Assert.DoesNotContain(rotated, output, StringComparison.Ordinal);

            using var refused = await ClientTokenAsync("svc-payroll", LabServiceSecret);
            oldStatus = (int)refused.StatusCode;
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Contains("unauthorized_client", await refused.Content.ReadAsStringAsync());
            using var accepted = await ClientTokenAsync("svc-payroll", rotated, "hr-core.lookup");
            Assert.True(accepted.IsSuccessStatusCode);

            var (again, againOut) = await ComposeAsync(TimeSpan.FromMinutes(5), "run", "--rm", "--no-deps", "-e", $"SVC_PAYROLL_CLIENT_SECRET={rotated}", "keycloak-reconcile");
            againOutput = againOut.Replace(rotated, "<rotated>", StringComparison.Ordinal);
            Assert.True(again == 0, againOutput);
            Assert.Contains("reconcile: 0 changes", againOut);
        }
        finally
        {
            var (restore, restoreOut) = await ComposeAsync(TimeSpan.FromMinutes(5), "run", "--rm", "--no-deps", "keycloak-reconcile");
            restoreOutput = restoreOut;
            Assert.True(restore == 0, restoreOut);
        }

        using var back = await ClientTokenAsync("svc-payroll", LabServiceSecret, "hr-core.lookup");
        Assert.True(back.IsSuccessStatusCode, "the lab secret is restored");
        Assert.True(await HealthyAsync("6260"), "payroll-web ready after the restore");
        r.WriteScenarioSteps("1. docker compose run -e SVC_PAYROLL_CLIENT_SECRET=<rotated> keycloak-reconcile.\n2. Old secret -> 401 unauthorized_client; new -> token.\n" +
                             "3. Second run with the same value -> reconcile: 0 changes.\n4. Restore: reconcile with the stack's value; the lab secret works again; payroll-web ready.");
        r.WriteScenarioDbChecks(new
        {
            rotate = LastLines(rotateOutput, 3),
            oldSecretStatus = oldStatus,
            secondRun = LastLines(againOutput, 1),
            restore = LastLines(restoreOutput, 2),
        });
        r.WriteResult("Pass");
    }

    private static string[] LastLines(string text, int n) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l.Contains("[keycloak-reconcile]", StringComparison.Ordinal)).TakeLast(n).ToArray();

    // ------------------------------------------------------------------------------------------------ 39e

    [SkippableFact]
    public async Task Ac39e_keycloak_connects_with_its_own_role_which_cannot_reach_hr_core_db()
    {
        var r = Recorder("39e", "pg_stat_activity: Keycloak as keycloak; keycloak cannot connect to hr_core_db");
        await RequireStackAsync(r);

        var users = await AdminSqlAsync("postgres",
            "SELECT COALESCE(string_agg(DISTINCT usename, ','), '') FROM pg_stat_activity WHERE datname = 'keycloak_db' AND backend_type = 'client backend' AND application_name <> 'psql'");
        Assert.Equal("keycloak", users);
        var owner = await AdminSqlAsync("postgres", "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'keycloak_db'");
        Assert.Equal("keycloak", owner);
        var foreign = await AdminSqlAsync("keycloak_db",
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) <> 'keycloak'");
        Assert.Equal("0", foreign);

        var (exit, output) = await RoleSqlAsync("keycloak", LabKeycloakDbPassword, "hr_core_db", "SELECT 1");
        Assert.NotEqual(0, exit);
        Assert.Contains("permission denied for database", output);
        r.WriteScenarioSteps("1. pg_stat_activity on keycloak_db: the only client user is keycloak; keycloak owns the database and every table.\n" +
                             "2. Negative: psql as keycloak to hr_core_db -> permission denied for database.");
        r.WriteScenarioDbChecks(new { keycloakDbUsers = users, owner, foreignOwnedTables = 0, hrCoreDbAsKeycloak = "permission denied for database" });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 39f

    [SkippableFact]
    public async Task Ac39f_national_id_and_a_form_from_before_survive_recreating_every_service()
    {
        var r = Recorder("39f", "Employee with a national ID; docker compose up -d --force-recreate (all services); ID shown, old antiforgery form accepted, session kept");
        await RequireStackAsync(r);

        var persona = AcceptanceHarness.RequirePersona("central-a2");
        var (browser, createPage) = await BrowserSignInAsync(persona, "/ui/hr-core/Employees/Create");
        var nationalId = NewNationalId();
        var created = await browser.PostAsync(Gateway + "/ui/hr-core/Employees/Create", new FormUrlEncodedContent(EmployeeForm(createPage, nationalId)));
        var createdUrl = created.RequestMessage!.RequestUri!.ToString();
        Assert.True(created.IsSuccessStatusCode && createdUrl.Contains("/Employees/Details/", StringComparison.Ordinal),
            $"employee not created ({(int)created.StatusCode} {createdUrl})");
        var employeeId = Regex.Match(createdUrl, "/Employees/Details/(?<id>[0-9a-fA-F-]{36})").Groups["id"].Value;
        var stored = await AdminSqlAsync("hr_core_db", $"SELECT left(\"NationalId\", 5) FROM \"Employees\" WHERE \"Id\" = '{employeeId}'");
        Assert.Equal("CfDJ8", stored);   // protected at rest
        // A form rendered before the recreate (its antiforgery token protected with HR.Core's key ring).
        var oldForm = await (await browser.GetAsync(Gateway + "/ui/hr-core/Employees/Create")).Content.ReadAsStringAsync();
        var secondId = NewNationalId();
        var formBefore = EmployeeForm(oldForm, secondId);

        var sw = Stopwatch.StartNew();
        var (upExit, upOutput) = await ComposeAsync(TimeSpan.FromMinutes(15), "up", "-d", "--force-recreate");
        Assert.True(upExit == 0, upOutput);
        foreach (var app in Apps)
            Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(5), () => HealthyAsync(app.Port)), $"{app.Service} not ready after the recreate");
        var recreateSeconds = (int)sw.Elapsed.TotalSeconds;

        var details = await browser.GetAsync($"{Gateway}/ui/hr-core/Employees/Details/{employeeId}");
        var detailsHtml = await details.Content.ReadAsStringAsync();
        Assert.True(details.IsSuccessStatusCode, $"details after recreate: {(int)details.StatusCode}");
        Assert.StartsWith(Gateway, details.RequestMessage!.RequestUri!.ToString(), StringComparison.Ordinal);   // no new sign-in
        Assert.Contains(nationalId, detailsHtml);
        Assert.DoesNotContain("محمي", Regex.Match(detailsHtml, nationalId.Length > 0 ? Regex.Escape(nationalId) + ".{0,40}" : "x").Value);

        var postedAfter = await browser.PostAsync(Gateway + "/ui/hr-core/Employees/Create", new FormUrlEncodedContent(formBefore));
        var afterUrl = postedAfter.RequestMessage!.RequestUri!.ToString();
        Assert.True(postedAfter.IsSuccessStatusCode && afterUrl.Contains("/Employees/Details/", StringComparison.Ordinal),
            $"form from before the recreate refused ({(int)postedAfter.StatusCode} {afterUrl})");

        r.WriteScenarioSteps("1. central-a2 signs in through the gateway (code flow) and creates an employee with a national ID (stored as CfDJ8...).\n" +
                             "2. A second create form is loaded (antiforgery token).\n3. docker compose up -d --force-recreate (every service), all applications ready.\n" +
                             "4. The same browser session opens the employee: the national ID is shown in full; the form from step 2 is accepted.");
        r.WriteScenarioDbChecks(new { employeeId, nationalIdStoredProtected = true, recreateSeconds, sessionKept = true, oldFormAccepted = true });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 39g

    [SkippableFact]
    public async Task Ac39g_a_lost_hr_core_key_ring_stops_hr_core_and_the_reset_switch_lets_it_start_with_critical()
    {
        var r = Recorder("39g", "HR.Core on protected data with an empty key ring: refused (no new ring); with AllowKeyRingReset=true: starts, Critical");
        await RequireStackAsync(r);

        const string clone = "hr_core_ops039_clone";
        const string ring = "hrm-acceptance-ops039-empty-ring";
        var pg = await ContainerAsync("postgresql");
        var env = DotEnv();
        var hrCore = await ContainerAsync("hr-core-web");
        var (_, hrEnv) = await RunAsync("docker", null, null, "inspect", "-f", "{{range .Config.Env}}{{println .}}{{end}}", hrCore);
        var connection = hrEnv.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("ConnectionStrings__HRCoreDb=", StringComparison.Ordinal))["ConnectionStrings__HRCoreDb=".Length..];
        var cloneConnection = Regex.Replace(connection, "Database=[^;]+", "Database=" + clone);
        string refusal = "", resetLine = "";
        try
        {
            await AdminSqlAsync("postgres", $"DROP DATABASE IF EXISTS {clone}");
            await AdminSqlAsync("postgres", $"CREATE DATABASE {clone}");
            var (dump, dumpOut) = await RunAsync("docker", null, TimeSpan.FromMinutes(10), "exec", pg, "sh", "-c",
                $"pg_dump -U \"$POSTGRES_USER\" hr_core_db | psql -q -U \"$POSTGRES_USER\" -d {clone} > /dev/null");
            Assert.True(dump == 0, dumpOut);
            Assert.Equal("CfDJ8", await AdminSqlAsync(clone, "SELECT left(\"NationalId\", 5) FROM \"Employees\" WHERE \"NationalId\" LIKE 'CfDJ8%' LIMIT 1"));
            await RunAsync("docker", null, null, "volume", "create", ring);
            await RunAsync("docker", null, null, "run", "--rm", "-v", $"{ring}:/v", "alpine:3.20.3", "chown", "1654:1654", "/v");

            // Negative: the ring is lost (empty) over protected data -> HR.Core exits with the canary message.
            var (exit, output) = await ComposeAsync(TimeSpan.FromMinutes(4), "run", "--rm", "--no-deps", "-T",
                "-v", $"{ring}:/var/hrm/dpkeys-ops039", "-e", "DataProtection__KeyRingPath=/var/hrm/dpkeys-ops039",
                "-e", $"ConnectionStrings__HRCoreDb={cloneConnection}", "-e", "RabbitMQ__Host=ops039-no-broker", "hr-core-web");
            Assert.NotEqual(0, exit);
            refusal = Regex.Match(output, "حلقة مفاتيح حماية البيانات لا تطابق البيانات المخزنة[^\r\n]*").Value;
            Assert.False(string.IsNullOrEmpty(refusal), "no canary message in the output");
            Assert.Contains("/var/hrm/dpkeys-ops039", refusal);
            var (_, keys) = await RunAsync("docker", null, null, "run", "--rm", "-v", $"{ring}:/v:ro", "alpine:3.20.3", "sh", "-c", "ls /v | grep -c '^key-' || true");
            Assert.Equal("0", keys.Trim());   // no new ring was generated

            // Positive: the emergency switch lets the start go on, with Critical.
            var name = "ops039-reset-" + Guid.NewGuid().ToString("N")[..8];
            var (started, startOut) = await ComposeAsync(TimeSpan.FromMinutes(2), "run", "-d", "--name", name, "--no-deps",
                "-v", $"{ring}:/var/hrm/dpkeys-ops039", "-e", "DataProtection__KeyRingPath=/var/hrm/dpkeys-ops039",
                "-e", "DataProtection__AllowKeyRingReset=true",
                "-e", $"ConnectionStrings__HRCoreDb={cloneConnection}", "-e", "RabbitMQ__Host=ops039-no-broker", "hr-core-web");
            Assert.True(started == 0, startOut.Replace(cloneConnection, "<clone>", StringComparison.Ordinal));
            try
            {
                Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(3), async () =>
                {
                    var (_, l) = await RunAsync("docker", null, null, "logs", name);
                    resetLine = Regex.Match(l, "[^\r\n]*AllowKeyRingReset=true[^\r\n]*").Value;
                    return resetLine.Length > 0;
                }), "no Critical reset line");
                // Microsoft.Extensions.Logging Critical is Serilog's Fatal: "FTL" on the console.
                Assert.Contains("FTL", resetLine, StringComparison.Ordinal);
                var (_, state) = await RunAsync("docker", null, null, "inspect", "-f", "{{.State.Status}}", name);
                Assert.Equal("running", state.Trim());
            }
            finally
            {
                await RunAsync("docker", null, null, "rm", "-f", name);
            }
        }
        finally
        {
            await RunAsync("docker", null, null, "volume", "rm", "-f", ring);
            await AdminSqlAsync("postgres", $"DROP DATABASE IF EXISTS {clone} WITH (FORCE)");
        }

        // The stack itself is untouched: the real HR.Core is ready and its own canary passes. The line appears only at a start
        // with protected data, and a fresh stack started before its seeds wrote any: one restart makes this test independent
        // of what ran before it (PLAN-41).
        Assert.True(await HealthyAsync("6210"));
        const string CanaryLine = "Key-ring canary of HR.Core: a protected national ID decrypts with the key ring";
        var (_, realLogs) = await RunAsync("docker", null, null, "logs", await ContainerAsync("hr-core-web"));
        if (!realLogs.Contains(CanaryLine, StringComparison.Ordinal))
        {
            var (restartExit, restartOut) = await ComposeAsync(TimeSpan.FromMinutes(3), "restart", "hr-core-web");
            Assert.True(restartExit == 0, restartOut);
            Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(3), () => HealthyAsync("6210")), "hr-core-web not ready after the restart");
            (_, realLogs) = await RunAsync("docker", null, null, "logs", await ContainerAsync("hr-core-web"));
        }
        var canaryOk = realLogs.Contains(CanaryLine, StringComparison.Ordinal);
        Assert.True(canaryOk, "the stack's HR.Core canary line is missing");
        r.WriteScenarioSteps("1. hr_core_db cloned (protected national IDs) and an empty scratch key ring.\n" +
                             "2. Negative: a one-off hr-core-web on the clone with the empty ring exits with the canary message; the ring stays empty.\n" +
                             "3. Positive: the same with DataProtection__AllowKeyRingReset=true keeps running and logs Critical.\n" +
                             "4. Clone and scratch ring removed; the stack's HR.Core is ready, its canary passed. (The acceptance stack shares one key-ring " +
                             "volume between services, so the lost dp_hr_core volume is played by the empty scratch ring.)");
        r.WriteScenarioDbChecks(new { refusal, resetLine = resetLine.Length > 300 ? resetLine[..300] : resetLine, newRingGenerated = false, stackCanaryPassed = canaryOk });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 39h

    [SkippableFact]
    public async Task Ac39h_a_service_without_a_key_ring_path_does_not_start()
    {
        var r = Recorder("39h", "IDCard.Web with RequirePersistentKeyRing and no DataProtection__KeyRingPath exits with the guard message");
        await RequireStackAsync(r);

        var (exit, output) = await ComposeAsync(TimeSpan.FromMinutes(4), "run", "--rm", "--no-deps", "-T",
            "-e", "DataProtection__KeyRingPath=", "-e", "RabbitMQ__Host=ops039-no-broker", "idcard-web");
        Assert.NotEqual(0, exit);
        var message = Regex.Match(output, "IDCard\\.Web: DataProtection:KeyRingPath[^\r\n;]*").Value;
        Assert.False(string.IsNullOrEmpty(message), "no guard message in the output");
        Assert.True(await HealthyAsync("6270"), "the stack's idcard-web is still ready");
        r.WriteScenarioSteps("1. One-off idcard-web (Acceptance, DataProtection__RequirePersistentKeyRing=true) with an empty DataProtection__KeyRingPath.\n" +
                             "2. It exits non-zero with the Data Protection guard message naming the service and the key; the stack's idcard-web stays ready.");
        r.WriteScenarioDbChecks(new { exitCode = exit, message });
        r.WriteResult("Pass");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcOps39Collection
{
    public const string Name = "AC-OPS-39 prod-like stack";
}
