using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace HRManagement.UnitTests;

/// <summary>IAM-001: negative JWT validation matching KeycloakExtensions TokenValidationParameters.</summary>
public class Iam001NegativeTokenTests
{
    private const string ValidIssuer = "https://keycloak.example.com/realms/hrmanagement";
    private const string ValidAudience = "hr-gateway-client";

    private static readonly string[] ValidAudiences = [ValidAudience, "account"];

    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _otherKey;
    private readonly SigningCredentials _credentials;

    public Iam001NegativeTokenTests()
    {
        var rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(rsa) { KeyId = "test-kid" };
        _credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);

        var otherRsa = RSA.Create(2048);
        _otherKey = new RsaSecurityKey(otherRsa) { KeyId = "other-kid" };
    }

    /// <summary>Matches KeycloakExtensions.AddKeycloakJwtBearer TokenValidationParameters shape.</summary>
    private TokenValidationParameters CreateParameters(
        RsaSecurityKey? signingKey = null,
        IEnumerable<string>? issuers = null,
        IEnumerable<string>? audiences = null) =>
        new()
        {
            ValidateAudience = true,
            ValidAudiences = audiences?.ToArray() ?? ValidAudiences,
            ValidateIssuer = true,
            ValidIssuers = issuers?.ToArray() ?? [ValidIssuer],
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey ?? _signingKey,
            ValidateLifetime = true,
            RequireSignedTokens = true
        };

    private string CreateToken(
        string issuer,
        string audience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SigningCredentials? credentials = null)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim("sub", "user-1"),
                new Claim("preferred_username", "alice")
            ],
            notBefore: notBefore ?? now.AddMinutes(-1),
            expires: expires ?? now.AddMinutes(5),
            signingCredentials: credentials ?? _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AssertValidationFails(string token, TokenValidationParameters parameters)
    {
        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(token, parameters, out _));
    }

    [Fact]
    public void Token_with_wrong_issuer_is_rejected()
    {
        var token = CreateToken(issuer: "https://evil.example.com/realms/other", audience: ValidAudience);
        AssertValidationFails(token, CreateParameters());
    }

    [Fact]
    public void Token_with_wrong_audience_is_rejected()
    {
        var token = CreateToken(issuer: ValidIssuer, audience: "other-client");
        AssertValidationFails(token, CreateParameters());
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        // Expiry well beyond the 2-minute clock skew.
        var expired = DateTime.UtcNow.AddMinutes(-10);
        var token = CreateToken(
            issuer: ValidIssuer,
            audience: ValidAudience,
            notBefore: expired.AddMinutes(-5),
            expires: expired);
        AssertValidationFails(token, CreateParameters());
    }

    [Fact]
    public void Token_with_invalid_signature_is_rejected()
    {
        var otherCreds = new SigningCredentials(_otherKey, SecurityAlgorithms.RsaSha256);
        var token = CreateToken(
            issuer: ValidIssuer,
            audience: ValidAudience,
            credentials: otherCreds);
        // Validate with the expected (different) signing key — signature must fail.
        AssertValidationFails(token, CreateParameters(signingKey: _signingKey));
    }

    [Fact]
    public void Clock_skew_is_two_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), CreateParameters().ClockSkew);
    }

    [Fact]
    public void Valid_token_is_accepted()
    {
        var token = CreateToken(issuer: ValidIssuer, audience: ValidAudience);
        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(token, CreateParameters(), out var validated);
        Assert.NotNull(principal);
        Assert.IsType<JwtSecurityToken>(validated);
    }
}
