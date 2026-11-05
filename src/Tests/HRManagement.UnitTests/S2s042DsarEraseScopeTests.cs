using System.Reflection;
using System.Security.Claims;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §5 (G-IAM-13): reading a DSAR subject erases nothing.</summary>
public class S2s042DsarEraseScopeTests
{
    private static async Task<bool> Passes(string policy, params Claim[] claims)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddServiceToServiceAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        return (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, resource: null, policy)).Succeeded;
    }

    [Fact]
    public async Task The_read_scope_does_not_pass_the_erase_policy()
    {
        Assert.False(await Passes(ServicePolicies.DsarSubjectErase, new Claim("azp", "svc-compliance"), new Claim("scope", "dsar.subject.read")));
    }

    [Fact]
    public async Task The_erase_scope_passes_for_svc_compliance_alone()
    {
        Assert.True(await Passes(ServicePolicies.DsarSubjectErase, new Claim("azp", "svc-compliance"), new Claim("scope", "dsar.subject.erase")));
        Assert.False(await Passes(ServicePolicies.DsarSubjectErase, new Claim("azp", "svc-payroll"), new Claim("scope", "dsar.subject.erase")));
    }

    [Fact]
    public async Task A_user_token_never_passes_even_with_the_scope()
    {
        Assert.False(await Passes(ServicePolicies.DsarSubjectErase, new Claim("azp", "hr-gateway-client"), new Claim("scope", "dsar.subject.erase")));
    }

    [Fact]
    public void Anonymize_carries_the_erase_policy()
    {
        var action = typeof(DsarInternalController).GetMethod(nameof(DsarInternalController.AnonymizeCandidate))!;

        Assert.Contains(action.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == ServicePolicies.DsarSubjectErase);
    }
}
