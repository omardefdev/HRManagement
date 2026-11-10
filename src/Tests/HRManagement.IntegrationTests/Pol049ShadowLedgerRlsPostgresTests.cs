using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-49 (P-8, plan section 5): Shadow under forced row security. Production connects HR.Core as a role that neither owns the
/// tables nor bypasses row security, and after this plan three services call the internal endpoint in Shadow on every asset,
/// card and leave action. Each call must leave its ledger row in the caller's tenant, written and read back by that role. Here
/// the endpoint itself (the controller, the orchestrator and the seeded national rules) runs on a migrated PostgreSQL database as
/// such a role: the row carries the tenant of the call, the owner's client and the mode Shadow; another tenant's session does
/// not see it; a body that names another tenant, and a domain out of the engine's scope, write nothing.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol049ShadowLedgerRlsPostgresTests(PostgreSqlFixture fixture)
{
    private static readonly Guid TenantA = Guid.Parse("a0000049-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000049-0000-4000-8000-0000000000b1");

    /// <summary>The production values of HR.Core: the five governed domains in Shadow.</summary>
    private static IConfiguration ShadowConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(PolicyDomainCatalog.Governed.ToDictionary(d => $"PolicyEngine:{d.Domain}", _ => (string?)"Shadow"))
            .Build();

    private static PolicyOrchestrator Orchestrator(HRCoreDbContext db) =>
        new(db, Mock.Of<IWorkflowStarter>(MockBehavior.Strict), new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);

    private static PolicyEnforceInternalController Endpoint(HRCoreDbContext db, IServiceProvider scope, string clientId) =>
        new(Orchestrator(db), new PolicyModeResolver(ShadowConfiguration()), scope.GetRequiredService<ITenantContext>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", clientId)], "Bearer")) }
            }
        };

    /// <summary>What each owner's call site sends, built by the product's own builders.</summary>
    private static readonly (string ClientId, Func<PolicyEnforceRequest> Request)[] OwnerCalls =
    [
        ("svc-admin-logistics", () => new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            SubjectType = "AssetAssignment",
            Action = "Assign",
            EntityId = Guid.NewGuid(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isPrivileged"] = "true", ["value"] = "12000", ["category"] = "أصول ثابتة", ["hasOriginReceipt"] = "true", ["isLegacyEntry"] = "false", ["action"] = "Assign"
            }
        }),
        ("svc-idcard", () => PolicyDomainFactsBuilders.ForIDCardRequest(Guid.NewGuid(), Guid.Empty, "NewEmployee", "Process")),
        ("svc-hr-contracts", () => PolicyDomainFactsBuilders.ForLeaveRequest(Guid.NewGuid(), Guid.Empty, 3, "Annual", "Approve", isSelfApproval: true))
    ];

    private async Task<(string Admin, ServiceProvider Provider)> ArrangeAsync(string name)
    {
        var admin = await Pol047Pg.MigratedAsync(fixture, name);
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        // PLAN-48: an agency's session reads the national policy through the select-only policy the bootstrap applies.
        await Pol047Pg.ApplyNationalSharedReadAsync(admin);
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM pg_tables WHERE tablename = 'PolicyEnforcementLedgers' AND tableowner = current_user"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'PolicyEnforcementLedgers'"""));
        var provider = Pol047Pg.AppProvider(app, ShadowConfiguration());
        // The national seed, as the app role in the national session (what a start-up does).
        await Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            await WorkflowPackSeeder.SeedAsync(db);
            await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
            await PolicyEngineSeeder.SeedAsync(db);
            return true;
        });
        return (admin, provider);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Each_owner_service_leaves_its_Shadow_ledger_row_in_the_tenant_of_its_call_as_a_non_owner_role()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, provider) = await ArrangeAsync("p49shadow");
        await using var _ = provider;

        foreach (var (clientId, request) in OwnerCalls)
        {
            var response = await Pol047Pg.AsTenantAsync(provider, TenantA, async db =>
            {
                await using var scope = provider.CreateAsyncScope();
                var result = await Endpoint(db, scope.ServiceProvider, clientId).Enforce(request(), CancellationToken.None);
                return Assert.IsType<PolicyEnforceResponse>(Assert.IsType<OkObjectResult>(result).Value);
            });

            // Shadow: the caller is allowed whatever the rules say, and the row exists.
            Assert.True(response.Allowed);
            Assert.Equal(PolicyEnforcementMode.Shadow, response.EffectiveMode);
            Assert.NotNull(response.LedgerId);
            Assert.Null(response.WorkflowInstanceId);
            Assert.DoesNotContain(response.Decision.Explanations, e => e.Contains("حقل غير مسموح", StringComparison.Ordinal));
        }

        // As the superuser, past row security: three rows, each in tenant A, in Shadow, with its owner's client.
        var rows = await Pol047Pg.ScalarAsync<string>(admin, $"""
            SELECT string_agg("Domain" || '|' || "CallerClientId" || '|' || "Mode" || '|' || ("TenantId" = '{TenantA}')::text || '|' || "Action", ';' ORDER BY "Domain")
            FROM "PolicyEnforcementLedgers"
            """);
        Assert.Equal(
            "Assets.Custody|svc-admin-logistics|Shadow|true|Assign;Contracts.Leave|svc-hr-contracts|Shadow|true|Approve;IDCard.Lifecycle|svc-idcard|Shadow|true|Process",
            rows);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyEnforcementLedgers" WHERE "TenantId" <> '{TenantA}'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "WorkflowInstances" """));

        // The engine's decision is recorded as it was: the handover's rules matched, the leave rule saw the self-approval.
        var custody = await Pol047Pg.ScalarAsync<string>(admin, """SELECT "DecisionJson" FROM "PolicyEnforcementLedgers" WHERE "Domain" = 'Assets.Custody'""");
        Assert.Contains("R-PRIV-EVIDENCE", custody, StringComparison.Ordinal);
        Assert.Contains("R-PRIV-WF", custody, StringComparison.Ordinal);
        var leave = await Pol047Pg.ScalarAsync<string>(admin, """SELECT "DecisionJson" FROM "PolicyEnforcementLedgers" WHERE "Domain" = 'Contracts.Leave'""");
        Assert.Contains("R-LEAVE-SELF", leave, StringComparison.Ordinal);

        // Row security, as the app role: tenant A reads its three rows, tenant B reads none.
        Assert.Equal(3, await Pol047Pg.AsTenantAsync(provider, TenantA, db => db.PolicyEnforcementLedgers.CountAsync()));
        Assert.Equal(0, await Pol047Pg.AsTenantAsync(provider, TenantB, db => db.PolicyEnforcementLedgers.CountAsync()));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_domains_inside_HR_Core_record_their_Shadow_decision_in_the_session_tenant()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, provider) = await ArrangeAsync("p49core");
        await using var _ = provider;

        await Pol047Pg.AsTenantAsync(provider, TenantB, async db =>
        {
            var orchestrator = Orchestrator(db);
            foreach (var request in new[]
                     {
                         PolicyDomainFactsBuilders.ForEstablishmentChange(Guid.NewGuid(), TenantB, "SubmitForApproval", "CreateJobCatalog", "Draft", actorIsBranchScoped: true),
                         PolicyDomainFactsBuilders.ForOfferLetter(Guid.NewGuid(), TenantB, "Issue", Guid.NewGuid(), isExceptionOffer: true)
                     })
            {
                request.Mode = PolicyEnforcementMode.Shadow;
                var response = await orchestrator.EnforceAsync(request);
                Assert.True(response.Allowed);
                Assert.Null(response.WorkflowInstanceId);
            }

            return true;
        });

        var rows = await Pol047Pg.ScalarAsync<string>(admin, $"""
            SELECT string_agg("Domain" || '|' || coalesce("CallerClientId", '-') || '|' || "Mode" || '|' || ("TenantId" = '{TenantB}')::text, ';' ORDER BY "Domain")
            FROM "PolicyEnforcementLedgers"
            """);
        Assert.Equal("Core.Establishment|-|Shadow|true;Core.Recruitment|-|Shadow|true", rows);
        // The branch-scoped establishment submit is a refusal in the ledger, and an allow for the caller (above).
        var establishment = await Pol047Pg.ScalarAsync<string>(admin, """SELECT "DecisionJson" FROM "PolicyEnforcementLedgers" WHERE "Domain" = 'Core.Establishment'""");
        Assert.Contains("R-EST-DENY-BRANCH", establishment, StringComparison.Ordinal);
        Assert.Equal(0, await Pol047Pg.AsTenantAsync(provider, TenantA, db => db.PolicyEnforcementLedgers.CountAsync()));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_body_that_names_another_tenant_a_foreign_client_and_a_domain_out_of_scope_write_no_row()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, provider) = await ArrangeAsync("p49refuse");
        await using var _ = provider;

        var results = await Pol047Pg.AsTenantAsync(provider, TenantA, async db =>
        {
            await using var scope = provider.CreateAsyncScope();
            var otherTenant = OwnerCalls[0].Request();
            otherTenant.TenantId = TenantB;
            var outOfScope = new PolicyEnforceRequest { Domain = PolicyDomains.ITTickets, SubjectType = "Ticket", Action = "Open" };
            return new[]
            {
                await Endpoint(db, scope.ServiceProvider, "svc-admin-logistics").Enforce(otherTenant, CancellationToken.None),
                await Endpoint(db, scope.ServiceProvider, "svc-idcard").Enforce(OwnerCalls[0].Request(), CancellationToken.None),
                await Endpoint(db, scope.ServiceProvider, "svc-it-infra").Enforce(outOfScope, CancellationToken.None)
            };
        });

        Assert.IsType<BadRequestObjectResult>(results[0]);
        Assert.IsType<ForbidResult>(results[1]);
        Assert.Equal(PolicyDomainCatalog.OutOfScopeMessageAr, Assert.IsType<BadRequestObjectResult>(results[2]).Value);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyEnforcementLedgers" """));
    }
}
