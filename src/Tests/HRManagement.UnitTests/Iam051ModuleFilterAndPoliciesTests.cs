using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-51 (§4, P-6, D-51-2, D-IAM-4): the module filter has no name list; an override replaces the module policy; a write with no policy
/// of its own also meets the module's write policy; the module policies come from configuration with today's roles; hr-admin is excluded
/// from the clinical and payroll posting policies in Production, Staging and the prod-like stack, never in demo mode.
/// </summary>
public sealed class Iam051ModuleFilterAndPoliciesTests
{
    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")))
            .Append(new Claim("azp", "hr-gateway-client"))
            .Append(new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString())),
        "TestAuth"));

    private static IServiceProvider Services(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    private static async Task<IActionResult?> RunAsync(
        string method, string controller, ClaimsPrincipal user, string module, string? write, params object[] metadata)
    {
        var http = new DefaultHttpContext { User = user, RequestServices = Services() };
        http.Request.Method = method;
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(new RouteValueDictionary { ["controller"] = controller }),
                new ActionDescriptor { EndpointMetadata = metadata.ToList() }),
            new List<IFilterMetadata>());
        await new HrModuleAuthorizationFilter(module, write).OnAuthorizationAsync(context);
        return context.Result;
    }

    // --- the filter ---

    [Theory]
    [InlineData("Essentials")]
    [InlineData("EssPortal")]
    [InlineData("LookupApi")]
    [InlineData("Onboarding")]
    [InlineData("WorkflowDefinitions")]
    public async Task No_controller_name_skips_the_module_policy(string controller)
    {
        // An employee alone, on a controller whose name the old list matched: without an override, the module policy decides.
        Assert.IsType<ForbidResult>(await RunAsync("GET", controller, User(HrManagementRoles.HrEmployee),
            HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite));
    }

    [Fact]
    public async Task The_override_replaces_the_module_policy_as_the_name_list_did()
    {
        var ess = new ModulePolicyOverrideAttribute(HrManagementPolicies.ModuleEss);
        var essPolicy = new AuthorizeAttribute { Policy = HrManagementPolicies.ModuleEss };
        Assert.Null(await RunAsync("GET", "EssPortal", User(HrManagementRoles.HrEmployee), HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, essPolicy, ess));
        Assert.Null(await RunAsync("POST", "EssPortal", User(HrManagementRoles.HrEmployee), HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, essPolicy, ess));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_write_that_names_no_policy_meets_the_module_write_policy(string method)
    {
        var reader = User(HrManagementRoles.HrCoreSupervisory);
        Assert.Null(await RunAsync("GET", "Shifts", reader, HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite));
        Assert.IsType<ForbidResult>(await RunAsync(method, "Shifts", reader, HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite));
        Assert.Null(await RunAsync(method, "Shifts", User(HrManagementRoles.HrCore), HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite));

        // HR.Contracts: the management approver reads the module, and writes nothing in it.
        var approver = User(HrManagementRoles.HrManagementApprover);
        Assert.Null(await RunAsync("GET", "Contracts", approver, HrManagementPolicies.ModuleHrContracts, HrManagementPolicies.HrContractsWrite));
        Assert.IsType<ForbidResult>(await RunAsync(method, "Contracts", approver, HrManagementPolicies.ModuleHrContracts, HrManagementPolicies.HrContractsWrite));
    }

    [Fact]
    public async Task A_write_that_names_its_policy_is_not_given_the_default_on_top()
    {
        var own = new AuthorizeAttribute { Policy = HrManagementPolicies.HrCoreReadOnly };
        Assert.Null(await RunAsync("POST", "Reports", User(HrManagementRoles.HrCoreSupervisory), HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, own));
    }

    [Fact]
    public async Task Under_an_override_a_write_that_names_no_policy_still_meets_the_write_policy()
    {
        var inbox = new ModulePolicyOverrideAttribute(HrManagementPolicies.HrCoreWorkInbox);
        var compliance = User(HrManagementRoles.Compliance);
        Assert.Null(await RunAsync("GET", "WorkInbox", compliance, HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, inbox));
        Assert.IsType<ForbidResult>(await RunAsync("POST", "WorkInbox", compliance, HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, inbox));
    }

    [Fact]
    public async Task A_service_policy_and_an_anonymous_action_keep_skipping_the_filter()
    {
        var service = new AuthorizeAttribute { Policy = ServicePolicies.CodeGenerate };
        Assert.Null(await RunAsync("POST", "CodeGenerationInternal", User(), HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, service));
        Assert.Null(await RunAsync("POST", "PublicCareers", new ClaimsPrincipal(new ClaimsIdentity()), HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite, new AllowAnonymousAttribute()));
    }

    [Fact]
    public void The_filter_has_no_name_list_left()
    {
        var source = global::System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Shared", "HRManagement.ServiceDefaults", "HrModuleAuthorizationFilter.cs"));
        Assert.DoesNotContain("RouteData.Values[\"controller\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartsWith(\"Ess\"", source, StringComparison.Ordinal);
    }

    // --- D-51-2: the module policies, today's roles by default, configurable ---

    [Theory]
    [InlineData(HrManagementPolicies.HrContractsWrite, HrManagementRoles.HrContracts, HrManagementRoles.HrManagementApprover)]
    [InlineData(HrManagementPolicies.HrTrainingWrite, HrManagementRoles.HrTraining, HrManagementRoles.HrCore)]
    [InlineData(HrManagementPolicies.HrTrainingApprove, HrManagementRoles.HrTraining, HrManagementRoles.HrEmployee)]
    [InlineData(HrManagementPolicies.IdCardApprove, HrManagementRoles.IdCard, HrManagementRoles.HrCore)]
    [InlineData(HrManagementPolicies.RecordsArchiveApprove, HrManagementRoles.RecordsArchive, HrManagementRoles.Compliance)]
    [InlineData(HrManagementPolicies.DevAnalyticsWrite, HrManagementRoles.PlatformAdmin, HrManagementRoles.HrCore)]
    [InlineData(HrManagementPolicies.HrHealthWrite, HrManagementRoles.HrHealthClinical, HrManagementRoles.HrEmployee)]
    [InlineData(HrManagementPolicies.HrCoreDecision, HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover)]
    public async Task A_module_policy_admits_todays_roles_by_default(string policy, string admitted, string refused)
    {
        var auth = Services().GetRequiredService<IAuthorizationService>();
        Assert.True((await auth.AuthorizeAsync(User(admitted), policy)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(User(HrManagementRoles.Admin), policy)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(User(refused), policy)).Succeeded);
    }

    [Fact]
    public async Task A_module_policy_takes_its_roles_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authorization:Policies:HrTraining.Approve:Roles:0"] = "training-approver",
        }).Build();
        var auth = Services(configuration).GetRequiredService<IAuthorizationService>();
        Assert.True((await auth.AuthorizeAsync(User("training-approver"), HrManagementPolicies.HrTrainingApprove)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(User(HrManagementRoles.HrTraining), HrManagementPolicies.HrTrainingApprove)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(User(HrManagementRoles.HrTraining), HrManagementPolicies.HrTrainingWrite)).Succeeded);
    }

    // --- D-IAM-4: hr-admin out of the clinical and payroll posting policies, outside demo mode ---

    private static IConfiguration ServiceConfiguration(string service, string? environment, params (string Key, string Value)[] extra)
    {
        var dir = RepoPaths.Resolve("src", "Services", service == "payroll" ? "Payroll" : "HRHealth",
            service == "payroll" ? "Payroll.Web" : "HR.Health.Web", "appsettings.json");
        var builder = new ConfigurationBuilder().AddJsonFile(dir, optional: false);
        if (environment is not null)
            builder.AddJsonFile(Path.Combine(Path.GetDirectoryName(dir)!, $"appsettings.{environment}.json"), optional: false);
        builder.AddInMemoryCollection(extra.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)));
        return builder.Build();
    }

    /// <summary>The prod-like stack's mirror (it runs ASPNETCORE_ENVIRONMENT=Acceptance and loads no Production file).</summary>
    private static IConfiguration ProdLikeConfiguration(string composeService)
    {
        var compose = global::System.IO.File.ReadAllLines(RepoPaths.Resolve("docker-compose.acceptance-prodlike.yml"));
        var values = new Dictionary<string, string?>();
        var inService = false;
        foreach (var line in compose)
        {
            if (line.Length > 2 && line[0] == ' ' && line[1] == ' ' && line[2] != ' ')
                inService = line.Trim() == composeService + ":";
            var trimmed = line.Trim();
            if (inService && trimmed.StartsWith("Authorization__", StringComparison.Ordinal))
            {
                var parts = trimmed.Split(':', 2);
                values[parts[0].Replace("__", ":", StringComparison.Ordinal)] = parts[1].Trim().Trim('"');
            }
        }

        Assert.NotEmpty(values);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static TheoryData<string, string, string> Excluded() => new()
    {
        { "payroll", "payroll-web", HrManagementPolicies.PayrollPost },
        { "payroll", "payroll-web", HrManagementPolicies.PayrollReverse },
        { "hr-health", "hr-health-web", HrManagementPolicies.HrHealthClinical },
    };

    private static async Task<bool> AdminPassesAsync(IConfiguration configuration, string policy) =>
        (await Services(configuration).GetRequiredService<IAuthorizationService>().AuthorizeAsync(User(HrManagementRoles.Admin), policy)).Succeeded;

    [Theory]
    [MemberData(nameof(Excluded))]
    public async Task Hr_admin_is_excluded_in_production_staging_and_the_prodlike_stack(string service, string composeService, string policy)
    {
        Assert.False(await AdminPassesAsync(ServiceConfiguration(service, "Production"), policy));
        Assert.False(await AdminPassesAsync(ServiceConfiguration(service, "Staging"), policy));
        Assert.False(await AdminPassesAsync(ProdLikeConfiguration(composeService), policy));
    }

    [Theory]
    [MemberData(nameof(Excluded))]
    public async Task Hr_admin_keeps_the_policy_in_demo_mode_and_in_development(string service, string composeService, string policy)
    {
        _ = composeService;
        Assert.True(await AdminPassesAsync(ServiceConfiguration(service, "Production", ("Deployment:Mode", "demo")), policy));
        Assert.True(await AdminPassesAsync(ServiceConfiguration(service, "Development"), policy));
        Assert.True(await AdminPassesAsync(ServiceConfiguration(service, null), policy));
    }

    [Theory]
    [MemberData(nameof(Excluded))]
    public async Task The_specialist_roles_keep_their_policy_where_hr_admin_is_excluded(string service, string composeService, string policy)
    {
        _ = composeService;
        var role = policy == HrManagementPolicies.HrHealthClinical ? HrManagementRoles.HrHealthClinical : HrManagementRoles.PayrollPoster;
        var auth = Services(ServiceConfiguration(service, "Production")).GetRequiredService<IAuthorizationService>();
        Assert.True((await auth.AuthorizeAsync(User(role), policy)).Succeeded);
    }
}
