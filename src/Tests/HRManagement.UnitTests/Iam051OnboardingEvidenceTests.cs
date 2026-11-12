using System.Reflection;
using System.Security.Claims;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-51 (§5, D-51-4, D-51-5): agency onboarding is the platform administrator's alone, in the service and at the gateway, and reads no
/// file over HTTP; <c>HrCore.Write</c> admits no service account; evidence ingest takes the clients of <c>Evidence:IngestClients</c>, nobody by
/// default.
/// </summary>
public sealed class Iam051OnboardingEvidenceTests
{
    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim("azp", "hr-gateway-client"))
            .Append(new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString())),
        "TestAuth"));

    private static ClaimsPrincipal Service(string clientId, params string[] scopes) => new(new ClaimsIdentity(
    [
        new Claim("azp", clientId),
        new Claim("preferred_username", "service-account-" + clientId),
        new Claim("scope", string.Join(' ', scopes)),
    ], "Bearer"));

    private static IAuthorizationService Authorization(IDictionary<string, string?>? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build());
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Fact]
    public async Task Onboarding_admits_the_platform_administrator_alone()
    {
        var auth = Authorization();
        Assert.True((await auth.AuthorizeAsync(User(HrManagementRoles.PlatformAdmin), HrManagementPolicies.PlatformAgencyOnboarding)).Succeeded);
        foreach (var role in new[] { HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover, HrManagementRoles.Admin, HrManagementRoles.BranchManager })
            Assert.False((await auth.AuthorizeAsync(User(role), HrManagementPolicies.PlatformAgencyOnboarding)).Succeeded, role);
        Assert.False((await auth.AuthorizeAsync(Service(ServiceClientIds.HrContracts, ServiceScopes.HrCoreWorkflow), HrManagementPolicies.PlatformAgencyOnboarding)).Succeeded);
    }

    [Fact]
    public void The_service_applies_the_onboarding_policy_and_has_no_apply_file()
    {
        var controller = typeof(OnboardingController);
        Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == HrManagementPolicies.PlatformAgencyOnboarding);
        Assert.Equal(HrManagementPolicies.PlatformAgencyOnboarding, controller.GetCustomAttribute<ModulePolicyOverrideAttribute>()!.Policy);
        Assert.DoesNotContain(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.Name == "ApplyFromFile" || m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Any(p => p.Template == "apply-file"));
        // The loader stays for the scripts and the seeds.
        Assert.NotNull(typeof(HR.Core.Infrastructure.Onboarding.AgencyManifestLoader).GetMethod("LoadFromFile"));
    }

    private static async Task<bool> GatewayPassesAsync(string path, ClaimsPrincipal user)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        services.AddHrManagementAuthorizationPolicies();
        var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = provider };
        http.Request.Method = "POST";
        http.Request.Path = path;
        var passed = false;
        await new GatewayPathAuthorizationMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(http, provider.GetRequiredService<IAuthorizationService>());
        return passed;
    }

    [Fact]
    public async Task The_gateway_admits_the_platform_administrator_alone_on_onboarding()
    {
        const string path = "/api/hr-core/api/onboarding/agencies/apply";
        Assert.True(await GatewayPassesAsync(path, User(HrManagementRoles.PlatformAdmin)));
        Assert.False(await GatewayPassesAsync(path, User(HrManagementRoles.HrCore)));
        Assert.False(await GatewayPassesAsync(path, User(HrManagementRoles.HrManagementApprover)));
        Assert.DoesNotContain(GatewayHrCoreActionPolicies.ApiRows, r => r.Template.Contains("apply-file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HrCore_write_admits_no_service_account_and_the_three_endpoints_keep_the_workflow_scope()
    {
        var auth = Authorization();
        var idcard = Service(ServiceClientIds.IdCard, ServiceScopes.HrCoreWorkflow);
        var compliance = Service(ServiceClientIds.Compliance, ServiceScopes.HrCoreLookup, ServiceScopes.HrCoreWorkflow);
        foreach (var service in new[] { idcard, compliance })
        {
            Assert.False((await auth.AuthorizeAsync(service, HrManagementPolicies.HrCoreWrite)).Succeeded);
            Assert.False((await auth.AuthorizeAsync(service, HrManagementPolicies.HrCoreDecision)).Succeeded);
            Assert.True((await auth.AuthorizeAsync(service, HrManagementPolicies.WorkflowExternalStart)).Succeeded);
            Assert.True((await auth.AuthorizeAsync(service, ServicePolicies.CodeGenerate)).Succeeded);
        }

        Assert.True((await auth.AuthorizeAsync(idcard, ServicePolicies.PolicyEnforceS2S)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(User(HrManagementRoles.HrCore), HrManagementPolicies.HrCoreWrite)).Succeeded);
    }

    [Fact]
    public async Task Evidence_ingest_admits_nobody_by_default_and_only_a_listed_client_with_the_scope()
    {
        var publisher = Service(ServiceClientIds.Compliance, ServiceScopes.EvidenceIngest);
        Assert.False((await Authorization().AuthorizeAsync(publisher, ServicePolicies.EvidenceIngest)).Succeeded);

        var configured = Authorization(new Dictionary<string, string?> { [HrManagementPolicies.EvidenceIngestClientsKey + ":0"] = ServiceClientIds.Compliance });
        Assert.True((await configured.AuthorizeAsync(publisher, ServicePolicies.EvidenceIngest)).Succeeded);
        Assert.False((await configured.AuthorizeAsync(Service(ServiceClientIds.IdCard, ServiceScopes.EvidenceIngest), ServicePolicies.EvidenceIngest)).Succeeded);
        Assert.False((await configured.AuthorizeAsync(Service(ServiceClientIds.Compliance, ServiceScopes.HrCoreLookup), ServicePolicies.EvidenceIngest)).Succeeded);
        Assert.False((await configured.AuthorizeAsync(User(HrManagementRoles.PlatformAdmin, HrManagementRoles.Compliance), ServicePolicies.EvidenceIngest)).Succeeded);
    }
}
