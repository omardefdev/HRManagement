using System.Security.Claims;
using HRManagement.ServiceDefaults;
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
    public async Task Non_api_non_ui_path_passes_through()
    {
        var (status, nextCalled) = await InvokeAsync(
            "/swagger/index.html", true);

        Assert.True(nextCalled);
    }
}
