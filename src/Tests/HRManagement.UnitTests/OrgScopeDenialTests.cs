using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class OrgScopeDenialTests
{
    [Fact]
    public void ForbidIfOrgUnitOutOfScope_records_denial_in_http_context()
    {
        var tenantId = Guid.NewGuid();
        var scopeOrgUnitId = Guid.NewGuid();
        var resourceOrgUnitId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(TenantContext.TenantClaimType, tenantId.ToString("D")),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, scopeOrgUnitId.ToString("D")),
            new Claim(ClaimTypes.Name, "mgr")
        ], "test"));

        var controller = new TestController { ControllerContext = new ControllerContext { HttpContext = http } };
        var scope = OrgAccessScope.Restricted(new HashSet<Guid> { scopeOrgUnitId });

        var result = controller.ForbidIfOrgUnitOutOfScope(
            scope,
            resourceOrgUnitId,
            resourceType: "Employee",
            resourceId: employeeId.ToString("D"));

        Assert.IsType<ForbidResult>(result);
        var denial = OrgScopeDenialContext.Get(http);
        Assert.NotNull(denial);
        Assert.Equal(tenantId, denial!.TenantId);
        Assert.Equal(scopeOrgUnitId, denial.ScopeOrgUnitId);
        Assert.Equal("org-scope-denied", denial.Reason);
        Assert.Equal("Employee", denial.ResourceType);
        Assert.Equal(employeeId.ToString("D"), denial.ResourceId);
    }

    private sealed class TestController : Controller;
}

public class GatewayTenantBarrierTests
{
    [Fact]
    public async Task Protected_api_without_tenant_claim_returns_403()
    {
        var (status, nextCalled) = await InvokeWithClaimsAsync(
            "/api/hr-core/employees",
            [new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)]);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task Protected_api_with_valid_tenant_claim_passes()
    {
        var tenantId = Guid.NewGuid();
        var (_, nextCalled) = await InvokeWithClaimsAsync(
            "/api/hr-core/employees",
            [
                new Claim(ClaimTypes.Role, HrManagementRoles.HrCore),
                new Claim(TenantContext.TenantClaimType, tenantId.ToString("D"))
            ]);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Platform_admin_without_tenant_claim_passes_gateway()
    {
        var (_, nextCalled) = await InvokeWithClaimsAsync(
            "/api/hr-core/employees",
            [
                new Claim(ClaimTypes.Role, HrManagementRoles.PlatformAdmin),
                new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)
            ]);

        Assert.True(nextCalled);
    }

    private static async Task<(int statusCode, bool nextCalled)> InvokeWithClaimsAsync(
        string path,
        Claim[] claims)
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        ctx.Request.Path = path;

        var services = new ServiceCollection();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        services.AddLogging();
        ctx.RequestServices = services.BuildServiceProvider();

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
}
