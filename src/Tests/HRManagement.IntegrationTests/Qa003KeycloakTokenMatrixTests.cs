using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HRManagement.IntegrationTests;

/// <summary>QA-003-D: JWT token matrix against a real Keycloak realm (not browser journeys).</summary>
[Collection("Keycloak")]
public class Qa003KeycloakTokenMatrixTests
{
    private readonly KeycloakFixture _fixture;

    public Qa003KeycloakTokenMatrixTests(KeycloakFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Valid_Client_Credentials_Token_Validates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var token = await FetchClientTokenAsync(_fixture.ClientId, _fixture.ClientSecret);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var principal = await ValidateAsync(token, audience: _fixture.ClientId);
        Assert.NotNull(principal);
        Assert.True(principal!.Identity?.IsAuthenticated);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Wrong_Audience_Token_Rejected()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var token = await FetchClientTokenAsync(_fixture.WrongAudienceClientId, _fixture.WrongAudienceClientSecret);

        // Token from wrong client must not satisfy azp == qa003-service
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ValidateAsync(token, audience: _fixture.ClientId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Tampered_Token_Rejected()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var token = await FetchClientTokenAsync(_fixture.ClientId, _fixture.ClientSecret);
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        parts[2] = new string('a', Math.Max(8, parts[2].Length));
        var tampered = string.Join('.', parts);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ValidateAsync(tampered, audience: _fixture.ClientId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Keycloak_Stopped_Token_Fetch_Fails()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        await using var kc = await StartEphemeralKeycloakAsync();
        var authority = $"{kc.BaseUrl}/realms/hrmanagement-qa003";
        var tokenUrl = $"{authority}/protocol/openid-connect/token";

        using (var http = new HttpClient())
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "qa003-service",
                ["client_secret"] = "qa003-service-secret"
            });
            var ok = await http.PostAsync(tokenUrl, form);
            Assert.True(ok.IsSuccessStatusCode);
        }

        await kc.Container.StopAsync();

        using var http2 = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "qa003-service",
                ["client_secret"] = "qa003-service-secret"
            });
            var resp = await http2.PostAsync(tokenUrl, form);
            resp.EnsureSuccessStatusCode();
        });
    }

    private async Task<string> FetchClientTokenAsync(string clientId, string clientSecret)
    {
        var tokenUrl = $"{_fixture.Authority}/protocol/openid-connect/token";
        using var http = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });
        using var resp = await http.PostAsync(tokenUrl, form);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"token endpoint failed: {(int)resp.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token missing");
    }

    private async Task<System.Security.Claims.ClaimsPrincipal?> ValidateAsync(string token, string audience)
    {
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{_fixture.Authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = false });

        var oidc = await configManager.GetConfigurationAsync(CancellationToken.None);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = _fixture.Authority,
            // Keycloak client-credentials tokens often use aud=account and azp=client_id
            ValidateAudience = false,
            IssuerSigningKeys = oidc.SigningKeys,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, parameters, out var validated);
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal(audience, jwt.Payload.TryGetValue("azp", out var azp) ? azp?.ToString() : null);
        return principal;
    }

    private static async Task<EphemeralKeycloak> StartEphemeralKeycloakAsync()
    {
        var fixture = new KeycloakFixture();
        await fixture.InitializeAsync();
        return new EphemeralKeycloak(fixture);
    }

    private sealed class EphemeralKeycloak(KeycloakFixture inner) : IAsyncDisposable
    {
        public string BaseUrl => inner.BaseUrl;
        public DotNet.Testcontainers.Containers.IContainer Container => inner.Container;

        public ValueTask DisposeAsync() => new(inner.DisposeAsync());
    }
}
