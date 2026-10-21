using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace HRManagement.UnitTests;

public class FineGrainedPolicyTests
{
    private IAuthorizationService BuildAuthService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAuthorizationService>();
    }

    private ClaimsPrincipal CreateUser(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim(ClaimTypes.NameIdentifier, "test-user"))
            .ToList();
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal CreateServiceUser(string clientId, params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new("azp", clientId),
            new("preferred_username", $"service-account-{clientId}"),
            new("scope", string.Join(' ', scopes))
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task HrCoreReadOnly_AdminRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.Admin);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreReadOnly);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreReadOnly_HrCoreRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCore);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreReadOnly);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreReadOnly_SupervisoryRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCoreSupervisory);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreReadOnly);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreReadOnly_ExecutiveRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCoreExecutive);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreReadOnly);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreReadOnly_ManagementApproverRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrManagementApprover);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreReadOnly);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ModuleHrCore_ManagementApproverRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrManagementApprover);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.ModuleHrCore);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ModuleHrCore_ServiceWorkflowScope_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateServiceUser(ServiceClientIds.AdminLogistics, ServiceScopes.HrCoreWorkflow);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.ModuleHrCore);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreWrite_ServiceWorkflowScope_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateServiceUser(ServiceClientIds.AdminLogistics, ServiceScopes.HrCoreWorkflow);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreWrite);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreBranchOperation_ManagementApproverRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrManagementApprover);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreBranchOperation);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreBranchOperation_ServiceWorkflowScope_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateServiceUser(ServiceClientIds.AdminLogistics, ServiceScopes.HrCoreWorkflow);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreBranchOperation);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreReadOnly_UnrelatedRole_Fails()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.Payroll);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreReadOnly);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreWrite_AdminRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.Admin);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreWrite);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreWrite_HrCoreRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCore);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreWrite);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreWrite_SupervisoryRole_Fails()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCoreSupervisory);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreWrite);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreWrite_ExecutiveRole_Fails()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCoreExecutive);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreWrite);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreDelete_AdminRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.Admin);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreDelete);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreDelete_HrCoreRole_Fails()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCore);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreDelete);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreApprove_AdminRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.Admin);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreApprove);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreApprove_HrCoreRole_Succeeds()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCore);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreApprove);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HrCoreApprove_SupervisoryRole_Fails()
    {
        var auth = BuildAuthService();
        var user = CreateUser(HrManagementRoles.HrCoreSupervisory);
        var result = await auth.AuthorizeAsync(user, HrManagementPolicies.HrCoreApprove);
        Assert.False(result.Succeeded);
    }
}
