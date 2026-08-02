using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Iam001JwtKeycloakTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "iam001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void JwtBearer_validates_audience()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://keycloak.example.com/realms/hrmanagement",
                ["Keycloak:ClientId"] = "hr-gateway-client",
                ["Keycloak:RequireHttpsMetadata"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAuthentication().AddKeycloakJwtBearer(config);
        var sp = services.BuildServiceProvider();

        var optionsMonitor = sp.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>();
        var opts = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(opts.TokenValidationParameters.ValidateAudience);
        Assert.Contains("hr-gateway-client", opts.TokenValidationParameters.ValidAudiences!);

        // Keycloak stamps "account" on nearly every access token in the realm. Accepting it gave
        // all services one shared audience, so a token issued for any client authenticated
        // everywhere. Per-service separation depends on it staying out.
        Assert.DoesNotContain("account", opts.TokenValidationParameters.ValidAudiences!);
    }

    [Fact]
    public void JwtBearer_validates_audience_even_when_none_configured()
    {
        // Guards the fail-open shape this used to have: ValidateAudience was derived from whether
        // an audience list could be built, so a service with no audience configured accepted
        // every token in the realm.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://keycloak.example.com/realms/hrmanagement",
                ["Keycloak:RequireHttpsMetadata"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAuthentication().AddKeycloakJwtBearer(config);
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(opts.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public void JwtBearer_validates_issuer()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://keycloak.example.com/realms/hrmanagement",
                ["Keycloak:ClientId"] = "test-client"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAuthentication().AddKeycloakJwtBearer(config);
        var sp = services.BuildServiceProvider();

        var optionsMonitor = sp.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>();
        var opts = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.True(opts.TokenValidationParameters.ValidateIssuer);
        Assert.Contains("https://keycloak.example.com/realms/hrmanagement",
            opts.TokenValidationParameters.ValidIssuers!);
    }

    [Fact]
    public void JwtBearer_includes_api_audience_from_service_identity()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://kc.example.com/realms/hrmanagement",
                ["Keycloak:ClientId"] = "hr-core-service",
                ["ServiceIdentity:ApiAudience"] = "hr-core-api"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAuthentication().AddKeycloakJwtBearer(config);
        var sp = services.BuildServiceProvider();

        var optionsMonitor = sp.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>();
        var opts = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Contains("hr-core-api", opts.TokenValidationParameters.ValidAudiences!);
    }

    [Fact]
    public void JwtBearer_clock_skew_is_two_minutes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://kc.example.com/realms/hrmanagement",
                ["Keycloak:ClientId"] = "test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAuthentication().AddKeycloakJwtBearer(config);
        var sp = services.BuildServiceProvider();

        var optionsMonitor = sp.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>();
        var opts = optionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.FromMinutes(2), opts.TokenValidationParameters.ClockSkew);
    }

    [Fact]
    public void ModuleAuth_throws_when_not_behind_gateway_in_production()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HrAuth:BehindGateway"] = "false",
                ["Keycloak:Authority"] = "https://kc.example.com/realms/hrmanagement",
                ["Keycloak:ClientId"] = "test"
            })
            .Build();

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");

        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddModuleAuthentication(config, env.Object));
    }

    [Fact]
    public void ModuleAuth_allows_behind_gateway_false_in_development()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HrAuth:BehindGateway"] = "false",
                ["Keycloak:Authority"] = "http://localhost:8088/realms/hrmanagement",
                ["Keycloak:ClientId"] = "test",
                ["Keycloak:RequireHttpsMetadata"] = "false"
            })
            .Build();

        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");

        var services = new ServiceCollection();
        services.AddModuleAuthentication(config, env.Object);
    }

    [Fact]
    public void HasBearerAuthorization_detects_bearer_token()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer eyJhbGciOiJSUzI1NiJ9.test.sig";
        Assert.True(KeycloakExtensions.HasBearerAuthorization(context.Request));
    }

    [Fact]
    public void HasBearerAuthorization_rejects_empty_bearer()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer ";
        Assert.False(KeycloakExtensions.HasBearerAuthorization(context.Request));
    }

    [Fact]
    public void HasBearerAuthorization_rejects_missing_header()
    {
        var context = new DefaultHttpContext();
        Assert.False(KeycloakExtensions.HasBearerAuthorization(context.Request));
    }

    [Fact]
    public void Gateway_production_config_requires_https()
    {
        var gwProdPath = RepoPaths.Resolve("src", "Gateway", "HRManagement.Gateway",
            "appsettings.Production.json");
        var config = new ConfigurationBuilder()
            .AddJsonFile(gwProdPath)
            .Build();

        Assert.True(bool.Parse(config["Keycloak:RequireHttpsMetadata"]!));
        Assert.True(bool.Parse(config["HrAuth:CookieSecureAlways"]!));
    }

    [Fact]
    public async Task Token_with_wrong_issuer_is_rejected()
    {
        var (parameters, signing) = CreateValidationPair(
            issuer: "https://keycloak.example.com/realms/hrmanagement",
            audience: "hr-gateway-client");
        var bad = CreateToken(signing, issuer: "https://evil.example/realms/other", audience: "hr-gateway-client");
        var result = await new JwtSecurityTokenHandler().ValidateTokenAsync(bad, parameters);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidIssuerException>(result.Exception);
    }

    [Fact]
    public async Task Token_with_wrong_audience_is_rejected()
    {
        var (parameters, signing) = CreateValidationPair(
            issuer: "https://keycloak.example.com/realms/hrmanagement",
            audience: "hr-gateway-client");
        var bad = CreateToken(signing, issuer: "https://keycloak.example.com/realms/hrmanagement", audience: "wrong-client");
        var result = await new JwtSecurityTokenHandler().ValidateTokenAsync(bad, parameters);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (parameters, signing) = CreateValidationPair(
            issuer: "https://keycloak.example.com/realms/hrmanagement",
            audience: "hr-gateway-client");
        var expired = CreateToken(
            signing,
            issuer: "https://keycloak.example.com/realms/hrmanagement",
            audience: "hr-gateway-client",
            notBefore: DateTime.UtcNow.AddMinutes(-30),
            expires: DateTime.UtcNow.AddMinutes(-10));
        var result = await new JwtSecurityTokenHandler().ValidateTokenAsync(expired, parameters);
        Assert.False(result.IsValid);
        Assert.True(result.Exception is SecurityTokenExpiredException or SecurityTokenNotYetValidException
            || result.Exception?.GetType().Name.Contains("Expired", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Token_with_invalid_signature_is_rejected()
    {
        var (parameters, signing) = CreateValidationPair(
            issuer: "https://keycloak.example.com/realms/hrmanagement",
            audience: "hr-gateway-client");
        using var other = RSA.Create(2048);
        var wrongKey = new SigningCredentials(new RsaSecurityKey(other), SecurityAlgorithms.RsaSha256);
        var forged = CreateToken(
            wrongKey,
            issuer: "https://keycloak.example.com/realms/hrmanagement",
            audience: "hr-gateway-client");
        var result = await new JwtSecurityTokenHandler().ValidateTokenAsync(forged, parameters);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Exception);
    }

    private static (TokenValidationParameters Parameters, SigningCredentials Signing) CreateValidationPair(
        string issuer,
        string audience)
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var signing = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
        return (parameters, signing);
    }

    private static string CreateToken(
        SigningCredentials signing,
        string issuer,
        string audience,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var nbf = notBefore ?? DateTime.UtcNow.AddMinutes(-1);
        var exp = expires ?? DateTime.UtcNow.AddMinutes(5);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore: nbf,
            expires: exp,
            signingCredentials: signing);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "IAM_001_JWT_KEYCLOAK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "iam", "realm-rebuild-checklist.md")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "iam001-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
