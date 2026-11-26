using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Options;
using Payroll.Web.Authorization;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (§5, D-PAY-20, P-9): a token that carries two conflicting payroll roles passes none of Payroll's five financial policies. Each
/// pair of the matrix is refused with a message that names it, and the act's own authorization writes a <c>RoleConflict</c> line (no token,
/// no secret). hr-admin counts as processor and approver everywhere, and as poster where the posting's policy keeps him. With the setting
/// off a conflicting holder passes, and every pass is still audited. The realm import refuses a conflicting persona but the marked fixture,
/// and its pairs are the matrix's.
/// </summary>
public sealed class Pay058RoleConflictTests
{
    private sealed class RecordingAudit : ISecurityAuditSink
    {
        public ConcurrentQueue<SecurityAuditPayload> Events { get; } = new();

        public bool TryPublish(SecurityAuditPayload payload)
        {
            Events.Enqueue(payload);
            return true;
        }
    }

    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim("sub", Guid.NewGuid().ToString("D"))), "TestAuth"));

    private static (IAuthorizationService Authorization, IServiceProvider Services) Build(bool reject = true, IDictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services.AddLogging();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        services.AddSingleton(new SeparationOfDutiesService());
        services.AddOptions<PayrollSodOptions>().Configure(o => o.RejectConflictingRoleHolders = reject);
        services.AddPayrollRoleConflictCheck();
        services.AddSingleton<RecordingAudit>();
        services.AddSingleton<ISecurityAuditSink>(sp => sp.GetRequiredService<RecordingAudit>());
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IAuthorizationService>(), provider);
    }

    /// <summary>The act's own authorization: the request is the resource, as for an endpoint's [Authorize].</summary>
    private static async Task<AuthorizationResult> ActAsync(IAuthorizationService authorization, IServiceProvider services, ClaimsPrincipal user, string policy) =>
        await authorization.AuthorizeAsync(user, new DefaultHttpContext { User = user, RequestServices = services }, policy);

    public static TheoryData<string, string, string> Pairs() => new()
    {
        { HrManagementRoles.PayrollProcessor, HrManagementRoles.PayrollApprover, HrManagementPolicies.PayrollApprove },
        { HrManagementRoles.PayrollApprover, HrManagementRoles.PayrollPoster, HrManagementPolicies.PayrollPost },
        { HrManagementRoles.PayrollProcessor, HrManagementRoles.PayrollPoster, HrManagementPolicies.PayrollProcess },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Each_conflicting_pair_is_refused_with_its_names_and_audited(string roleA, string roleB, string policy)
    {
        var (authorization, services) = Build();
        var user = User(HrManagementRoles.Payroll, roleA, roleB);

        var result = await ActAsync(authorization, services, user, policy);

        Assert.False(result.Succeeded);
        var failure = Assert.Single(result.Failure!.FailureReasons.OfType<PayrollRoleConflictFailure>());
        Assert.Contains(roleA, failure.Message, StringComparison.Ordinal);
        Assert.Contains(roleB, failure.Message, StringComparison.Ordinal);
        var audit = Assert.Single(services.GetRequiredService<RecordingAudit>().Events);
        Assert.Equal(SecurityAudit.EventTypes.RoleConflict, audit.EventType);
        Assert.Equal($"policy:{policy}", audit.Resource);
        using var detail = JsonDocument.Parse(audit.DetailJson!);
        Assert.True(detail.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Contains(roleA, detail.RootElement.GetProperty("pair").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", audit.DetailJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_multirole_holder_passes_no_financial_policy_and_a_single_role_holder_passes_his_own()
    {
        var (authorization, services) = Build();
        var multirole = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor, HrManagementRoles.PayrollApprover, HrManagementRoles.PayrollPoster);
        foreach (var policy in PayrollRoleConflictRequirement.FinancialPolicies)
            Assert.False((await ActAsync(authorization, services, multirole, policy)).Succeeded, policy);

        Assert.True((await ActAsync(authorization, services, User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor), HrManagementPolicies.PayrollProcess)).Succeeded);
        Assert.True((await ActAsync(authorization, services, User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover), HrManagementPolicies.PayrollApprove)).Succeeded);
        Assert.True((await ActAsync(authorization, services, User(HrManagementRoles.Payroll, HrManagementRoles.PayrollPoster), HrManagementPolicies.PayrollPost)).Succeeded);
        Assert.True((await ActAsync(authorization, services, User(HrManagementRoles.Auditor), HrManagementPolicies.PayrollAuditReview)).Succeeded);
        // A page that only asks whether to show a button (no request as the resource) writes no audit line.
        Assert.False((await authorization.AuthorizeAsync(multirole, HrManagementPolicies.PayrollApprove)).Succeeded);
        Assert.Equal(PayrollRoleConflictRequirement.FinancialPolicies.Count, services.GetRequiredService<RecordingAudit>().Events.Count(e => e.EventType == SecurityAudit.EventTypes.RoleConflict));
    }

    [Fact]
    public async Task Hr_admin_counts_as_processor_and_approver_and_as_poster_only_where_the_posting_keeps_him()
    {
        var admin = User(HrManagementRoles.Admin);

        // Outside demo mode Payroll.Post excludes hr-admin (PLAN-51): he is refused processing and approving by the conflict.
        var production = new Dictionary<string, string?> { [$"{ConfiguredRolesHandler.AdminExcludedPoliciesKey}:0"] = HrManagementPolicies.PayrollPost };
        var (authorization, services) = Build(settings: production);
        Assert.False((await ActAsync(authorization, services, admin, HrManagementPolicies.PayrollProcess)).Succeeded);
        var approve = await ActAsync(authorization, services, admin, HrManagementPolicies.PayrollApprove);
        Assert.False(approve.Succeeded);
        Assert.Contains(HrManagementRoles.PayrollApprover, Assert.Single(approve.Failure!.FailureReasons.OfType<PayrollRoleConflictFailure>()).Message, StringComparison.Ordinal);

        // On the demo (and a lab) hr-admin keeps every payroll policy, so he is refused in every financial act, the posting included.
        var (demo, demoServices) = Build(settings: new Dictionary<string, string?> { ["Deployment:Mode"] = "demo" });
        foreach (var policy in new[] { HrManagementPolicies.PayrollProcess, HrManagementPolicies.PayrollApprove, HrManagementPolicies.PayrollPost, HrManagementPolicies.PayrollReverse })
            Assert.False((await ActAsync(demo, demoServices, admin, policy)).Succeeded, policy);
    }

    [Fact]
    public async Task With_the_setting_off_a_conflicting_holder_passes_and_every_pass_is_still_audited()
    {
        var (authorization, services) = Build(reject: false);
        var user = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor, HrManagementRoles.PayrollApprover);

        Assert.True((await ActAsync(authorization, services, user, HrManagementPolicies.PayrollApprove)).Succeeded);
        Assert.True((await ActAsync(authorization, services, user, HrManagementPolicies.PayrollProcess)).Succeeded);

        var events = services.GetRequiredService<RecordingAudit>().Events.ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(SecurityAudit.EventTypes.RoleConflict, e.EventType);
            Assert.False(JsonDocument.Parse(e.DetailJson!).RootElement.GetProperty("rejected").GetBoolean());
        });
    }

    [Fact]
    public void The_import_guard_refuses_a_conflicting_persona_but_the_marked_fixture()
    {
        var (exitCode, stdout, stderr) = RepoScriptRunner.RunScript(
            RepoPaths.Resolve("scripts", "keycloak", "persona-role-conflicts.ps1"), "-SelfTest", timeoutMs: 120_000);
        Assert.True(exitCode == 0, stdout + stderr);
        Assert.Contains("PASS", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_import_guards_pairs_are_the_matrixs_and_the_personas_hold_one_marked_fixture_only()
    {
        var script = System.IO.File.ReadAllText(RepoPaths.Resolve("scripts", "keycloak", "persona-role-conflicts.ps1"));
        foreach (var pair in SodMatrixDefaults.CreateDefaultConflicts())
            Assert.Contains($"@(\"{pair.RoleA}\", \"{pair.RoleB}\")", script, StringComparison.Ordinal);

        using var personas = JsonDocument.Parse(System.IO.File.ReadAllText(RepoPaths.Resolve("testdata", "acceptance", "personas.json")));
        var fixtures = personas.RootElement.GetProperty("personas").EnumerateArray()
            .Where(p => p.TryGetProperty("sodConflictFixture", out var f) && f.GetBoolean())
            .Select(p => p.GetProperty("username").GetString())
            .ToList();
        Assert.Equal(["acc-a-payroll-multirole"], fixtures);
    }
}
