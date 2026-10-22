using System.Reflection;
using System.Security.Claims;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-028 (F, PLAN-25 slice): the compliance reviewer reaches RecruitmentAppeals and the work inbox — and nothing else
/// in HR.Core — through the policies, the module filter exemption and the gateway path rules.
/// </summary>
public class Hcm028AuthorizationTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())),
            "test"));

    [Theory]
    [InlineData(HrManagementRoles.Compliance, true)]
    [InlineData(HrManagementRoles.HrCore, true)]
    [InlineData(HrManagementRoles.Admin, true)]
    [InlineData(HrManagementRoles.BranchManager, false)]
    [InlineData(HrManagementRoles.HrManagementApprover, false)]
    [InlineData(HrManagementRoles.HrCoreSupervisory, false)]
    [InlineData(HrManagementRoles.HrEmployee, false)]
    [InlineData(HrManagementRoles.Auditor, false)]
    public async Task HrCoreAppealReview_admits_compliance_and_central_hr_only(string role, bool expected)
    {
        var auth = BuildServices().GetRequiredService<IAuthorizationService>();
        Assert.Equal(expected, (await auth.AuthorizeAsync(User(role), HrManagementPolicies.HrCoreAppealReview)).Succeeded);
    }

    [Theory]
    [InlineData(HrManagementRoles.Compliance, true)]
    [InlineData(HrManagementRoles.BranchManager, true)]
    [InlineData(HrManagementRoles.HrManagementApprover, true)]
    [InlineData(HrManagementRoles.HrCore, true)]
    [InlineData(HrManagementRoles.HrEmployee, false)]
    public async Task HrCoreWorkInbox_is_the_read_roles_plus_compliance(string role, bool expected)
    {
        var auth = BuildServices().GetRequiredService<IAuthorizationService>();
        Assert.Equal(expected, (await auth.AuthorizeAsync(User(role), HrManagementPolicies.HrCoreWorkInbox)).Succeeded);
    }

    [Fact]
    public async Task Compliance_still_fails_the_hr_core_module_and_read_policies()
    {
        var auth = BuildServices().GetRequiredService<IAuthorizationService>();
        Assert.False((await auth.AuthorizeAsync(User(HrManagementRoles.Compliance), HrManagementPolicies.ModuleHrCore)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(User(HrManagementRoles.Compliance), HrManagementPolicies.HrCoreReadOnly)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(User(HrManagementRoles.Compliance), HrManagementPolicies.HrCoreWrite)).Succeeded);
    }

    [Theory]
    [InlineData("RecruitmentAppeals", true)]
    [InlineData("WorkInbox", true)]
    [InlineData("RecruitmentApplications", false)]
    [InlineData("Employees", false)]
    [InlineData("SelectionCommittees", false)]
    public async Task Module_filter_exempts_only_the_appeal_controllers(string controller, bool admitted)
    {
        using var services = BuildServices();
        var httpContext = new DefaultHttpContext { RequestServices = services, User = User(HrManagementRoles.Compliance) };
        var routeData = new RouteData();
        routeData.Values["controller"] = controller;
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor { EndpointMetadata = [] });
        var context = new AuthorizationFilterContext(actionContext, []);

        await new HrModuleAuthorizationFilter(HrManagementPolicies.ModuleHrCore).OnAuthorizationAsync(context);

        if (admitted)
            Assert.Null(context.Result);
        else
            Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Appeal_controllers_carry_their_own_policies()
    {
        Assert.Equal(HrManagementPolicies.HrCoreAppealReview,
            typeof(RecruitmentAppealsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(HrManagementPolicies.HrCoreWorkInbox,
            typeof(WorkInboxController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        foreach (var hrOnly in new[] { "AssignReviewer", "Resubmit" })
            Assert.Equal(HrManagementPolicies.HrCoreWrite,
                typeof(RecruitmentAppealsController).GetMethod(hrOnly)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public void Public_careers_details_route_does_not_capture_controller_index_urls()
    {
        // /ui/hr-core/RecruitmentAppeals and /ui/hr-core/WorkInbox (menu links) must reach their controllers.
        var details = typeof(PublicCareersController).GetMethod(nameof(PublicCareersController.Details))!;
        Assert.Equal("{id:guid}", details.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void Legacy_appeal_actions_are_gone()
    {
        foreach (var removed in new[] { "ReopenAppeal", "ApproveAppealWorkflow", "RejectAppealWorkflow", "DecideAppeal" })
            Assert.Null(typeof(RecruitmentApplicationsController).GetMethod(removed));
        Assert.Null(typeof(HR.Core.Infrastructure.Services.Recruitment.RecruitmentSelectionService).GetMethod("ReopenAppealAsync"));
    }

    [Theory]
    [InlineData("GET", "/ui/hr-core/RecruitmentAppeals", HrManagementPolicies.HrCoreAppealReview)]
    [InlineData("GET", "/ui/hr-core/RecruitmentAppeals/Details/7", HrManagementPolicies.HrCoreAppealReview)]
    [InlineData("POST", "/ui/hr-core/RecruitmentAppeals/Decide", HrManagementPolicies.HrCoreAppealReview)]
    [InlineData("GET", "/ui/hr-core/WorkInbox", HrManagementPolicies.HrCoreWorkInbox)]
    [InlineData("GET", "/ui/hr-core/lib/bootstrap/css/bootstrap.rtl.min.css", HrManagementPolicies.HrCoreLookup)]
    [InlineData("GET", "/ui/hr-core/HR.Core.Web.styles.css", HrManagementPolicies.HrCoreLookup)]
    [InlineData("GET", "/ui/hr-core/RecruitmentApplications/Details/7", HrManagementPolicies.HrCoreReadOnly)]
    [InlineData("POST", "/ui/hr-core/RecruitmentApplications/FileAppeal", HrManagementPolicies.HrCoreWrite)]
    [InlineData("POST", "/ui/hr-core/WorkInbox", HrManagementPolicies.HrCoreWrite)]
    public void Gateway_admits_compliance_only_on_appeal_paths(string method, string path, string expected)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("hr-core", method, new PathString(path), out var policy));
        Assert.Equal(expected, policy);
    }
}
