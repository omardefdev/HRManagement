using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class GatewayPathAuthorizationMiddlewareTests
{
    private static (DefaultHttpContext ctx, int statusCode, bool nextCalled) CreateContext(
        string path,
        bool isAuthenticated,
        params string[] roles)
    {
        var claims = new List<Claim>();
        if (isAuthenticated)
        {
            claims.Add(new Claim(ClaimTypes.Name, "testuser"));
            claims.Add(new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString()));
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = isAuthenticated
            ? new ClaimsIdentity(claims, "TestAuth")
            : new ClaimsIdentity();

        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(identity);
        ctx.Request.Path = path;

        var services = new ServiceCollection();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        services.AddLogging();
        ctx.RequestServices = services.BuildServiceProvider();

        return (ctx, 0, false);
    }

    private static async Task<(int statusCode, bool nextCalled)> InvokeAsync(
        string path,
        bool isAuthenticated,
        params string[] roles)
    {
        var (ctx, _, _) = CreateContext(path, isAuthenticated, roles);
        var nextCalled = false;

        var middleware = new GatewayPathAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        await middleware.InvokeAsync(ctx, authorization);

        return (ctx.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task HrCore_path_allows_hr_core_role()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/api/hr-core/employees", true, HrManagementRoles.HrCore);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HrCore_path_allows_admin_role()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/api/hr-core/employees", true, HrManagementRoles.Admin);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Payroll_ui_path_requires_payroll_role()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/ui/payroll/runs", true, HrManagementRoles.Payroll);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Unknown_path_passes_through()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/health", false);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HrContracts_api_path_requires_hr_contracts_role()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/api/hr-contracts/contracts", true, HrManagementRoles.HrContracts);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HrContracts_api_path_write_requires_hr_contracts_role()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/api/hr-contracts/contracts", true);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Authenticated_without_role_gets_403()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/api/hr-core/employees", true);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Upstream_health_path_allows_authenticated_user()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/health/upstreams/hr-core", true, HrManagementRoles.Admin);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Upstream_health_path_allows_localhost_probe_without_auth()
    {
        var (ctx, _, _) = CreateContext("/health/upstreams/hr-core", isAuthenticated: false);
        ctx.Request.Host = new HostString("localhost", 8080);
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        ctx.Connection.LocalIpAddress = System.Net.IPAddress.Loopback;

        var nextCalled = false;
        var middleware = new GatewayPathAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        await middleware.InvokeAsync(ctx, authorization);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Explicitly_anonymous_proxy_endpoint_bypasses_path_policy()
    {
        var (ctx, _, _) = CreateContext("/health/upstreams/hr-core", isAuthenticated: false);
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            "anonymous upstream health"));

        var nextCalled = false;
        var middleware = new GatewayPathAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        await middleware.InvokeAsync(ctx, authorization);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Non_api_non_ui_path_passes_through()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/swagger/index.html", true);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/hr-core/api/onboarding/agencies/apply", HrManagementPolicies.HrCoreApprove)]
    [InlineData("/api/hr-core/api/policies/enforce", HrManagementPolicies.HrCoreBranchOperation)]
    [InlineData("/api/hr-core/api/workflow-instances/00000000-0000-0000-0000-000000000001/approve", HrManagementPolicies.HrCoreBranchOperation)]
    [InlineData("/ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/00000000-0000-0000-0000-000000000001", HrManagementPolicies.HrCoreBranchOperation)]
    [InlineData("/ui/hr-core/RecruitmentRequisitions/RejectWorkflow/00000000-0000-0000-0000-000000000001", HrManagementPolicies.HrCoreBranchOperation)]
    public void HrCore_special_write_paths_use_their_narrow_policy(string path, string expectedPolicy)
    {
        var matched = GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(
            "hr-core", HttpMethods.Post, new PathString(path), out var policy);

        Assert.True(matched);
        Assert.Equal(expectedPolicy, policy);
    }

    [Fact]
    public void Tenant_quarantine_read_uses_platform_approval_policy()
    {
        var matched = GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(
            "hr-core",
            HttpMethods.Get,
            new PathString("/api/hr-core/api/tenant-quarantine"),
            out var policy);

        Assert.True(matched);
        Assert.Equal(HrManagementPolicies.TenantQuarantineReconcile, policy);
    }

    [Fact]
    public async Task Branch_manager_can_run_scoped_operations_but_cannot_publish_policy()
    {
        var (ctx, _, _) = CreateContext(
            "/api/hr-core/api/policies/enforce", true, HrManagementRoles.BranchManager);
        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();

        var operational = await authorization.AuthorizeAsync(
            ctx.User, resource: null, HrManagementPolicies.HrCoreBranchOperation);
        var publish = await authorization.AuthorizeAsync(
            ctx.User, resource: null, HrManagementPolicies.HrCoreApprove);

        Assert.True(operational.Succeeded);
        Assert.False(publish.Succeeded);
    }

    [Theory]
    [InlineData("/api/hr-core/api/lookup/employees", "GET")]
    [InlineData("/ui/hr-core/api/lookup/employees/by-ids", "POST")]
    public void HrCore_lookup_paths_use_shared_lookup_policy(string path, string method)
    {
        var matched = GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(
            "hr-core", method, new PathString(path), out var policy);

        Assert.True(matched);
        Assert.Equal(HrManagementPolicies.HrCoreLookup, policy);
    }

    [Fact]
    public async Task HrCore_lookup_post_allows_module_only_contracts_user()
    {
        var (ctx, _, _) = CreateContext(
            "/ui/hr-core/api/lookup/employees/by-ids", true, HrManagementRoles.HrContracts);
        ctx.Request.Method = HttpMethods.Post;

        var nextCalled = false;
        var middleware = new GatewayPathAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var authorization = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
        await middleware.InvokeAsync(ctx, authorization);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/files", true)]
    [InlineData("/api/hr-core", false)]
    [InlineData("/ui/payroll", false)]
    public void Gateway_preserves_only_upstream_owned_public_api_prefixes(string prefix, bool expected)
    {
        Assert.Equal(expected, GatewayAccessTokenTransformProvider.ShouldPreserveApiPrefix(prefix));
    }
}
