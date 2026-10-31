using System.Text.Json;
using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>QA-004 environment contract: Skip vs Fail and gateway base URL.</summary>
public static class E2eEnv
{
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_PLAYWRIGHT_E2E"), "true",
            StringComparison.OrdinalIgnoreCase);

    public static string BaseUrl =>
        (Environment.GetEnvironmentVariable("GATEWAY_BASE_URL") ?? "http://localhost:5000").TrimEnd('/');

    public static bool IsKeycloakLoginUrl(string url) =>
        url.Contains("realms", StringComparison.OrdinalIgnoreCase)
        || url.Contains(":8080", StringComparison.OrdinalIgnoreCase)
        || url.Contains(":8088", StringComparison.OrdinalIgnoreCase)
        || url.Contains("signin", StringComparison.OrdinalIgnoreCase);

    public static bool IsGatewayUrl(string url) =>
        url.Contains(":5000", StringComparison.OrdinalIgnoreCase)
        || url.Contains(":5200", StringComparison.OrdinalIgnoreCase);

    /// <summary>A page on the GATEWAY_BASE_URL origin other than the OIDC callback (IsKeycloakLoginUrl matches /signin-oidc too).</summary>
    public static bool IsGatewayPageUrl(string url) =>
        TryGetGatewayPath(url, out var path) && !path.StartsWith(OidcCallbackPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>The gateway's OIDC callback, where Keycloak posts the authorization code back.</summary>
    public static bool IsGatewayOidcCallbackUrl(string url) =>
        TryGetGatewayPath(url, out var path) && path.StartsWith(OidcCallbackPath, StringComparison.OrdinalIgnoreCase);

    private const string OidcCallbackPath = "/signin-oidc";

    private static bool TryGetGatewayPath(string url, out string path)
    {
        path = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var gateway)
            || Uri.Compare(uri, gateway, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        path = uri.AbsolutePath;
        return true;
    }

    public static string ArtifactsDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetEnvironmentVariable("E2E_ARTIFACTS_DIR")
                ?? Path.Combine("TestResults", "e2e-artifacts"),
                DateTime.UtcNow.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Skip when flag off; otherwise proceed (callers must Fail on missing stack/secrets).</summary>
    public static void RequireEnabled()
    {
        Skip.IfNot(IsEnabled, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");
    }

    public static async Task EnsureGatewayReachableAsync(IAPIRequestContext api)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var response = await api.GetAsync($"{BaseUrl}/health/live", new() { Timeout = 15_000 });
                E2eHttp.EnsureSuccessNot5xx(response, $"{BaseUrl}/health/live");
                Assert.Equal(200, response.Status);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        Assert.Fail($"Gateway unreachable at {BaseUrl}/health/live while RUN_PLAYWRIGHT_E2E=true: {last?.Message}");
    }
}

/// <summary>HTTP assertions — 503/5xx never count as success (QA-004 AC).</summary>
public static class E2eHttp
{
    public static void EnsureNotServerError(int status, string context)
    {
        Assert.True(
            status < 500,
            $"{context}: HTTP {status} is a server error; 503/5xx must not be treated as success.");
    }

    public static void EnsureNotServerError(IAPIResponse response, string context) =>
        EnsureNotServerError(response.Status, context);

    public static void EnsureNotServerError(IResponse response, string context) =>
        EnsureNotServerError(response.Status, context);

    public static void EnsureSuccessNot5xx(IAPIResponse response, string context)
    {
        EnsureNotServerError(response, context);
        Assert.True(
            response.Status is >= 200 and < 400 or 401 or 403 or 302 or 404,
            $"{context}: unexpected HTTP {response.Status}");
    }

    public static void EnsureAuthChallengeOrForbidden(IAPIResponse response, string context)
    {
        EnsureNotServerError(response, context);
        Assert.True(
            response.Status is 401 or 403 or 302,
            $"{context}: expected 401/403/302 for unauthenticated/unauthorized, got {response.Status}");
    }

    /// <summary>
    /// A signed-in user without the path's role: the gateway answers 403 "missing role for this module" itself. A challenge
    /// (302/401) means the session is gone, and its other 403s (tenant claim, module entitlement) refuse for another reason.
    /// </summary>
    public static async Task EnsureRoleForbiddenAsync(IResponse response, string context) =>
        EnsureRoleForbidden(response.Status, await response.TextAsync(), context);

    public static async Task EnsureRoleForbiddenAsync(IAPIResponse response, string context) =>
        EnsureRoleForbidden(response.Status, await response.TextAsync(), context);

    private static void EnsureRoleForbidden(int status, string body, string context)
    {
        EnsureNotServerError(status, context);
        Assert.True(
            status == 403 && body.Contains("missing role for this module", StringComparison.Ordinal),
            $"{context}: expected the gateway's 403 \"missing role for this module\" for a signed-in user without the role, got {status}: {body[..Math.Min(body.Length, 200)]}");
    }
}

public sealed class E2eSeedManifest
{
    public string Version { get; set; } = "";
    public string Card { get; set; } = "";
}

public sealed class E2ePersona
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordEnv { get; set; } = "";
    public string DefaultPassword { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public string TenantCode { get; set; } = "";
    public string? TenantId { get; set; }
    public string? ScopeOrgUnitId { get; set; }
    public string? ScopeOrgUnitCode { get; set; }
    public string? Purpose { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Fixture { get; set; }
    public string[] Scenarios { get; set; } = [];
}

public sealed class E2ePersonaFile
{
    public List<E2ePersona> Personas { get; set; } = [];
}

public static class E2eSeed
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }

    public static E2eSeedManifest LoadManifest()
    {
        var path = ResolveDataPath(Path.Combine("testdata", "e2e", "manifest.json"));
        Assert.True(File.Exists(path), $"Missing E2E seed manifest at {path}");
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<E2eSeedManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(manifest);
        Assert.False(string.IsNullOrWhiteSpace(manifest!.Version));
        return manifest;
    }

    public static E2ePersona RequirePersona(string id)
    {
        var path = ResolveDataPath(Path.Combine("testdata", "e2e", "personas.json"));
        Assert.True(File.Exists(path), $"Missing personas file at {path}");
        var file = JsonSerializer.Deserialize<E2ePersonaFile>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var persona = file?.Personas.FirstOrDefault(p => p.Id == id);
        Assert.NotNull(persona);
        return persona!;
    }

    private static string ResolveDataPath(string relative)
    {
        var candidates = new[]
        {
            Path.Combine(RepoRoot, relative),
            Path.Combine(AppContext.BaseDirectory, relative)
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public static string ResolvePassword(E2ePersona persona)
    {
        var fromEnv = Environment.GetEnvironmentVariable(persona.PasswordEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        if (!string.IsNullOrWhiteSpace(persona.DefaultPassword))
            return persona.DefaultPassword;
        Assert.Fail(
            $"Password for persona '{persona.Id}' missing: set {persona.PasswordEnv} (RUN_PLAYWRIGHT_E2E=true).");
        return "";
    }
}
