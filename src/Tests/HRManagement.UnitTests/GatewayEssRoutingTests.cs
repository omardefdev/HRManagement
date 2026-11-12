using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-51 (§1, §2, P-3, P-4): the gateway's self-service rule matches the first segment after <c>/ui/{segment}/</c> exactly, never a
/// substring; under <c>/ui/ess</c> only the named controllers and the static files (GET) pass, and anything else answers 404 with the audit
/// decision <c>Denied:EssNotExposed</c>, before PLAN-50's table or <c>Gateway.ApprovalReach</c> is ever consulted. The directory lookup is for
/// staff and services; HR.Core's static files for any signed-in user.
/// </summary>
public sealed class GatewayEssRoutingTests
{
    private const string Guid1 = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static readonly GatewayEssOptions Defaults = new();

    // --- the plan's table and the look-alikes (the rule alone) ---

    [Theory]
    [InlineData("GET", "/ui/ess/EssPortal", GatewayEssDecision.Ess)]
    [InlineData("POST", "/ui/ess/EssPortal/Leave", GatewayEssDecision.Ess)]
    [InlineData("GET", "/ui/ess/EssTalent/Index", GatewayEssDecision.Ess)]
    [InlineData("POST", "/ui/ess/Shifts/Create", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/hr-core/Shifts/Create/Ess", GatewayEssDecision.NotEss)]
    [InlineData("POST", "/ui/hr-core/EssPortal/Leave", GatewayEssDecision.Ess)]
    [InlineData("GET", "/ui/payroll/Ess/Payslips", GatewayEssDecision.Ess)]
    [InlineData("GET", "/ui/payroll/PayrollRuns/Index/Essam", GatewayEssDecision.NotEss)]
    [InlineData("GET", "/ui/ess/api/internal/employees/" + Guid1 + "/lifecycle-flags", GatewayEssDecision.NotExposed)]
    // look-alikes and odd casing
    [InlineData("GET", "/ui/hr-core/Essentials/Index", GatewayEssDecision.NotEss)]
    [InlineData("GET", "/ui/hr-core/EssPortalX/Index", GatewayEssDecision.NotEss)]
    [InlineData("GET", "/ui/ess/EssPortalX/Index", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/Essentials", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/Ess", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/essportal/Index", GatewayEssDecision.Ess)]
    [InlineData("GET", "/ui/ess/ESSPORTAL", GatewayEssDecision.Ess)]
    [InlineData("GET", "/ui/ess/EssPortal.json", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess//EssPortal", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/hr-training/EssLearning", GatewayEssDecision.NotEss)]
    [InlineData("GET", "/ui/idcard/EssPortal", GatewayEssDecision.NotEss)]
    [InlineData("GET", "/api/ess/EssPortal", GatewayEssDecision.Ess)]
    [InlineData("POST", "/api/ess/Shifts/Create", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/api/hr-core/EssTalent", GatewayEssDecision.Ess)]
    [InlineData("GET", "/api/hr-core/api/lookup/employees", GatewayEssDecision.NotEss)]
    // PLAN-50's approval paths under /ui/ess
    [InlineData("POST", "/ui/ess/EmployeeCorrections/Approve/" + Guid1, GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/Delegations/Create", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/Approvals/Approve", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/Competitions/Finalize/" + Guid1, GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/RecruitmentRequisitions/ApproveWorkflow/" + Guid1, GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/WorkflowApprovals/Approve", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/api/employee-corrections/" + Guid1 + "/approve", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/api/workflow-instances/" + Guid1 + "/approve", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/api/onboarding/agencies/apply", GatewayEssDecision.NotExposed)]
    [InlineData("POST", "/ui/ess/PolicyTemplates/PublishFromUi", GatewayEssDecision.NotExposed)]
    // static files of the ess segment: GET only, no traversal
    [InlineData("GET", "/ui/ess/css/site.css", GatewayEssDecision.StaticFile)]
    [InlineData("HEAD", "/ui/ess/lib/bootstrap/css/bootstrap.rtl.min.css", GatewayEssDecision.StaticFile)]
    [InlineData("GET", "/ui/ess/HR.Core.Web.styles.css", GatewayEssDecision.StaticFile)]
    [InlineData("GET", "/ui/ess/favicon.ico", GatewayEssDecision.StaticFile)]
    [InlineData("POST", "/ui/ess/css/site.css", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/css/", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/css/../Shifts/Create", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/cssx/site.css", GatewayEssDecision.NotExposed)]
    [InlineData("GET", "/ui/ess/api/lookup/employees", GatewayEssDecision.NotExposed)]
    public void The_self_service_rule_matches_the_first_segment_exactly(string method, string path, GatewayEssDecision expected) =>
        Assert.Equal(expected, GatewayEssRouting.Decide(method, path, Defaults));

    [Fact]
    public void The_allowed_controllers_come_from_configuration_and_hr_training_is_one_line_away()
    {
        var options = new GatewayEssOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Ess:AllowedControllers:ess:0"] = "EssPortal",
                ["Gateway:Ess:AllowedControllers:hr-training:0"] = "EssLearning",
            })
            .Build()
            .GetSection(GatewayEssOptions.SectionName)
            .Bind(options);

        Assert.Equal(GatewayEssDecision.Ess, GatewayEssRouting.Decide("GET", "/ui/hr-training/EssLearning", options));
        Assert.Equal(GatewayEssDecision.NotExposed, GatewayEssRouting.Decide("GET", "/ui/ess/EssTalent", options));
        Assert.Equal(GatewayEssDecision.NotEss, GatewayEssRouting.Decide("GET", "/ui/hr-core/EssTalent", options));
    }

    [Fact]
    public void A_configuration_without_a_usable_ess_entry_exposes_nothing_under_ui_ess()
    {
        // Review L1: the ess segment never falls back to its module policy (the old catch-all), whatever the configuration says.
        var options = new GatewayEssOptions();
        options.AllowedControllers["hr-core"] = ["EssPortal"];
        Assert.Equal(GatewayEssDecision.NotExposed, GatewayEssRouting.Decide("GET", "/ui/ess/EssPortal", options));
        Assert.Equal(GatewayEssDecision.NotExposed, GatewayEssRouting.Decide("POST", "/ui/ess/Shifts/Create", options));
        Assert.Equal(GatewayEssDecision.Ess, GatewayEssRouting.Decide("GET", "/ui/hr-core/EssPortal", options));

        options.AllowedControllers["ess"] = [];
        Assert.Equal(GatewayEssDecision.NotExposed, GatewayEssRouting.Decide("GET", "/ui/ess/EssPortal", options));
        Assert.Equal(GatewayEssDecision.StaticFile, GatewayEssRouting.Decide("GET", "/ui/ess/css/site.css", options));
    }

    [Fact]
    public void The_gateway_holds_no_substring_match_of_Ess()
    {
        var source = global::System.IO.File.ReadAllText(HRManagement.TestSupport.RepoPaths.Resolve(
            "src", "Shared", "HRManagement.ServiceDefaults", "GatewayPathAuthorizationMiddleware.cs"));
        Assert.DoesNotContain("\"/Ess\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TryGetEssPolicy", source, StringComparison.Ordinal);
    }

    // --- the middleware: status codes, the audit decision, and the order of the rules ---

    private static ClaimsPrincipal User(bool authenticated, params string[] roles)
    {
        if (!authenticated)
            return new ClaimsPrincipal(new ClaimsIdentity());
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "user"),
            new(TenantContext.TenantClaimType, Guid.NewGuid().ToString()),
            new("azp", "hr-gateway-client"),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<(int Status, bool Forwarded, object? AuditDecision, string? ContentType)> InvokeAsync(string method, string path, ClaimsPrincipal user)
    {
        var services = new ServiceCollection();
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoResultHandler>("Test", _ => { });
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = provider, User = user };
        ctx.Request.Method = method;
        ctx.Request.Path = path;

        var forwarded = false;
        var middleware = new GatewayPathAuthorizationMiddleware(_ =>
        {
            forwarded = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(ctx, provider.GetRequiredService<IAuthorizationService>(),
            essOptions: Options.Create(new GatewayEssOptions()));
        ctx.Items.TryGetValue(GatewayEssOptions.AuditDecisionItemKey, out var decision);
        return (ctx.Response.StatusCode, forwarded, decision, ctx.Response.ContentType);
    }

    [Fact]
    public async Task A_supervisory_employee_reaches_no_write_through_the_ess_segment()
    {
        var user = User(true, HrManagementRoles.HrEmployee, HrManagementRoles.HrCoreSupervisory);

        var ess = await InvokeAsync("POST", "/ui/ess/Shifts/Create", user);
        Assert.Equal(404, ess.Status);
        // The 404 carries its own body, so the gateway's status pages (GET only) never turn a POST into 405 (AC-IAM-51a).
        Assert.Equal("text/plain; charset=utf-8", ess.ContentType);
        Assert.False(ess.Forwarded);
        Assert.Equal(GatewayEssOptions.NotExposedDecision, ess.AuditDecision);

        var hrCore = await InvokeAsync("POST", "/ui/hr-core/Shifts/Create/Ess", user);
        Assert.Equal(403, hrCore.Status);
        Assert.False(hrCore.Forwarded);

        var portal = await InvokeAsync("GET", "/ui/ess/EssPortal", user);
        Assert.True(portal.Forwarded);
    }

    [Theory]
    [InlineData("/ui/ess/EmployeeCorrections/Approve/" + Guid1)]
    [InlineData("/ui/ess/Delegations/Create")]
    [InlineData("/ui/ess/RecruitmentRequisitions/ApproveWorkflow/" + Guid1)]
    public async Task An_approver_employee_reaches_no_approval_through_the_ess_segment(string path)
    {
        var user = User(true, HrManagementRoles.HrEmployee, HrManagementRoles.HrManagementApprover);
        var result = await InvokeAsync("POST", path, user);
        Assert.Equal(404, result.Status);
        Assert.False(result.Forwarded);
        Assert.Equal(GatewayEssOptions.NotExposedDecision, result.AuditDecision);

        // PLAN-50's own path still reaches the service, which decides.
        var own = await InvokeAsync("POST", path.Replace("/ui/ess/", "/ui/hr-core/", StringComparison.Ordinal), user);
        Assert.True(own.Forwarded);
    }

    [Fact]
    public async Task An_anonymous_caller_gets_the_same_404_under_the_ess_segment()
    {
        var result = await InvokeAsync("GET", "/ui/ess/api/internal/employees/" + Guid1 + "/lifecycle-flags", User(false));
        Assert.Equal(404, result.Status);
        Assert.False(result.Forwarded);
    }

    [Theory]
    [InlineData("/ui/hr-core/css/site.css")]
    [InlineData("/ui/hr-core/lib/bootstrap/css/bootstrap.rtl.min.css")]
    [InlineData("/ui/ess/js/site.js")]
    public async Task The_static_files_admit_an_employee_and_a_marker_but_not_an_anonymous_caller(string path)
    {
        Assert.True((await InvokeAsync("GET", path, User(true, HrManagementRoles.HrEmployee))).Forwarded);
        Assert.True((await InvokeAsync("GET", path, User(true, HrManagementRoles.HrExamMarker))).Forwarded);
        Assert.False((await InvokeAsync("GET", path, User(false))).Forwarded);
    }

    [Theory]
    [InlineData("GET", "/api/hr-core/api/lookup/employees?q=a")]
    [InlineData("POST", "/ui/hr-core/api/lookup/employees/by-ids")]
    public async Task The_directory_admits_staff_and_refuses_the_employee_and_the_marker(string method, string path)
    {
        Assert.Equal(403, (await InvokeAsync(method, path, User(true, HrManagementRoles.HrEmployee, "offline_access", "uma_authorization", "default-roles-hrmanagement"))).Status);
        Assert.Equal(403, (await InvokeAsync(method, path, User(true, HrManagementRoles.HrExamMarker))).Status);
        Assert.True((await InvokeAsync(method, path, User(true, HrManagementRoles.PayrollPoster))).Forwarded);
        Assert.True((await InvokeAsync(method, path, User(true, HrManagementRoles.HrContracts))).Forwarded);
    }

    [Fact]
    public async Task A_patch_on_hr_core_meets_the_write_policy_at_the_gateway()
    {
        var reader = await InvokeAsync("PATCH", "/api/hr-core/api/employees/" + Guid1, User(true, HrManagementRoles.HrCoreSupervisory));
        Assert.Equal(403, reader.Status);
        var writer = await InvokeAsync("PATCH", "/api/hr-core/api/employees/" + Guid1, User(true, HrManagementRoles.HrCore));
        Assert.True(writer.Forwarded);
    }

    private sealed class NoResultHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    // --- HrCore.Lookup, the policy itself ---

    private static async Task<bool> LookupAsync(ClaimsPrincipal user, IDictionary<string, string?>? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (configuration is not null)
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddHrManagementAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, HrManagementPolicies.HrCoreLookup);
        return result.Succeeded;
    }

    private static ClaimsPrincipal Service(string clientId, string scope) => new(new ClaimsIdentity(
    [
        new Claim("azp", clientId),
        new Claim("preferred_username", "service-account-" + clientId),
        new Claim("scope", scope),
    ], "Bearer"));

    [Fact]
    public async Task The_lookup_policy_counts_only_platform_staff_roles()
    {
        Assert.False(await LookupAsync(User(true, "offline_access", "uma_authorization", "default-roles-hrmanagement")));
        Assert.False(await LookupAsync(User(true, HrManagementRoles.HrEmployee)));
        Assert.False(await LookupAsync(User(true, HrManagementRoles.HrExamMarker)));
        Assert.False(await LookupAsync(User(true, "applicant")));
        Assert.True(await LookupAsync(User(true, HrManagementRoles.HrEmployee, HrManagementRoles.HrCoreSupervisory)));
        Assert.True(await LookupAsync(User(true, HrManagementRoles.Compliance)));
        Assert.False(await LookupAsync(User(false)));

        // A staff role without a valid tenant_id is not interactive staff (PLAN-50's definition).
        var noTenant = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)], "TestAuth"));
        Assert.False(await LookupAsync(noTenant));
    }

    [Fact]
    public async Task The_lookup_policy_admits_a_service_with_the_lookup_scope_only()
    {
        Assert.True(await LookupAsync(Service(ServiceClientIds.HrContracts, ServiceScopes.HrCoreLookup)));
        Assert.False(await LookupAsync(Service(ServiceClientIds.IdCard, ServiceScopes.HrCoreWorkflow)));
        Assert.False(await LookupAsync(Service(ServiceClientIds.Payroll, "openid")));
    }

    [Fact]
    public async Task The_non_staff_roles_come_from_configuration()
    {
        var config = new Dictionary<string, string?> { ["Authorization:NonStaffRoles:0"] = HrManagementRoles.HrEmployee, ["Authorization:NonStaffRoles:1"] = HrManagementRoles.Auditor };
        Assert.False(await LookupAsync(User(true, HrManagementRoles.Auditor), config));
        Assert.True(await LookupAsync(User(true, HrManagementRoles.HrExamMarker), config));
    }
}
