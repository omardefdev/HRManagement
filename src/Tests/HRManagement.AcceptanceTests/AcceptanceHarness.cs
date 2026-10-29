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
