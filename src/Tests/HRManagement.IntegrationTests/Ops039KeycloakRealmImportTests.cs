using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Wiring;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-39 (sections 1-2, P-2, P-4): Keycloak 24 in production mode (<c>start</c>, PostgreSQL) imports the realm rendered
/// for the <b>Production</b> profile from an empty database, resolves its ${NAME} placeholders from its own environment,
/// and deploy/keycloak/reconcile.sh (run from the Keycloak image, as the keycloak-reconcile one-shot) keeps it in step:
/// AC-OPS-39a's Production assertions (sslRequired, brute force, password policy, password grant refused, the bootstrap
/// administrator's UPDATE_PASSWORD), a token per matrix client with its scopes and audiences (39b), reconcile twice ->
/// "0 changes", and a secret rotation through reconcile (39d).
/// </summary>
public sealed class Ops039KeycloakFixture : IAsyncLifetime
{
    public const string AppDomain = "hr.ops039.test";
    public const string BootstrapAdmin = "agency-admin-ops039";

    private INetwork? _network;
    private PostgreSqlContainer? _pg;
    private IContainer? _keycloak;
    private string? _importDir;

    public string RepoRoot => RepoPaths.Root;
    public ServiceWiringMatrix Matrix { get; } = ServiceWiringMatrix.Load();
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
    public string AdminPassword { get; } = "kc-admin-" + Hex(12);

    public string BaseUrl => $"http://{_keycloak!.Hostname}:{_keycloak.GetMappedPublicPort(8080)}";
    public string ImportLog { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (!RealInfra.IsEnabled) return;

        _importDir = Path.Combine(Path.GetTempPath(), "ops039-kc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_importDir);
        var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
            Path.Combine(RepoRoot, "scripts", "keycloak", "render-realm.ps1"),
            $"-Profile Production -AppDomain {AppDomain} -OutFile \"{Path.Combine(_importDir, "realm-hrmanagement.json")}\"");
        if (exitCode != 0) throw new InvalidOperationException("render-realm.ps1 failed: " + stdout + stderr);

        Secrets["KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"] = "gw-" + Hex(16);
        Secrets["KEYCLOAK_DIRECTORY_SYNC_SECRET"] = "ds-" + Hex(16);
        Secrets["HRM_BOOTSTRAP_ADMIN_PASSWORD"] = "Tmp-" + Hex(16);
        foreach (var service in Matrix.Services.Where(s => s.ServiceIdentity))
            Secrets[service.SecretEnv!] = "svc-" + Hex(16);

        _network = new NetworkBuilder().WithName("ops039-" + Guid.NewGuid().ToString("N")[..12]).Build();
        await _network.CreateAsync();

        var dbPassword = Hex(16);
        _pg = new PostgreSqlBuilder(RealInfra.PostgresImage)
            .WithNetwork(_network).WithNetworkAliases("postgresql")
            .WithDatabase("keycloak_db").WithUsername("keycloak").WithPassword(dbPassword)
            .Build();
        await _pg.StartAsync();

        var builder = new ContainerBuilder(RealInfra.KeycloakImage)
            .WithNetwork(_network).WithNetworkAliases("keycloak")
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment("KC_DB_URL", "jdbc:postgresql://postgresql:5432/keycloak_db")
            .WithEnvironment("KC_DB_USERNAME", "keycloak")
            .WithEnvironment("KC_DB_PASSWORD", dbPassword)
            .WithEnvironment("KC_HTTP_ENABLED", "true")
            .WithEnvironment("KC_HOSTNAME_STRICT", "false")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithEnvironment("HRM_BOOTSTRAP_ADMIN_USER", BootstrapAdmin)
            .WithResourceMapping(new DirectoryInfo(_importDir), "/opt/keycloak/data/import")
            .WithPortBinding(8080, true)
            .WithCommand("start", "--import-realm")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/realms/hrmanagement")));
        foreach (var (key, value) in Secrets) builder = builder.WithEnvironment(key, value);
        _keycloak = builder.Build();
        await _keycloak.StartAsync();
        ImportLog = (await _keycloak.GetLogsAsync()).Stdout;
    }

    public async Task DisposeAsync()
    {
        if (_keycloak is not null) await _keycloak.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
        if (_network is not null) await _network.DeleteAsync();
        if (_importDir is not null && Directory.Exists(_importDir)) Directory.Delete(_importDir, recursive: true);
    }

    /// <summary>deploy/keycloak/reconcile.sh in a Keycloak-image container on the same network, as the compose one-shot.</summary>
    public async Task<(long ExitCode, string Output)> ReconcileAsync(IReadOnlyDictionary<string, string>? overrides = null, bool check = false)
    {
        var builder = new ContainerBuilder(RealInfra.KeycloakImage)
            .WithNetwork(_network!)
            .WithEntrypoint("bash")
            .WithCommand(check ? ["/opt/hrm/reconcile/reconcile.sh", "--check"] : ["/opt/hrm/reconcile/reconcile.sh"])
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("HRM_KEYCLOAK_URL", "http://keycloak:8080")
            .WithResourceMapping(new DirectoryInfo(Path.Combine(RepoRoot, "deploy", "keycloak")), "/opt/hrm/reconcile")
            .WithResourceMapping(new DirectoryInfo(_importDir!), "/opt/keycloak/data/import")
            .WithWaitStrategy(Wait.ForUnixContainer());
        foreach (var (key, value) in Secrets)
            builder = builder.WithEnvironment(key, overrides is not null && overrides.TryGetValue(key, out var v) ? v : value);
        await using var container = builder.Build();
        await container.StartAsync();
        var exit = await container.GetExitCodeAsync();
        var logs = await container.GetLogsAsync();
        return (exit, logs.Stdout + logs.Stderr);
    }

    public async Task<HttpResponseMessage> TokenAsync(params (string Key, string Value)[] form)
    {
        using var http = new HttpClient();
        return await http.PostAsync($"{BaseUrl}/realms/hrmanagement/protocol/openid-connect/token",
            new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));
    }

    public async Task<JsonElement> AdminGetAsync(string path)
    {
        using var tokenResponse = await PostFormAsync($"{BaseUrl}/realms/master/protocol/openid-connect/token",
            ("grant_type", "password"), ("client_id", "admin-cli"), ("username", "admin"), ("password", AdminPassword));
        tokenResponse.EnsureSuccessStatusCode();
        var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = await http.GetStringAsync($"{BaseUrl}/admin/realms/hrmanagement{path}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async Task AdminPutAsync(string path, string json)
    {
        using var tokenResponse = await PostFormAsync($"{BaseUrl}/realms/master/protocol/openid-connect/token",
            ("grant_type", "password"), ("client_id", "admin-cli"), ("username", "admin"), ("password", AdminPassword));
        var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.PutAsync($"{BaseUrl}/admin/realms/hrmanagement{path}", new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> PostFormAsync(string url, params (string Key, string Value)[] form)
    {
        using var http = new HttpClient();
        return await http.PostAsync(url, new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));
    }

    public static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part))).RootElement.Clone();
    }

    private static string Hex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}

public sealed class Ops039KeycloakRealmImportTests : IClassFixture<Ops039KeycloakFixture>
{
    private const string Skip = "Set RUN_TESTCONTAINERS=true and start Docker to run this test.";
    private readonly Ops039KeycloakFixture _f;

    public Ops039KeycloakRealmImportTests(Ops039KeycloakFixture fixture) => _f = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Production_import_has_the_production_security_settings_and_every_client_and_scope()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        Assert.Contains("Realm 'hrmanagement' imported", _f.ImportLog);
        var realm = await _f.AdminGetAsync("");
        Assert.Equal("external", realm.GetProperty("sslRequired").GetString());
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.Equal(5, realm.GetProperty("failureFactor").GetInt32());
        Assert.Equal(900, realm.GetProperty("waitIncrementSeconds").GetInt32());
        Assert.Equal("length(12) and notUsername and passwordHistory(3)", realm.GetProperty("passwordPolicy").GetString());

        var clients = (await _f.AdminGetAsync("/clients")).EnumerateArray().ToDictionary(c => c.GetProperty("clientId").GetString()!, c => c);
        Assert.Contains("hr-gateway-client", clients.Keys);
        Assert.Contains("directory-sync", clients.Keys);
        Assert.False(clients["hr-gateway-client"].GetProperty("directAccessGrantsEnabled").GetBoolean());
        foreach (var service in _f.Matrix.Services.Where(s => s.ServiceIdentity))
        {
            Assert.True(clients.TryGetValue(service.ClientId!, out var client), $"{service.ClientId} missing");
            Assert.False(client.GetProperty("standardFlowEnabled").GetBoolean(), service.ClientId);
            Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean(), service.ClientId);
        }

        var scopes = (await _f.AdminGetAsync("/client-scopes")).EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToHashSet();
        Assert.All(_f.Matrix.Scopes.Keys, s => Assert.Contains(s, scopes));
        Assert.Equal(9, _f.Matrix.Scopes.Count);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Password_grant_on_the_gateway_client_is_refused_in_production()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        using var response = await _f.TokenAsync(("grant_type", "password"), ("client_id", "hr-gateway-client"),
            ("client_secret", _f.Secrets["KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"]),
            ("username", Ops039KeycloakFixture.BootstrapAdmin), ("password", _f.Secrets["HRM_BOOTSTRAP_ADMIN_PASSWORD"]));
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("unauthorized_client", await response.Content.ReadAsStringAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Bootstrap_administrator_must_change_the_temporary_password_at_first_login()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var users = await _f.AdminGetAsync("/users?briefRepresentation=false");
        var admin = Assert.Single(users.EnumerateArray());
        Assert.Equal(Ops039KeycloakFixture.BootstrapAdmin, admin.GetProperty("username").GetString());
        Assert.Equal(["UPDATE_PASSWORD"], admin.GetProperty("requiredActions").EnumerateArray().Select(a => a.GetString()));

        // The browser login with the temporary password lands on Keycloak's "update password" form, not on the application.
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        using var http = new HttpClient(handler);
        var authorize = $"{_f.BaseUrl}/realms/hrmanagement/protocol/openid-connect/auth?client_id=hr-gateway-client&response_type=code&scope=openid" +
                        $"&redirect_uri={Uri.EscapeDataString($"https://{Ops039KeycloakFixture.AppDomain}/signin-oidc")}";
        var loginPage = await http.GetStringAsync(authorize);
        var action = WebUtility.HtmlDecode(Regex.Match(loginPage, "action=\"([^\"]+)\"").Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(action), "no login form");
        using var posted = await http.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = Ops039KeycloakFixture.BootstrapAdmin,
            ["password"] = _f.Secrets["HRM_BOOTSTRAP_ADMIN_PASSWORD"],
        }));
        var page = await posted.Content.ReadAsStringAsync();
        if (posted.StatusCode == HttpStatusCode.Found)
        {
            // Keycloak sends the browser to its required-action page; it must not send it to the application yet.
            var location = posted.Headers.Location!;
            Assert.DoesNotContain(Ops039KeycloakFixture.AppDomain, location.ToString());
            page = await http.GetStringAsync(location.IsAbsoluteUri ? location : new Uri(new Uri(_f.BaseUrl), location));
        }

        Assert.Contains("password-new", page);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Every_service_client_gets_a_token_with_its_scopes_and_audiences()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        foreach (var service in _f.Matrix.Services.Where(s => s.ServiceIdentity))
        {
            var form = new List<(string, string)> { ("grant_type", "client_credentials"), ("client_id", service.ClientId!), ("client_secret", _f.Secrets[service.SecretEnv!]) };
            if (service.Scopes.Count > 0) form.Add(("scope", string.Join(' ', service.Scopes)));
            using var response = await _f.TokenAsync(form.ToArray());
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{service.ClientId}: {body}");
            var payload = Ops039KeycloakFixture.Payload(JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!);
            var aud = payload.TryGetProperty("aud", out var a)
                ? (a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : [a.GetString()!])
                : [];
            foreach (var scope in service.Scopes)
                foreach (var audience in _f.Matrix.Scopes[scope].Audiences)
                    Assert.True(aud.Contains(audience), $"{service.ClientId}: aud lacks {audience} for {scope}");
        }

        using var refused = await _f.TokenAsync(("grant_type", "client_credentials"), ("client_id", "svc-payroll"),
            ("client_secret", _f.Secrets["SVC_PAYROLL_CLIENT_SECRET"]), ("scope", "dsar.subject.read"));
        Assert.Contains("invalid_scope", await refused.Content.ReadAsStringAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Reconcile_on_the_imported_realm_changes_nothing_and_prints_no_secret()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var (exit1, out1) = await _f.ReconcileAsync();
        Assert.True(exit1 == 0, out1);
        var (exit2, out2) = await _f.ReconcileAsync();
        Assert.True(exit2 == 0, out2);
        Assert.Contains("reconcile: 0 changes", out2);
        var (check, outCheck) = await _f.ReconcileAsync(check: true);
        Assert.True(check == 0, outCheck);
        foreach (var value in _f.Secrets.Values.Append(_f.AdminPassword))
        {
            Assert.DoesNotContain(value, out1, StringComparison.Ordinal);
            Assert.DoesNotContain(value, out2, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Review F4: with KEYCLOAK_DIRECTORY_SYNC_SECRET empty (directory sync disabled), a directory-sync secret that is
    /// empty or the unresolved placeholder of a first import is replaced by a Keycloak-generated one, which is then
    /// left alone; with the variable set again, reconcile sets it from the variable.
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_unresolved_directory_sync_secret_is_replaced_by_a_generated_one()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var id = (await _f.AdminGetAsync("/clients?clientId=directory-sync")).EnumerateArray().Single().GetProperty("id").GetString();
        await _f.AdminPutAsync($"/clients/{id}", "{\"secret\":\"${KEYCLOAK_DIRECTORY_SYNC_SECRET}\"}");
        var empty = new Dictionary<string, string> { ["KEYCLOAK_DIRECTORY_SYNC_SECRET"] = "" };
        try
        {
            var (exit, output) = await _f.ReconcileAsync(empty);
            Assert.True(exit == 0, output);
            Assert.Contains("client directory-sync: secret generated by Keycloak", output);
            var generated = (await _f.AdminGetAsync($"/clients/{id}/client-secret")).GetProperty("value").GetString();
            Assert.False(string.IsNullOrEmpty(generated));
            Assert.DoesNotContain("${", generated, StringComparison.Ordinal);
            Assert.DoesNotContain(generated!, output, StringComparison.Ordinal);

            var (again, againOutput) = await _f.ReconcileAsync(empty);
            Assert.True(again == 0, againOutput);
            Assert.Contains("reconcile: 0 changes", againOutput);
            Assert.Equal(generated, (await _f.AdminGetAsync($"/clients/{id}/client-secret")).GetProperty("value").GetString());
        }
        finally
        {
            var (restore, restoreOutput) = await _f.ReconcileAsync();
            Assert.True(restore == 0, restoreOutput);
        }

        Assert.Equal(_f.Secrets["KEYCLOAK_DIRECTORY_SYNC_SECRET"], (await _f.AdminGetAsync($"/clients/{id}/client-secret")).GetProperty("value").GetString());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Rotating_a_client_secret_through_reconcile_refuses_the_old_one_and_accepts_the_new_one()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var old = _f.Secrets["SVC_PAYROLL_CLIENT_SECRET"];
        var rotated = "svc-rotated-" + Guid.NewGuid().ToString("N");
        try
        {
            var (exit, output) = await _f.ReconcileAsync(new Dictionary<string, string> { ["SVC_PAYROLL_CLIENT_SECRET"] = rotated });
            Assert.True(exit == 0, output);
            Assert.Contains("client svc-payroll: secret set from SVC_PAYROLL_CLIENT_SECRET", output);
            Assert.DoesNotContain(rotated, output, StringComparison.Ordinal);

            using var refused = await _f.TokenAsync(("grant_type", "client_credentials"), ("client_id", "svc-payroll"), ("client_secret", old));
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Contains("unauthorized_client", await refused.Content.ReadAsStringAsync());
            using var accepted = await _f.TokenAsync(("grant_type", "client_credentials"), ("client_id", "svc-payroll"), ("client_secret", rotated), ("scope", "hr-core.lookup"));
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());

            var (again, againOutput) = await _f.ReconcileAsync(new Dictionary<string, string> { ["SVC_PAYROLL_CLIENT_SECRET"] = rotated });
            Assert.True(again == 0, againOutput);
            Assert.Contains("reconcile: 0 changes", againOutput);
        }
        finally
        {
            var (restore, restoreOutput) = await _f.ReconcileAsync();
            Assert.True(restore == 0, restoreOutput);
        }

        using var back = await _f.TokenAsync(("grant_type", "client_credentials"), ("client_id", "svc-payroll"), ("client_secret", old));
        Assert.True(back.IsSuccessStatusCode, "the original secret is restored");
    }
}
