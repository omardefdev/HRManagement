using System.Security.Claims;
using System.Text.Encodings.Web;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>
/// Test auth that accepts optional <c>X-Test-Tenant-Id</c> and <c>X-Test-Roles</c> headers
/// so API penetration tests can act as Tenant A vs Tenant B (TEN-003).
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string TenantHeader = "X-Test-Tenant-Id";
    public const string RolesHeader = "X-Test-Roles";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "integration-test-user"),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
        };

        if (Request.Headers.TryGetValue(TenantHeader, out var tenantHeader) &&
            Guid.TryParse(tenantHeader.ToString(), out var tenantId))
        {
            claims.Add(new Claim(TenantContext.TenantClaimType, tenantId.ToString("D")));
        }
        else
        {
            // Default platform tenant so existing integration tests keep a valid insert stamp.
            claims.Add(new Claim(TenantContext.TenantClaimType, TenantDefaults.DefaultTenantId.ToString("D")));
        }

        var roles = Request.Headers.TryGetValue(RolesHeader, out var rolesHeader)
            ? rolesHeader.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [HrManagementRoles.Admin, HrManagementRoles.HrCore];

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
internal static class IntegrationTestAuthExtensions
{
    public static IServiceCollection AddIntegrationTestAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "IntegrationTest";
                options.DefaultChallengeScheme = "IntegrationTest";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("IntegrationTest", _ => { });

        return services;
    }
}
