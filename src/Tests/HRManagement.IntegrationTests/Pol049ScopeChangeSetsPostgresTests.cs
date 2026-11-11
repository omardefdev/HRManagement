using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using HRManagement.UnitTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-49 (plan section 2, P-4) on PostgreSQL: a fully migrated database that holds the policy data of before the plan, booted
/// as the app role under forced row security in the national session. The four change sets produce new published versions and
/// never edit one in place (the immutability triggers of PLAN-47 are in force and nothing trips them); the fifteen out-of-scope
/// templates carry RetiredAt; a second and a third start change nothing; two instances starting together apply each set once.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol049ScopeChangeSetsPostgresTests(PostgreSqlFixture fixture)
{
    private static Task LegacyAsync(ServiceProvider provider) =>
        Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            await WorkflowPackSeeder.SeedAsync(db);
            await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
            await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);
            await Pol049LegacyPolicyData.SeedAsync(db);
            return true;
        });

    private static Task BootAsync(ServiceProvider provider) =>
        Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            await WorkflowPackSeeder.SeedAsync(db);
            await PolicyEngineSeeder.SeedAsync(db);
            return true;
        });

    /// <summary>Every rule of every version, hashed: what an edit in place would change.</summary>
    private const string RuleFingerprintSql = """
        SELECT coalesce(md5(string_agg(r."Id"::text || '|' || r."PolicyTemplateVersionId"::text || '|' || coalesce(r."RuleKey", '') || '|' || r."Priority"::text || '|' || r."WhenJson" || '|' || r."ThenJson", ';' ORDER BY r."Id")), '')
        FROM "PolicyRules" r WHERE r."PolicyTemplateVersionId" = ANY(@ids)
        """;

    private static async Task<string> OldRulesFingerprintAsync(string admin, Guid[] versionIds)
    {
        await using var conn = new Npgsql.NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(RuleFingerprintSql, conn);
        cmd.Parameters.AddWithValue("ids", versionIds);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Guid[]> VersionIdsAsync(string admin)
    {
        await using var conn = new Npgsql.NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("""SELECT "Id" FROM "PolicyTemplateVersions" ORDER BY "Id" """, conn);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return [.. ids];
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_sets_publish_new_versions_never_edit_one_in_place_and_mark_the_fifteen_templates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "p49sets");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        await using var provider = Pol047Pg.AppProvider(app);

        // The migration of this plan: two nullable columns, no data.
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%_POL049_PolicyDomainRetirement'"""));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_name = 'PolicyTemplates' AND column_name IN ('RetiredAt', 'RetiredReason') AND is_nullable = 'YES'
            """));

        await LegacyAsync(provider);
        Assert.Equal(20L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplates" """));
        var oldVersions = await VersionIdsAsync(admin);
        Assert.Equal(20, oldVersions.Length);
        var rulesBefore = await OldRulesFingerprintAsync(admin, oldVersions);
        Assert.NotEqual(string.Empty, rulesBefore);

        await BootAsync(provider);

        // Fifteen retired templates, each with one final published version with no rules and the set's reason.
        Assert.Equal(15L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplates" WHERE "RetiredAt" IS NOT NULL AND "RetiredReason" IS NOT NULL"""));
        Assert.Equal(15L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM "PolicyTemplates" t JOIN "PolicyTemplateVersions" v ON v."PolicyTemplateId" = t."Id"
            WHERE t."RetiredAt" IS NOT NULL AND v."VersionNumber" = 2 AND v."Status" = 'Published' AND v."WithdrawsAllRules"
              AND v."EffectiveTo" IS NULL AND v."ChangeReason" = t."RetiredReason"
              AND NOT EXISTS (SELECT 1 FROM "PolicyRules" r WHERE r."PolicyTemplateVersionId" = v."Id")
            """));
        var retired = await Pol047Pg.ScalarAsync<string>(admin, """SELECT string_agg("Domain", ',' ORDER BY "Domain") FROM "PolicyTemplates" WHERE "RetiredAt" IS NOT NULL""");
        Assert.Equal(string.Join(",", PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal)), retired);
        var governed = await Pol047Pg.ScalarAsync<string>(admin, """SELECT string_agg("Domain", ',' ORDER BY "Domain") FROM "PolicyTemplates" WHERE "RetiredAt" IS NULL""");
        Assert.Equal(string.Join(",", PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal)), governed);

        // The publications are the sets', as SystemSeed.
        Assert.Equal(15L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyPublications" WHERE "Kind" = 'SystemSeed' AND "EvidenceRef" = '{PolicySeedChangeSets.ScopeRetireId}'"""));
        foreach (var set in new[] { PolicySeedChangeSets.AssetsActionId, PolicySeedChangeSets.IdCardOrphanId, PolicySeedChangeSets.LeaveId })
            Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyPublications" WHERE "Kind" = 'SystemSeed' AND "EvidenceRef" = '{set}'"""));

        // Never an edit in place: the twenty old versions keep every rule exactly, and are closed, not changed.
        Assert.Equal(rulesBefore, await OldRulesFingerprintAsync(admin, oldVersions));
        Assert.Equal(18L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" WHERE "VersionNumber" = 1 AND "Status" = 'Published' AND "EffectiveTo" IS NOT NULL"""));
        Assert.Equal(38L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """));

        // The effective custody rules carry their action; the card rule and the placeholder leave rules are in no effective version.
        Assert.Equal(3L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM "PolicyRules" r JOIN "PolicyTemplateVersions" v ON v."Id" = r."PolicyTemplateVersionId"
            JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
            WHERE t."Domain" = 'Assets.Custody' AND v."EffectiveTo" IS NULL AND r."RuleKey" IN ('R-PRIV-EVIDENCE', 'R-PRIV-WF', 'R-HIGH-APPROVAL')
              AND r."WhenJson" LIKE '%"field":"action","operator":"Equals","value":"Assign"%'
            """));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM "PolicyRules" r JOIN "PolicyTemplateVersions" v ON v."Id" = r."PolicyTemplateVersionId"
            WHERE v."EffectiveTo" IS NULL AND r."RuleKey" IN ('R-ID-PROCESS', 'R-LEAVE-MAX', 'R-LEAVE-LONG')
            """));

        // Every row the national session wrote is the national tenant's (the applications, the versions, the publications).
        foreach (var table in new[] { "PolicySeedApplications", "PolicyTemplateVersions", "PolicyPublications", "PolicyWorkflowBindings" })
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "{table}" WHERE "TenantId" <> '{Pol047Pg.National}'"""));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyWorkflowBindings" WHERE "Domain" = 'Core.Establishment' AND "WorkflowCode" = 'ESTABLISHMENT-CHANGE-APPROVAL' AND "WorkflowDefinitionId" IS NULL"""));

        // A second and a third start: nothing new.
        var versions = await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """);
        var applications = await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicySeedApplications" """);
        var retiredAt = await Pol047Pg.ScalarAsync<string>(admin, """SELECT md5(string_agg("Id"::text || "RetiredAt"::text, ';' ORDER BY "Id")) FROM "PolicyTemplates" WHERE "RetiredAt" IS NOT NULL""");
        await BootAsync(provider);
        await BootAsync(provider);
        Assert.Equal(versions, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """));
        Assert.Equal(applications, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicySeedApplications" """));
        Assert.Equal(retiredAt, await Pol047Pg.ScalarAsync<string>(admin, """SELECT md5(string_agg("Id"::text || "RetiredAt"::text, ';' ORDER BY "Id")) FROM "PolicyTemplates" WHERE "RetiredAt" IS NOT NULL"""));
        Assert.Equal(rulesBefore, await OldRulesFingerprintAsync(admin, oldVersions));
        Assert.Equal(23L, applications);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_instances_starting_together_retire_each_template_once()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "p49two");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        await using var provider = Pol047Pg.AppProvider(app);
        await LegacyAsync(provider);

        // Two connections as the app role in the national session, at the same moment (the advisory lock of the applier).
        await Task.WhenAll(
            Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db => { await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync(PolicySeedChangeSets.All); return true; }),
            Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db => { await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync(PolicySeedChangeSets.All); return true; }));

        Assert.Equal(15L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicySeedApplications" WHERE "ChangeSetId" = '{PolicySeedChangeSets.ScopeRetireId}' AND "Outcome" = 'Published'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM (SELECT "PolicyTemplateId" FROM "PolicyTemplateVersions" WHERE "Status" = 'Published' AND "WithdrawsAllRules"
                                  GROUP BY "PolicyTemplateId" HAVING count(*) > 1) twice
            """));
        Assert.Equal(15L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplates" WHERE "RetiredAt" IS NOT NULL"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" WHERE "Status" = 'Draft'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_new_database_under_forced_row_security_seeds_the_five_governed_domains_and_retires_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "p49new");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        await using var provider = Pol047Pg.AppProvider(app);

        await BootAsync(provider);
        await BootAsync(provider);

        var domains = await Pol047Pg.ScalarAsync<string>(admin, """SELECT string_agg("Domain", ',' ORDER BY "Domain") FROM "PolicyTemplates" """);
        Assert.Equal(string.Join(",", PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal)), domains);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplates" WHERE "RetiredAt" IS NOT NULL"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicySeedApplications" WHERE "ChangeSetId" = '{PolicySeedChangeSets.ScopeRetireId}'"""));
        Assert.Equal(7L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyWorkflowBindings" """));
        // Leave: the seed, the baseline, then POL-049-LEAVE; its effective version holds the self-approval rule alone.
        Assert.Equal("R-LEAVE-SELF", await Pol047Pg.ScalarAsync<string>(admin, """
            SELECT string_agg(r."RuleKey", ',' ORDER BY r."RuleKey") FROM "PolicyRules" r JOIN "PolicyTemplateVersions" v ON v."Id" = r."PolicyTemplateVersionId"
            JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId" WHERE t."Domain" = 'Contracts.Leave' AND v."EffectiveTo" IS NULL
            """));
    }
}
