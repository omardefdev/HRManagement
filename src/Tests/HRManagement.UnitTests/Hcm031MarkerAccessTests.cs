using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.Controllers;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P3, AC-HCM-31c, decision 6/J7): the exam marker reaches the marking screens and nothing else in
/// HR.Core, and the marking pages and models carry no applicant identity.
/// </summary>
public sealed class Hcm031MarkerAccessTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal User(Guid? userId, Guid? employeeId, params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r))
                .Append(new Claim(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()))
                .Concat(employeeId is Guid e ? [new Claim("employee_id", e.ToString())] : []),
            "test"));

    [Fact]
    public async Task The_marker_role_passes_only_the_marking_policy()
    {
        var auth = BuildServices().GetRequiredService<IAuthorizationService>();
        var marker = User(null, null, HrManagementRoles.HrExamMarker);

        Assert.True((await auth.AuthorizeAsync(marker, HrManagementPolicies.HrCoreExamMarking)).Succeeded);
        foreach (var policy in new[]
                 {
                     HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreReadOnly, HrManagementPolicies.HrCoreWrite,
                     HrManagementPolicies.HrCoreWorkInbox, HrManagementPolicies.HrCoreAppealReview, HrManagementPolicies.HrCoreApprove
                 })
            Assert.False((await auth.AuthorizeAsync(marker, policy)).Succeeded, policy);

        foreach (var role in new[] { HrManagementRoles.HrCore, HrManagementRoles.Admin, HrManagementRoles.Compliance })
            Assert.False((await auth.AuthorizeAsync(User(null, null, role), HrManagementPolicies.HrCoreExamMarking)).Succeeded, role);
    }

    [Theory]
    [InlineData("ExamMarking", true)]
    [InlineData("Competitions", false)]
    [InlineData("RecruitmentApplications", false)]
    [InlineData("Dashboard", false)]
    [InlineData("Employees", false)]
    public async Task The_module_filter_admits_the_marker_only_on_the_marking_controller(string controller, bool admitted)
    {
        using var services = BuildServices();
        var httpContext = new DefaultHttpContext { RequestServices = services, User = User(null, null, HrManagementRoles.HrExamMarker) };
        var routeData = new RouteData();
        routeData.Values["controller"] = controller;
        var context = new AuthorizationFilterContext(new ActionContext(httpContext, routeData, new ActionDescriptor { EndpointMetadata = [] }), []);

        await new HrModuleAuthorizationFilter(HrManagementPolicies.ModuleHrCore).OnAuthorizationAsync(context);

        if (admitted)
            Assert.Null(context.Result);
        else
            Assert.IsType<ForbidResult>(context.Result);
    }

    [Theory]
    [InlineData("GET", "/ui/hr-core/ExamMarking", HrManagementPolicies.HrCoreExamMarking)]
    [InlineData("GET", "/ui/hr-core/ExamMarking/Paper/7", HrManagementPolicies.HrCoreExamMarking)]
    [InlineData("POST", "/ui/hr-core/ExamMarking/Submit/7", HrManagementPolicies.HrCoreExamMarking)]
    [InlineData("GET", "/ui/hr-core/Competitions/Details/7", HrManagementPolicies.HrCoreReadOnly)]
    [InlineData("GET", "/ui/hr-core/Competitions/ExamSession/7", HrManagementPolicies.HrCoreReadOnly)]
    [InlineData("GET", "/ui/hr-core/RecruitmentApplications/Details/7", HrManagementPolicies.HrCoreReadOnly)]
    [InlineData("POST", "/ui/hr-core/Competitions/SetAttendance/7", HrManagementPolicies.HrCoreWrite)]
    // AC-HCM-31d (found live): the results approval steps are workflow actions, open to hr-management-approver.
    [InlineData("POST", "/ui/hr-core/Competitions/ApproveWorkflow/7", HrManagementPolicies.HrCoreBranchOperation)]
    [InlineData("POST", "/ui/hr-core/Competitions/ApproveResultsWorkflow/7", HrManagementPolicies.HrCoreBranchOperation)]
    [InlineData("POST", "/ui/hr-core/Competitions/RejectResultsWorkflow/7", HrManagementPolicies.HrCoreBranchOperation)]
    public void The_gateway_maps_only_the_marking_paths_to_the_marker_policy(string method, string path, string expected)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("hr-core", method, new PathString(path), out var policy));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public void The_marking_controller_carries_the_marker_policy_and_HR_screens_do_not()
    {
        Assert.Equal(HrManagementPolicies.HrCoreExamMarking, typeof(ExamMarkingController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(HrManagementPolicies.HrCoreReadOnly, typeof(CompetitionsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Null(typeof(ExamMarkingController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.True(HR.Core.Web.Services.ExamMarkerAccess.IsExamMarkerOnly(User(null, null, HrManagementRoles.HrExamMarker)));
        Assert.False(HR.Core.Web.Services.ExamMarkerAccess.IsExamMarkerOnly(User(null, null, HrManagementRoles.HrExamMarker, HrManagementRoles.HrCore)));
    }

    [Fact]
    public void Marker_models_have_no_identity_field()
    {
        var banned = new[] { "Name", "NationalId", "Application", "Channel", "Phone", "Email", "Birth" };
        foreach (var type in new[] { typeof(MarkerPaperView), typeof(MarkerSessionView), typeof(ExamMarkingPaperViewModel), typeof(ExamMarkingSessionViewModel), typeof(ExamMarkingIndexViewModel) })
            foreach (var property in type.GetProperties())
                Assert.DoesNotContain(banned, b => property.Name.Contains(b, StringComparison.Ordinal));
    }

    [Fact]
    public void Marker_views_render_no_applicant_field()
    {
        var root = Path.Combine(FindRepoRoot(), "src", "Services", "HRCore", "HR.Core.Web", "Views", "ExamMarking");
        var banned = new[] { "ApplicantFullName", "NationalId", "RecruitmentApplication", "IntakeChannel", "CompetitionId", "Phone", "Email" };
        foreach (var view in new[] { "Index.cshtml", "Session.cshtml", "Paper.cshtml" })
        {
            var text = System.IO.File.ReadAllText(Path.Combine(root, view));
            Assert.DoesNotContain(banned, b => text.Contains(b, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Marking_responses_carry_exam_numbers_and_never_the_applicant()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, sessions) = await SeedExamReadyAsync(db, 3, 3);
        var seating = CreateSeating(db);
        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        var candidates = await db.ExamCandidates.ToListAsync();
        Assert.True((await seating.SetAttendanceAsync(sessions[0].Id, candidates.ToDictionary(c => c.Id, _ => ExamAttendance.Present), Creator)).Success);
        Assert.True((await seating.MarkHeldAsync(sessions[0].Id, Creator)).Success);
        var employee = await SeedEmployeeAsync(db, "مصحح");
        Assert.True((await seating.AssignMarkerAsync(sessions[0].Id, employee.Id, ExamMarkerRole.First, Creator)).Success);

        var auth = new Mock<IAuthorizationService>();
        auth.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Failed());
        var controller = new ExamMarkingController(new ExamMarkingService(db), auth.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(Guid.NewGuid(), employee.Id, HrManagementRoles.HrExamMarker) } },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(new DefaultHttpContext(), Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
        };

        var index = Assert.IsType<ViewResult>(await controller.Index(default)).Model;
        var session = Assert.IsType<ExamMarkingSessionViewModel>(Assert.IsType<ViewResult>(await controller.Session(sessions[0].Id, default)).Model);
        var paper = Assert.IsType<ViewResult>(await controller.Paper(candidates[0].Id, sessions[0].Id, default)).Model;
        Assert.Equal(3, session.Papers.Count);

        var json = JsonSerializer.Serialize(new object?[] { index, session, paper });
        foreach (var application in await db.RecruitmentApplications.AsNoTracking().ToListAsync())
        {
            Assert.DoesNotContain(application.ApplicantFullName, json, StringComparison.Ordinal);
            Assert.DoesNotContain(application.NationalId!, json, StringComparison.Ordinal);
            Assert.DoesNotContain(application.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(candidates[0].ExamNumber, json, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
