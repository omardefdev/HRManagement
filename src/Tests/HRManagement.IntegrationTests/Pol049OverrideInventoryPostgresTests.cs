using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-49 (the review, finding 1) on PostgreSQL, as the app role under forced row security: the start-up inventory runs in the
/// national tenant's session, and that session cannot see an agency's override (the EF filter, and row security). So "no rule
/// of an effective version" said nothing about overrides. The inventory now reads each tenant of the Tenants table in its own
/// session and reports what the validator refuses there, apart from the national part.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol049OverrideInventoryPostgresTests(PostgreSqlFixture fixture)
{
    private static readonly Guid AgencyA = Guid.Parse("a0000049-0000-4000-8000-0000000000d1");
    private static readonly Guid AgencyB = Guid.Parse("a0000049-0000-4000-8000-0000000000d2");
    private const string Privileged = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""";

    /// <summary>An override in force, written as a seeder writes one (no validator on the way): one rule, or two.</summary>
    private static async Task<bool> SeedOverrideAsync(HRCoreDbContext db, Guid tenant, bool withRefusedRule)
    {
        var template = new PolicyTemplate
        {
            TenantId = tenant,
            Domain = PolicyDomains.AssetsCustody,
            Name = "override",
            SubjectType = "Asset",
            Scope = PolicyTemplateScope.TenantOverride
        };
        db.PolicyTemplates.Add(template);
        var rules = new List<PolicyRule>
        {
            new() { TenantId = tenant, RuleKey = "P49-R-EVIDENCE", Name = "evidence", Priority = 10, WhenJson = Privileged, ThenJson = """[{"effect":"RequireEvidence"}]""" }
        };
        if (withRefusedRule)
        {
            // A handover approval fired on every action of the domain: not bound to Assign (and, for an agency, under the ceiling).
            rules.Add(new PolicyRule
            {
                TenantId = tenant,
                RuleKey = "P49-R-OLD-WF",
                Name = "old layer",
                Priority = 20,
                WhenJson = Privileged,
                ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""
            });
        }

        await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = tenant,
            VersionNumber = 1,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1)
        }, rules, PolicyVersionStatus.Published, new PolicyPublication { TenantId = tenant, EvidenceRef = "pol049-override-inventory" });
        return true;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_national_session_cannot_see_an_agencys_override_and_the_inventory_reads_each_tenant_in_its_own_session()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "p49inv");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        await Pol047Pg.ApplyNationalSharedReadAsync(admin);
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "Tenants" ("Id","Code","NameAr","IsActive","CreatedAt") VALUES
              ('{Pol047Pg.National}','PLATFORM','national',true,now()),
              ('{AgencyA}','P49-A','agency a',true,now()),
              ('{AgencyB}','P49-B','agency b',true,now())
            ON CONFLICT DO NOTHING
            """);
        await using var provider = Pol047Pg.AppProvider(app);
        await Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            // As the national seeding does it: the workflow pack, the policies, then the code policies a SelectCodePolicy rule names.
            await WorkflowPackSeeder.SeedAsync(db);
            await PolicyEngineSeeder.SeedAsync(db);
            await CodeGenerationSeeder.SeedAsync(db);
            return true;
        });
        // Agency A holds a rule the validator refuses; agency B's override passes whole.
        await Pol047Pg.AsTenantAsync(provider, AgencyA, db => SeedOverrideAsync(db, AgencyA, withRefusedRule: true));
        await Pol047Pg.AsTenantAsync(provider, AgencyB, db => SeedOverrideAsync(db, AgencyB, withRefusedRule: false));

        var nationalTemplates = 0;
        await Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            // The start-up session by itself: the national templates, and nothing of an agency.
            Assert.Equal(0, await db.PolicyTemplates.CountAsync(t => t.Scope == PolicyTemplateScope.TenantOverride));
            nationalTemplates = await db.PolicyTemplates.CountAsync();
            Assert.True(nationalTemplates > 0, "the national seed left no template");
            var national = await PolicyPublishedRuleInventory.FindAsync(db, PolicyRuleValidationService.ForCatalog(db));
            Assert.DoesNotContain(national, r => r.InForce);
            Assert.DoesNotContain(national, r => r.TenantId == AgencyA || r.TenantId == AgencyB);

            // Each tenant in its own session.
            var walked = await PolicyPublishedRuleInventory.FindAgencyOverridesAsync(db, provider);
            Assert.NotNull(walked);
            Assert.Equal(2, walked.Value.TenantsRead);
            var found = Assert.Single(walked.Value.Found);
            Assert.Equal(AgencyA, found.TenantId);
            Assert.Equal(PolicyDomains.AssetsCustody, found.Domain);
            Assert.Equal("P49-R-OLD-WF", found.RuleKey);
            Assert.True(found.InForce);
            Assert.Contains("THEN_WORKFLOW_NOT_ALLOWED", found.Codes, StringComparison.Ordinal);
            // The walk leaves the caller's tenant as it found it.
            Assert.Equal(Pol047Pg.National, TenantIdFlow.Get());
            return true;
        });

        // The start-up step itself, called by someone who holds no tenant (at HR.Core's start the flow is the national tenant
        // already; this is any other caller). The first part must still read the national templates: its summary says how
        // many, and a session that is nobody's would read none and print the same "0 rule(s)". The second reads the two agencies.
        var lines = new ListLoggerProvider();
        provider.GetRequiredService<ILoggerFactory>().AddProvider(lines);
        var before = TenantIdFlow.Get();
        TenantIdFlow.Set(null);
        try
        {
            await using var scope = provider.CreateAsyncScope();
            await PolicyPublishedRuleInventory.LogAsync(scope.ServiceProvider.GetRequiredService<HRCoreDbContext>(), scope.ServiceProvider);
        }
        finally
        {
            TenantIdFlow.Set(before);
        }

        var nationalSummary = Assert.Single(lines.Messages, m => m.StartsWith($"{PolicyPublishedRuleInventory.LogPrefix}: 0 rule(s) of effective versions the validator refuses; ", StringComparison.Ordinal));
        Assert.EndsWith($"; {nationalTemplates} national template(s) read", nationalSummary, StringComparison.Ordinal);
        Assert.Contains(lines.Messages, m => m.StartsWith(
            $"{PolicyPublishedRuleInventory.OverrideLogPrefix}: 1 rule(s) of overrides in force the validator refuses, in 1 of 2 agency tenant(s) read", StringComparison.Ordinal));
        Assert.Contains(lines.Messages, m => m.StartsWith($"{PolicyPublishedRuleInventory.OverrideLogPrefix}: {PolicyDomains.AssetsCustody} tenant {AgencyA}", StringComparison.Ordinal)
                                             && m.Contains("rule P49-R-OLD-WF: ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines.Messages, m => m.Contains("could not be taken", StringComparison.Ordinal));

        // Inside a request the tenant of every session is the caller's (the token's tenant comes before the flow): the walk
        // would read agency A's rows once for each agency and call them all read. It takes nothing instead.
        var http = provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        http.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(TenantContext.TenantClaimType, AgencyA.ToString())], "test"))
        };
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var asCaller = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            Assert.Equal(AgencyA, asCaller.CurrentTenantId);
            Assert.Null(await PolicyPublishedRuleInventory.FindAgencyOverridesAsync(asCaller, scope.ServiceProvider));
        }
        finally
        {
            http.HttpContext = null;
        }

        // With no service scope to open a session per tenant, one session cannot stand for the others: the part is not taken.
        await using var alone = Pol047Pg.AdminContext(admin);
        Assert.Null(await PolicyPublishedRuleInventory.FindAgencyOverridesAsync(alone, null));
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                    return [.. _messages];
            }
        }

        public ILogger CreateLogger(string categoryName) => new ListLogger(this);

        public void Dispose()
        {
        }

        private sealed class ListLogger(ListLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._messages)
                    owner._messages.Add(formatter(state, exception));
            }
        }
    }
}
