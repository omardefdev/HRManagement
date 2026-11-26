using System.Reflection;
using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-58 (§4, D-PAY-3, P-8), as <c>GatewayApproverReachTests</c> does for an approver: the gateway lets the independent reviewer
/// (<c>auditor</c>, <c>Payroll.AuditReview</c>) through to the payroll review screens, a run's read, the two review API writes and the
/// module's static files — and to no other payroll path (an auditor posting to <c>PayrollRuns/Approve</c> is refused, 58d). The module's
/// users keep the run's read; the review screens and API are the reviewer's. Every review API template is a real action of Payroll.Web.
/// </summary>
public sealed class GatewayAuditorReachTests
{
    private static readonly string Id = Guid.Parse("11111111-2222-4333-8444-555555555555").ToString("D");

    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")))
            .Append(new Claim("azp", "hr-gateway-client"))
            .Append(new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString())),
        "TestAuth"));

    private static IAuthorizationService Authorization(IDictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services.AddLogging();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>The gateway's decision for a payroll path: its fine-grained policy, else the module's.</summary>
    private static async Task<bool> PassesAsync(IAuthorizationService authorization, ClaimsPrincipal user, string method, string path)
    {
        var policy = GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("payroll", method, new PathString(path), out var fine)
            ? fine
            : HrManagementPolicies.ModulePayroll;
        return (await authorization.AuthorizeAsync(user, resource: null, policy)).Succeeded;
    }

    public static readonly (string Method, string Path)[] ReviewerPaths =
    [
        ("GET", "/ui/payroll/PayrollAuditRecords"),
        ("GET", "/ui/payroll/PayrollAuditRecords/Index"),
        ("GET", "/ui/payroll/PayrollAuditRecords/Create"),
        ("POST", "/ui/payroll/PayrollAuditRecords/Create"),
        ("POST", $"/ui/payroll/PayrollAuditRecords/CloseAudit/{Id}"),
        ("GET", $"/ui/payroll/PayrollAuditRecords/RunSummary/{Id}"),
        ("GET", $"/api/payroll/runs/{Id}"),
        ("POST", $"/api/payroll/runs/{Id}/audit-records"),
        ("POST", $"/api/payroll/audit-records/{Id}/close"),
        ("GET", "/api/payroll/audit-records/antiforgery"),
        ("GET", "/ui/payroll/css/site.css"),
        ("GET", "/ui/payroll/js/site.js"),
    ];

    public static readonly (string Method, string Path)[] NotTheReviewers =
    [
        ("POST", $"/ui/payroll/PayrollRuns/ApproveRun/{Id}"),
        ("POST", $"/api/payroll/runs/{Id}/approve"),
        ("POST", $"/api/payroll/runs/{Id}/post"),
        ("GET", $"/ui/payroll/PayrollRuns/Details/{Id}"),
        ("GET", "/ui/payroll/PayrollRuns"),
        ("GET", $"/api/payroll/runs/{Id}/bank-readiness"),
        ("GET", $"/api/payroll/runs/{Id}/audit-records"),
        ("POST", $"/api/payroll/payment-batches/{Id}/process"),
        ("GET", "/ui/payroll/PaymentBatches"),
        ("GET", "/api/payroll/dual-control"),
        ("POST", $"/api/payroll/dual-control/{Id}/decide"),
        ("GET", "/ui/payroll"),
        ("GET", $"/ui/payroll/PayrollAuditRecords/RunSummary/{Id}/more"),
    ];

    [Fact]
    public async Task The_reviewer_reaches_the_review_paths_and_no_other_payroll_path()
    {
        var authorization = Authorization();
        var auditor = User(HrManagementRoles.Auditor);
        foreach (var (method, path) in ReviewerPaths)
            Assert.True(await PassesAsync(authorization, auditor, method, path), $"the auditor is refused {method} {path}");
        foreach (var (method, path) in NotTheReviewers)
            Assert.False(await PassesAsync(authorization, auditor, method, path), $"the auditor passes {method} {path}");
    }

    [Fact]
    public async Task The_modules_users_keep_a_runs_read_and_the_review_is_not_theirs()
    {
        var authorization = Authorization();
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);
        Assert.True(await PassesAsync(authorization, approver, "GET", $"/api/payroll/runs/{Id}"));
        Assert.True(await PassesAsync(authorization, approver, "POST", $"/api/payroll/runs/{Id}/approve"));
        Assert.True(await PassesAsync(authorization, approver, "GET", "/ui/payroll/css/site.css"));
        // 58d: an approver does not close (nor open) a review record.
        Assert.False(await PassesAsync(authorization, approver, "POST", $"/api/payroll/audit-records/{Id}/close"));
        Assert.False(await PassesAsync(authorization, approver, "POST", $"/ui/payroll/PayrollAuditRecords/CloseAudit/{Id}"));
        Assert.False(await PassesAsync(authorization, User(), "GET", $"/api/payroll/runs/{Id}"));
    }

    [Fact]
    public async Task The_reviewers_roles_come_from_the_agencys_setting()
    {
        var authorization = Authorization(new Dictionary<string, string?> { [PayrollReviewerRequirement.SettingKey] = "compliance, platform-admin" });
        Assert.False(await PassesAsync(authorization, User(HrManagementRoles.Auditor), "POST", $"/api/payroll/runs/{Id}/audit-records"));
        Assert.True(await PassesAsync(authorization, User(HrManagementRoles.Compliance), "POST", $"/api/payroll/runs/{Id}/audit-records"));
    }

    [Fact]
    public void Every_review_api_path_is_an_action_of_payroll_that_names_the_review_policy()
    {
        var api = typeof(Payroll.Web.Controllers.PayrollAuditRecordsApiController);
        Assert.Equal(HrManagementPolicies.PayrollAuditReview, api.GetCustomAttribute<ModulePolicyOverrideAttribute>()?.Policy);
        var actions = api.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(m => (Verb: m.GetCustomAttribute<HttpMethodAttribute>(), Method: m))
            .Where(a => a.Verb is not null)
            .Select(a => (Method: a.Verb!.HttpMethods.First(), Template: System.Text.RegularExpressions.Regex.Replace(a.Verb!.Template ?? "", @"\{[^}]+\}", "{guid}")))
            .ToList();
        foreach (var (template, method) in GatewayPayrollReviewPaths.ReviewApi)
            Assert.Contains(actions, a => a.Method == method && a.Template == template);

        var read = typeof(Payroll.Web.Controllers.PayrollRunReadApiController);
        Assert.Equal(HrManagementPolicies.PayrollRunRead, read.GetCustomAttribute<ModulePolicyOverrideAttribute>()?.Policy);
        Assert.Equal(HrManagementPolicies.PayrollAuditReview,
            typeof(Payroll.Web.Controllers.PayrollAuditRecordsController).GetCustomAttribute<ModulePolicyOverrideAttribute>()?.Policy);
    }
}
