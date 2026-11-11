using System.Security.Claims;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §2 (G-POL-08): ownership, a spoofed body, the workflow allow-list, subject type, size, tenant.</summary>
public class S2s042PolicyEnforceInternalTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class Harness
    {
        public Mock<IPolicyOrchestrator> Orchestrator { get; } = new();
        public PolicyEnforceRequest? Seen { get; private set; }
        public string? SeenClient { get; private set; }

        public PolicyEnforceInternalController Controller(string clientId, string mode = "Enforce", Guid? tenant = null, string? actingUser = null)
        {
            Orchestrator
                .Setup(o => o.EnforceForServiceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<PolicyEnforceRequest, string, CancellationToken>((r, c, _) => { Seen = r; SeenClient = c; })
                .ReturnsAsync(new PolicyEnforceResponse { Allowed = false, EffectiveMode = PolicyEnforcementMode.Enforce });

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [$"PolicyEngine:{PolicyDomains.AssetsCustody}"] = mode })
                .Build();
            var tenantContext = Mock.Of<ITenantContext>(t => t.TenantId == (tenant ?? Tenant));
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", clientId)], "Bearer"))
            };
            if (actingUser is not null)
                http.Request.Headers[ForwardActingUserHandler.HeaderName] = actingUser;

            return new PolicyEnforceInternalController(Orchestrator.Object, new PolicyModeResolver(config), tenantContext)
            {
                ControllerContext = new ControllerContext { HttpContext = http }
            };
        }
    }

    private static PolicyEnforceRequest Body() => new()
    {
        Domain = PolicyDomains.AssetsCustody,
        SubjectType = "AssetAssignment",
        Action = "Assign",
        EntityId = Guid.NewGuid()
    };

    [Fact]
    public async Task The_server_ignores_mode_actor_and_actor_name_from_the_body()
    {
        var harness = new Harness();
        var body = Body();
        body.Mode = PolicyEnforcementMode.Off;
        body.ActorUserId = Guid.NewGuid();
        body.ActorUserName = "spoofed";

        var result = await harness.Controller("svc-admin-logistics", actingUser: Actor.ToString()).Enforce(body, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(PolicyEnforcementMode.Enforce, harness.Seen!.Mode);
        Assert.Equal(Actor, harness.Seen.ActorUserId);
        Assert.Null(harness.Seen.ActorUserName);
        Assert.Equal(Tenant, harness.Seen.TenantId);
        Assert.Equal("svc-admin-logistics", harness.SeenClient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task The_actor_is_null_without_a_valid_acting_user_header(string? header)
    {
        var harness = new Harness();
        var body = Body();
        body.ActorUserId = Guid.NewGuid();

        await harness.Controller("svc-admin-logistics", actingUser: header).Enforce(body, CancellationToken.None);

        Assert.Null(harness.Seen!.ActorUserId);
    }

    [Fact]
    public async Task A_denial_is_a_200()
    {
        var result = await new Harness().Controller("svc-admin-logistics").Enforce(Body(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False(((PolicyEnforceResponse)ok.Value!).Allowed);
    }

    [Theory]
    [InlineData("svc-idcard")]
    [InlineData("svc-hr-contracts")]
    [InlineData("hr-gateway-client")]
    public async Task A_client_that_does_not_own_the_domain_gets_403(string client)
    {
        var harness = new Harness();

        var result = await harness.Controller(client).Enforce(Body(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(harness.Seen);
    }

    [Theory]
    [InlineData(PolicyDomains.CoreRecruitment)]
    [InlineData(PolicyDomains.CoreEstablishment)]
    [InlineData("Unknown.Domain")]
    public async Task Core_domains_and_unknown_domains_are_never_reached_over_http(string domain)
    {
        var harness = new Harness();
        var body = Body();
        body.Domain = domain;

        Assert.IsType<ForbidResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
        Assert.Null(harness.Seen);
    }

    /// <summary>PLAN-49 (G-POL-02): the catalog answers before the ownership check, whoever asks and whatever the body says.</summary>
    [Theory]
    [InlineData(PolicyDomains.AssetsProcurement, "svc-admin-logistics", "SupplyOrder")]
    [InlineData(PolicyDomains.ContractsAdvance, "svc-hr-contracts", "AdvanceRequest")]
    [InlineData(PolicyDomains.ITTickets, "svc-idcard", "Ticket")]
    [InlineData(PolicyDomains.CoreEmployment, "svc-admin-logistics", "EmploymentRecord")]
    public async Task A_domain_out_of_the_engine_scope_is_400_with_the_catalog_message(string domain, string client, string subject)
    {
        var harness = new Harness();
        var body = Body();
        body.Domain = domain;
        body.SubjectType = subject;
        body.Mode = PolicyEnforcementMode.Enforce;

        var result = Assert.IsType<BadRequestObjectResult>(await harness.Controller(client).Enforce(body, CancellationToken.None));

        Assert.Equal(PolicyDomainCatalog.OutOfScopeMessageAr, result.Value);
        Assert.Null(harness.Seen);
    }

    [Theory]
    [InlineData("OfferLetter")]
    [InlineData("")]
    public async Task A_subject_type_outside_the_domain_list_is_400(string subject)
    {
        var harness = new Harness();
        var body = Body();
        body.SubjectType = subject;

        Assert.IsType<BadRequestObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
        Assert.Null(harness.Seen);
    }

    /// <summary>PLAN-49: the catalog lists the actions of each subject type; any other pair is refused before the engine sees it.</summary>
    [Theory]
    [InlineData("Asset", "Assign")]
    [InlineData("AssetAssignment", "Create")]
    [InlineData("AssetAssignment", "MarkLost")]
    [InlineData("AssetAssignment", "assign")]
    [InlineData("AssetAssignment", "")]
    public async Task An_action_outside_the_catalog_for_the_subject_type_is_400(string subject, string action)
    {
        var harness = new Harness();
        var body = Body();
        body.SubjectType = subject;
        body.Action = action;

        Assert.IsType<BadRequestObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
        Assert.Null(harness.Seen);
    }

    [Theory]
    [InlineData("Asset", "Classify")]
    [InlineData("Asset", "Create")]
    [InlineData("AssetAssignment", "Assign")]
    public async Task Every_pair_the_catalog_lists_for_custody_reaches_the_engine(string subject, string action)
    {
        var harness = new Harness();
        var body = Body();
        body.SubjectType = subject;
        body.Action = action;

        Assert.IsType<OkObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
        Assert.Equal(action, harness.Seen!.Action);
    }

    [Fact]
    public async Task ContextJson_above_8_KB_is_400_and_8_KB_is_accepted()
    {
        var harness = new Harness();
        var body = Body();
        body.ContextJson = new string('x', PolicyEnforceInternalController.MaxContextJsonBytes);
        Assert.IsType<OkObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));

        body = Body();
        body.ContextJson = new string('x', PolicyEnforceInternalController.MaxContextJsonBytes + 1);
        Assert.IsType<BadRequestObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
    }

    [Fact]
    public async Task The_size_counts_bytes_not_characters()
    {
        var body = Body();
        body.ContextJson = new string('ع', (PolicyEnforceInternalController.MaxContextJsonBytes / 2) + 1);

        Assert.IsType<BadRequestObjectResult>(await new Harness().Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
    }

    [Fact]
    public async Task A_different_tenant_in_the_body_is_400_and_the_same_one_is_accepted()
    {
        var harness = new Harness();
        var body = Body();
        body.TenantId = OtherTenant;
        Assert.IsType<BadRequestObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
        Assert.Null(harness.Seen);

        body = Body();
        body.TenantId = Tenant;
        Assert.IsType<OkObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
    }

    [Fact]
    public async Task An_entity_not_saved_yet_sends_an_empty_tenant_and_gets_the_tenant_of_the_request()
    {
        var harness = new Harness();
        var body = Body();
        body.TenantId = Guid.Empty;

        Assert.IsType<OkObjectResult>(await harness.Controller("svc-admin-logistics").Enforce(body, CancellationToken.None));
        Assert.Equal(Tenant, harness.Seen!.TenantId);
    }

    [Theory]
    [InlineData(PolicyDomains.AssetsCustody, "AssetAssignment", "ASSET-ASSIGN-APPROVAL", false)]
    [InlineData(PolicyDomains.AssetsCustody, "Asset", "ASSET-ASSIGN-APPROVAL", false)]
    [InlineData(PolicyDomains.AssetsCustody, "AssetAssignment", "OTHER-WORKFLOW", false)]
    [InlineData(PolicyDomains.IDCardLifecycle, "CardRequest", "ASSET-ASSIGN-APPROVAL", false)]
    [InlineData(PolicyDomains.AssetsCustody, "AssetAssignment", null, false)]
    public void The_allowlist_is_empty_since_PLAN_43(string domain, string subject, string? code, bool expected) =>
        Assert.Equal(expected, PolicyWorkflowTriggerAllowlist.Allows(domain, subject, code));

    /// <summary>
    /// PLAN-49: owners come from the catalog. A governed domain outside HR.Core has one; <c>Core.*</c> has none (never reached
    /// over HTTP) and an out-of-scope domain has none (nobody may ask).
    /// </summary>
    [Fact]
    public void Every_governed_domain_outside_hr_core_is_owned_and_no_other_domain_is()
    {
        foreach (var domain in PolicyDomains.All)
        {
            var expected = PolicyDomainCatalog.IsGoverned(domain) && !domain.StartsWith("Core.", StringComparison.Ordinal);
            Assert.Equal(expected, PolicyDomainOwners.OwnedDomains.Contains(domain));
        }

        Assert.Equal(
            [PolicyDomains.AssetsCustody, PolicyDomains.ContractsLeave, PolicyDomains.IDCardLifecycle],
            PolicyDomainOwners.OwnedDomains.OrderBy(d => d, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_owner_is_a_known_service_client()
    {
        foreach (var domain in PolicyDomainOwners.OwnedDomains)
            Assert.Contains(ServiceClientIds.All, client => PolicyDomainOwners.Owns(client, domain));
    }

    private static async Task<bool> Passes(params Claim[] claims)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddServiceToServiceAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        return (await authorization.AuthorizeAsync(user, resource: null, ServicePolicies.PolicyEnforceS2S)).Succeeded;
    }

    [Fact]
    public async Task The_policy_needs_a_service_account_the_workflow_scope_and_a_domain_owner()
    {
        Assert.True(await Passes(new Claim("azp", "svc-admin-logistics"), new Claim("scope", "hr-core.workflow")));
        Assert.False(await Passes(new Claim("azp", "svc-admin-logistics"), new Claim("scope", "hr-core.lookup")));
        Assert.False(await Passes(new Claim("azp", "svc-hr-core"), new Claim("scope", "hr-core.workflow")));
        Assert.False(await Passes(new Claim("azp", "hr-gateway-client"), new Claim("scope", "hr-core.workflow"), new Claim("sub", Actor.ToString())));
    }

    /// <summary>
    /// PLAN-49: the clients that hold hr-core.workflow for code generation or approvals, and owned a domain the catalog now puts
    /// out of scope, no longer pass the policy (they got a 200 on their own domain before, with nothing behind it).
    /// </summary>
    [Theory]
    [InlineData("svc-records-archive")]
    [InlineData("svc-it-infra")]
    [InlineData("svc-compliance")]
    [InlineData("svc-payroll")]
    public async Task The_owner_of_a_retired_domain_no_longer_passes_the_policy(string client) =>
        Assert.False(await Passes(new Claim("azp", client), new Claim("scope", "hr-core.workflow")));

    [Theory]
    [InlineData("svc-admin-logistics")]
    [InlineData("svc-idcard")]
    [InlineData("svc-hr-contracts")]
    public async Task The_three_owners_of_governed_domains_pass_the_policy(string client) =>
        Assert.True(await Passes(new Claim("azp", client), new Claim("scope", "hr-core.workflow")));

    [Fact]
    public void Enforce_left_the_user_controller()
    {
        var methods = typeof(PoliciesController).GetMethods().Select(m => m.Name);
        Assert.DoesNotContain("Enforce", methods);
        Assert.Single(typeof(PoliciesController).GetCustomAttributes(typeof(RouteAttribute), true));
    }
}
