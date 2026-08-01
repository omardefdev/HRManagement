using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Directory.Infrastructure.Keycloak;

/// <summary>
/// Keycloak Admin REST (service-account client in the same realm). Disabled unless Keycloak:DirectorySync:Enabled is true.
/// </summary>
public sealed class KeycloakUserProvisioningService : IKeycloakUserProvisioningService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeycloakUserProvisioningService> _logger;

    public KeycloakUserProvisioningService(
        HttpClient http,
        IConfiguration configuration,
        ILogger<KeycloakUserProvisioningService> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<KeycloakProvisionResult> TryEnsureUserAsync(
        string username,
        string email,
        string displayName,
        Guid employeeId,
        Guid? scopeOrgUnitId = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var sync = _configuration.GetSection("Keycloak:DirectorySync");
        if (!sync.GetValue("Enabled", false))
            return KeycloakProvisionResult.Disabled();

        if (!TryParseAuthority(_configuration["Keycloak:Authority"], out var baseUrl, out var realm))
        {
            _logger.LogWarning("Keycloak DirectorySync: invalid Keycloak:Authority");
            return KeycloakProvisionResult.Failed("invalid authority");
        }

        var clientId = sync["AdminClientId"];
        var clientSecret = sync["AdminClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
            clientSecret = Environment.GetEnvironmentVariable("Keycloak__DirectorySync__AdminClientSecret");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogWarning("Keycloak DirectorySync: missing AdminClientId or AdminClientSecret");
            return KeycloakProvisionResult.Failed("missing admin client credentials");
        }

        string? accessToken;
        try
        {
            accessToken = await RequestServiceAccountTokenAsync(baseUrl, realm, clientId, clientSecret!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: token request failed");
            return KeycloakProvisionResult.Failed("token request failed");
        }

        if (string.IsNullOrEmpty(accessToken))
            return KeycloakProvisionResult.Failed("empty access token");

        var attributes = new Dictionary<string, string[]>
        {
            ["employeeId"] = [employeeId.ToString("D")]
        };
        if (scopeOrgUnitId.HasValue)
            attributes["scope_org_unit_id"] = [scopeOrgUnitId.Value.ToString("D")];
        if (tenantId.HasValue)
            attributes["tenant_id"] = [tenantId.Value.ToString("D")];

        var payload = new
        {
            username,
            email,
            enabled = true,
            emailVerified = false,
            firstName = displayName.Length > 0 ? displayName : username,
            attributes
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/admin/realms/{realm}/users")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: create user HTTP failed");
            return KeycloakProvisionResult.Failed("create user HTTP failed");
        }

        if (resp.IsSuccessStatusCode)
        {
            var id = resp.Headers.Location?.Segments.LastOrDefault()?.Trim('/');
            return KeycloakProvisionResult.Succeeded(id);
        }

        if ((int)resp.StatusCode == 409)
        {
            _logger.LogInformation("Keycloak user already exists for {Username}", username);
            return KeycloakProvisionResult.Succeeded(null);
        }

        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Keycloak create user failed {Status}: {Body}", (int)resp.StatusCode, body);
        return KeycloakProvisionResult.Failed($"status {(int)resp.StatusCode}");
    }

    public async Task<KeycloakProvisionResult> TryDisableUserAsync(string keycloakUserId, CancellationToken ct = default)
    {
        var result = await SetUserEnabledAsync(keycloakUserId, false, ct);
        if (result.Attempted && result.Success)
            await TryRevokeUserSessionsAsync(keycloakUserId, ct);
        return result;
    }

    public async Task<KeycloakProvisionResult> TryEnableUserAsync(string keycloakUserId, CancellationToken ct = default)
        => await SetUserEnabledAsync(keycloakUserId, true, ct);

    public async Task<KeycloakProvisionResult> TryAssignRealmRolesAsync(string keycloakUserId, string[] roleNames, CancellationToken ct = default)
    {
        if (!TryGetSyncConfig(out var baseUrl, out var realm, out var accessToken))
            return _lastConfigResult!;

        accessToken = await SafeGetTokenAsync(baseUrl!, realm!, ct);
        if (accessToken is null) return KeycloakProvisionResult.Failed("token request failed");

        var availableRolesUrl = $"{baseUrl}/admin/realms/{realm}/roles";
        using var listReq = new HttpRequestMessage(HttpMethod.Get, availableRolesUrl);
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var listResp = await _http.SendAsync(listReq, ct);
            if (!listResp.IsSuccessStatusCode)
                return KeycloakProvisionResult.Failed($"list roles status {(int)listResp.StatusCode}");

            await using var stream = await listResp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var roleMappings = new List<object>();
            foreach (var role in doc.RootElement.EnumerateArray())
            {
                var name = role.GetProperty("name").GetString();
                if (roleNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    roleMappings.Add(new
                    {
                        id = role.GetProperty("id").GetString(),
                        name
                    });
                }
            }

            if (roleMappings.Count == 0)
                return KeycloakProvisionResult.Succeeded("no matching roles found");

            var assignUrl = $"{baseUrl}/admin/realms/{realm}/users/{keycloakUserId}/role-mappings/realm";
            using var assignReq = new HttpRequestMessage(HttpMethod.Post, assignUrl)
            {
                Content = JsonContent.Create(roleMappings)
            };
            assignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var assignResp = await _http.SendAsync(assignReq, ct);
            return assignResp.IsSuccessStatusCode
                ? KeycloakProvisionResult.Succeeded($"assigned {roleMappings.Count} roles")
                : KeycloakProvisionResult.Failed($"assign roles status {(int)assignResp.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: assign roles HTTP failed");
            return KeycloakProvisionResult.Failed("assign roles HTTP failed");
        }
    }

    public async Task<KeycloakProvisionResult> TrySyncUserInfoAsync(string keycloakUserId, string displayName, string email, CancellationToken ct = default)
    {
        if (!TryGetSyncConfig(out var baseUrl, out var realm, out var accessToken))
            return _lastConfigResult!;

        accessToken = await SafeGetTokenAsync(baseUrl!, realm!, ct);
        if (accessToken is null) return KeycloakProvisionResult.Failed("token request failed");

        var payload = new { firstName = displayName, email };
        var url = $"{baseUrl}/admin/realms/{realm}/users/{keycloakUserId}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode
                ? KeycloakProvisionResult.Succeeded(keycloakUserId)
                : KeycloakProvisionResult.Failed($"sync info status {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: sync user info HTTP failed");
            return KeycloakProvisionResult.Failed("sync user info HTTP failed");
        }
    }

    private async Task TryRevokeUserSessionsAsync(string keycloakUserId, CancellationToken ct)
    {
        if (!TryGetSyncConfig(out var baseUrl, out var realm, out _))
            return;

        var accessToken = await SafeGetTokenAsync(baseUrl!, realm!, ct);
        if (accessToken is null)
            return;

        var url = $"{baseUrl}/admin/realms/{realm}/users/{keycloakUserId}/logout";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Keycloak DirectorySync: session revoke failed for {UserId}: status {Status}",
                    keycloakUserId, (int)resp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: session revoke HTTP failed for {UserId}", keycloakUserId);
        }
    }

    private async Task<KeycloakProvisionResult> SetUserEnabledAsync(string keycloakUserId, bool enabled, CancellationToken ct)
    {
        if (!TryGetSyncConfig(out var baseUrl, out var realm, out var accessToken))
            return _lastConfigResult!;

        accessToken = await SafeGetTokenAsync(baseUrl!, realm!, ct);
        if (accessToken is null) return KeycloakProvisionResult.Failed("token request failed");

        var url = $"{baseUrl}/admin/realms/{realm}/users/{keycloakUserId}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(new { enabled })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode
                ? KeycloakProvisionResult.Succeeded(keycloakUserId)
                : KeycloakProvisionResult.Failed($"set enabled={enabled} status {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: set enabled={Enabled} HTTP failed", enabled);
            return KeycloakProvisionResult.Failed($"set enabled={enabled} HTTP failed");
        }
    }

    private KeycloakProvisionResult? _lastConfigResult;

    private bool TryGetSyncConfig(out string? baseUrl, out string? realm, out string? accessToken)
    {
        baseUrl = null;
        realm = null;
        accessToken = null;

        var sync = _configuration.GetSection("Keycloak:DirectorySync");
        if (!sync.GetValue("Enabled", false))
        {
            _lastConfigResult = KeycloakProvisionResult.Disabled();
            return false;
        }

        if (!TryParseAuthority(_configuration["Keycloak:Authority"], out var b, out var r))
        {
            _logger.LogWarning("Keycloak DirectorySync: invalid Keycloak:Authority");
            _lastConfigResult = KeycloakProvisionResult.Failed("invalid authority");
            return false;
        }

        baseUrl = b;
        realm = r;
        return true;
    }

    private async Task<string?> SafeGetTokenAsync(string baseUrl, string realm, CancellationToken ct)
    {
        var sync = _configuration.GetSection("Keycloak:DirectorySync");
        var clientId = sync["AdminClientId"];
        var clientSecret = sync["AdminClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
            clientSecret = Environment.GetEnvironmentVariable("Keycloak__DirectorySync__AdminClientSecret");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogWarning("Keycloak DirectorySync: missing AdminClientId or AdminClientSecret");
            return null;
        }

        try
        {
            return await RequestServiceAccountTokenAsync(baseUrl, realm, clientId, clientSecret!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keycloak DirectorySync: token request failed");
            return null;
        }
    }

    private async Task<string?> RequestServiceAccountTokenAsync(
        string baseUrl,
        string realm,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var tokenUrl = $"{baseUrl}/realms/{realm}/protocol/openid-connect/token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });

        using var response = await _http.PostAsync(tokenUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }

    private static bool TryParseAuthority(string? authority, out string baseUrl, out string realm)
    {
        baseUrl = "";
        realm = "";
        if (string.IsNullOrWhiteSpace(authority))
            return false;

        var a = authority.TrimEnd('/');
        const string marker = "/realms/";
        var i = a.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return false;

        baseUrl = a[..i];
        realm = a[(i + marker.Length)..];
        return realm.Length > 0;
    }
}
