using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>PLAN-00 acceptance environment contract.</summary>
public static class AcceptanceEnv
{
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_ACCEPTANCE"), "true",
            StringComparison.OrdinalIgnoreCase);

    public static string KeycloakBaseUrl =>
        (Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL") ?? "http://localhost:8088").TrimEnd('/');

    /// <summary>Skips unless <c>RUN_ACCEPTANCE=true</c>; fails a required scenario under <c>ACCEPTANCE_REQUIRE_ALL</c>.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void RequireEnabled() => AcceptanceGate.RequireEnabled();
}

public sealed class AcceptancePersonaFile
{
    public List<E2ePersona> Personas { get; set; } = [];
}

public static class AcceptanceHarness
{
    public static string RepoRoot => E2eSeed.RepoRoot;

    /// <summary>Where this run writes evidence; the job points it away from the committed folder (PLAN-41, P-12a).</summary>
    public static string EvidenceRoot =>
        Environment.GetEnvironmentVariable("ACCEPTANCE_EVIDENCE_DIR") ?? Path.Combine(RepoRoot, "acceptance-evidence");

    public static IReadOnlyList<E2ePersona> LoadPersonas()
    {
        var path = ResolveDataPath(Path.Combine("testdata", "acceptance", "personas.json"));
        Assert.True(File.Exists(path), $"Missing acceptance personas at {path}");
        var file = JsonSerializer.Deserialize<AcceptancePersonaFile>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(file);
        return file!.Personas;
    }

    public static E2ePersona RequirePersona(string id)
    {
        var persona = LoadPersonas().FirstOrDefault(p => p.Id == id);
        Assert.NotNull(persona);
        return persona!;
    }

    public static string ResolvePassword(E2ePersona persona)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(persona.PasswordEnv);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        var credentialsPath = Path.Combine(RepoRoot, ".acceptance", "credentials.json");
        if (File.Exists(credentialsPath))
        {
            using var credentials = JsonDocument.Parse(File.ReadAllText(credentialsPath));
            if (credentials.RootElement.TryGetProperty("passwords", out var passwords)
                && passwords.TryGetProperty(persona.Username, out var password))
                return password.GetString()!;
        }

        Assert.Fail(
            $"Password for '{persona.Username}' is missing. Run scripts/prepare-keycloak-import.ps1 -Profile Acceptance.");
        return string.Empty;
    }

    public static async Task<(string AccessToken, string CorrelationId)> LoginAsync(E2ePersona persona)
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var token = await FetchPasswordGrantTokenAsync(persona, correlationId);
        return (token, correlationId);
    }

    /// <summary>
    /// PLAN-42 (P-6): the secret of a service client of the acceptance realm: <c>ACCEPTANCE_SVC_&lt;CLIENT&gt;_SECRET</c> (the client id
    /// without <c>svc-</c>, upper case), then <c>ACCEPTANCE_SVC_CLIENT_SECRET</c>, then the generated local credentials. Null when
    /// none exists; no value is kept in the repository.
    /// </summary>
    public static string? ResolveServiceClientSecret(string clientId)
    {
        var name = clientId.StartsWith("svc-", StringComparison.Ordinal) ? clientId["svc-".Length..] : clientId;
        foreach (var variable in new[] { $"ACCEPTANCE_SVC_{name.Replace('-', '_').ToUpperInvariant()}_SECRET", "ACCEPTANCE_SVC_CLIENT_SECRET" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        var credentialsPath = Path.Combine(RepoRoot, ".acceptance", "credentials.json");
        if (!File.Exists(credentialsPath))
            return null;
        using var credentials = JsonDocument.Parse(File.ReadAllText(credentialsPath));
        return credentials.RootElement.TryGetProperty("serviceClientSecret", out var secret) ? secret.GetString() : null;
    }

    /// <summary>A client_credentials token from the acceptance Keycloak; <paramref name="scope"/> null asks for the client's defaults.</summary>
    public static async Task<HttpResponseMessage> FetchServiceTokenResponseAsync(string clientId, string secret, string? scope)
    {
        using var http = new HttpClient();
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", secret)
        };
        if (scope is not null)
            form.Add(new("scope", scope));
        return await http.PostAsync(
            $"{AcceptanceEnv.KeycloakBaseUrl.TrimEnd('/')}/realms/hrmanagement/protocol/openid-connect/token",
            new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// PLAN-42 (آ§8): an <see cref="HttpClient"/> that calls a service as a service does: straight to the container, with a service
    /// token, the tenant in <c>X-Tenant-ID</c> and, when given, the user in <c>X-Acting-User</c>. Default target: HR.Core
    /// (<c>HR_CORE_DIRECT_URL</c>). A missing secret writes Blocked through <see cref="AcceptanceGate"/>, never Pass. Later plans
    /// reuse this for every service token.
    /// </summary>
    public static async Task<HttpClient> CreateServiceClientAsync(
        string clientId,
        string? scope,
        EvidenceRecorder recorder,
        Guid? tenantId = null,
        Guid? actingUser = null,
        string? baseUrl = null)
    {
        var secret = ResolveServiceClientSecret(clientId);
        AcceptanceGate.RequireData(
            recorder,
            !string.IsNullOrWhiteSpace(secret),
            "ط³ط± ط¹ظ…ظٹظ„ ط§ظ„ط®ط¯ظ…ط© ط؛ظٹط± ظ…ظˆط¬ظˆط¯",
            "No service client secret: run scripts/prepare-keycloak-import.ps1 -Profile Acceptance or set ACCEPTANCE_SVC_CLIENT_SECRET.");

        using var response = await FetchServiceTokenResponseAsync(clientId, secret!, scope);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Service token for {clientId} failed: {(int)response.StatusCode}");
        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;

        var target = baseUrl ?? Environment.GetEnvironmentVariable("HR_CORE_DIRECT_URL") ?? "http://localhost:6210";
        var http = new HttpClient { BaseAddress = new Uri(target.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenantId is Guid tenant)
            http.DefaultRequestHeaders.Add("X-Tenant-ID", tenant.ToString("D"));
        if (actingUser is Guid actor)
            http.DefaultRequestHeaders.Add("X-Acting-User", actor.ToString("D"));
        return http;
    }

    public static async Task<HttpClient> CreateAuthorizedClientAsync(
        E2ePersona persona,
        string correlationId,
        bool allowAutoRedirect = true)
    {
        var token = await FetchPasswordGrantTokenAsync(persona, correlationId);
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect })
        {
            BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        return http;
    }

    /// <summary>
    /// PLAN-47: a bearer client for HR.Core's policy lifecycle writes (drafts, rules, effective date, discard, publish). It holds
    /// the antiforgery cookie and sends the request token on every call, as those actions require.
    /// </summary>
    public static async Task<HttpClient> CreatePolicyLifecycleClientAsync(E2ePersona persona, string correlationId)
    {
        var token = await FetchPasswordGrantTokenAsync(persona, correlationId);
        var baseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/");
        var cookies = new System.Net.CookieContainer();
        var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = baseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        using var issued = await http.GetAsync("api/hr-core/api/policies/antiforgery");
        var body = await issued.Content.ReadAsStringAsync();
        Assert.True(issued.IsSuccessStatusCode, $"policy antiforgery for {persona.Username}: {(int)issued.StatusCode} {body}");
        // The service scopes its antiforgery cookie to /ui/hr-core; the policy API sits under /api/hr-core.
        if (issued.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies.Where(c => c.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal)))
            {
                var pair = setCookie.Split(';')[0].Split('=', 2);
                cookies.Add(baseAddress, new System.Net.Cookie(pair[0], pair[1], "/"));
            }
        }

        using var doc = JsonDocument.Parse(body);
        http.DefaultRequestHeaders.Add(doc.RootElement.GetProperty("headerName").GetString()!, doc.RootElement.GetProperty("requestToken").GetString());
        return http;
    }

    public static async Task<string> FetchPasswordGrantTokenAsync(E2ePersona persona, string? correlationId = null)
    {
        using var resp = await FetchPasswordGrantResponseAsync(persona, correlationId);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Token grant failed for {persona.Username}: {resp.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    public static async Task<HttpResponseMessage> FetchPasswordGrantResponseAsync(
        E2ePersona persona,
        string? correlationId = null)
    {
        var password = ResolvePassword(persona);
        var clientSecret = Environment.GetEnvironmentVariable("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET")
            ?? Environment.GetEnvironmentVariable("E2E_KEYCLOAK_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(clientSecret))
            Assert.Fail("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET required for acceptance token grant.");

        using var http = new HttpClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "hr-gateway-client",
            ["client_secret"] = clientSecret,
            ["username"] = persona.Username,
            ["password"] = password
        };
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"{AcceptanceEnv.KeycloakBaseUrl}/realms/hrmanagement/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (!string.IsNullOrWhiteSpace(correlationId))
            req.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        return await http.SendAsync(req);
    }

    public static JwtSecurityToken ParseJwt(string accessToken) =>
        new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

    public static void AssertTenantClaim(JwtSecurityToken jwt, string expectedTenantId)
    {
        var claim = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
        Assert.Equal(expectedTenantId, claim);
    }

    public static void AssertScopeClaim(JwtSecurityToken jwt, string? expectedScopeOrgUnitId)
    {
        var claim = jwt.Claims.FirstOrDefault(c => c.Type == "scope_org_unit_id")?.Value;
        if (expectedScopeOrgUnitId is null)
        {
            Assert.True(string.IsNullOrWhiteSpace(claim));
            return;
        }
        Assert.Equal(expectedScopeOrgUnitId, claim);
    }

    public static async Task<HttpResponseMessage> EnterTenantAsync(
        HttpClient http,
        Guid tenantId,
        string reason)
    {
        var payload = JsonSerializer.Serialize(new { tenantId, reason });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        return await http.PostAsync("api/platform/enter-tenant", content);
    }

    public static async Task<HttpResponseMessage> ExitTenantAsync(HttpClient http) =>
        await http.PostAsync("api/platform/exit-tenant", null);

    internal static string ResolveDataPath(string relative)
    {
        var candidates = new[]
        {
            Path.Combine(RepoRoot, relative),
            Path.Combine(AppContext.BaseDirectory, relative)
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
