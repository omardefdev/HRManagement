using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-47 §4 and P-6 on PostgreSQL: the start-up seeding (v1, bindings, change sets) as the app role under forced row security in
/// the national session, booted twice: the applier sees the effective versions, so the second boot publishes no second version;
/// and a changed digest of an applied set fails the boot.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol047SeedApplierPostgresTests(PostgreSqlFixture fixture)
{
    private static Task BootAsync(Microsoft.Extensions.DependencyInjection.ServiceProvider provider) =>
        Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db =>
        {
            await WorkflowPackSeeder.SeedAsync(db);
            await PolicyEngineSeeder.SeedAsync(db);
            return true;
        });

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_applier_sees_its_rows_under_forced_row_security_and_a_second_boot_repeats_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "applier");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        await using var provider = Pol047Pg.AppProvider(app);

        await BootAsync(provider);
        var versions = await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """);
        var applications = await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicySeedApplications" """);
        var published = await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicySeedApplications" WHERE "Outcome" = 'Published'""");

        await BootAsync(provider);
        await BootAsync(provider);

        Assert.Equal(versions, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """));
        Assert.Equal(applications, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicySeedApplications" """));
        // PLAN-49: Archive.Retention is out of the engine's scope, so the baseline's operation there publishes nothing.
        Assert.True(published >= 2, $"the baseline publishes v2 of Leave and Recruitment on a new database (got {published})");
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""
            SELECT count(*) FROM "PolicySeedApplications" a JOIN "PolicyTemplates" t ON t."Id" = a."PolicyTemplateId"
            WHERE a."Outcome" = 'Published' AND a."ChangeSetId" = '{PolicySeedChangeSets.BaselineId}' AND t."Domain" = '{PolicyDomains.ArchiveRetention}'
            """));
        // Every row the national session wrote is the national tenant's.
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicySeedApplications" WHERE "TenantId" <> '{Pol047Pg.National}'"""));
        // Core.Recruitment: v2 effective, published as SystemSeed by the baseline.
        Assert.Equal("SystemSeed", await Pol047Pg.ScalarAsync<string>(admin, """
            SELECT p."Kind" FROM "PolicyPublications" p JOIN "PolicyTemplateVersions" v ON v."Id" = p."PolicyTemplateVersionId"
            JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId" WHERE t."Domain" = 'Core.Recruitment' AND v."VersionNumber" = 2
            """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_changed_digest_of_an_applied_set_fails_the_boot()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "tamper");
        await TamperAsync(admin);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_instances_applying_a_new_set_at_once_publish_it_once()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "twoboots");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        await using var provider = Pol047Pg.AppProvider(app);
        await BootAsync(provider);
        var set = new PolicySeedChangeSet("POL-TEST-TWO-INSTANCES",
        [
            // PLAN-49: a governed domain and a rule its catalog accepts (the applier adds nothing to an out-of-scope domain).
            new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, "R-REQ-TWO", "مرتان", 20,
                """{"all":[{"field":"action","operator":"Equals","value":"PublishInternal"}]}""", """[{"effect":"Allow"}]""")
        ]);
        var before = await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """);

        // Two instances, two connections as the app role in the national session, at the same moment (the advisory lock).
        await Task.WhenAll(
            Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db => { await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync([set]); return true; }),
            Pol047Pg.AsTenantAsync(provider, Pol047Pg.National, async db => { await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync([set]); return true; }));

        Assert.Equal(before + 1, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicyTemplateVersions" """));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PolicySeedApplications" WHERE "ChangeSetId" = 'POL-TEST-TWO-INSTANCES'"""));
    }

    private async Task TamperAsync(string admin)
    {
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        await using var provider = Pol047Pg.AppProvider(app);
        await BootAsync(provider);

        // The record of an applied set no longer matches the code (as if the set had been edited after it was applied).
        await Pol047Pg.ExecAsync(admin, $"""UPDATE "PolicySeedApplications" SET "Digest" = repeat('0', 64) WHERE "ChangeSetId" = '{PolicySeedChangeSets.RetireRecruitmentHireId}';""");

        var ex = await Assert.ThrowsAsync<PolicySeedChangeSetException>(() => BootAsync(provider));
        Assert.Contains(PolicySeedChangeSets.RetireRecruitmentHireId, ex.Message);
    }
}
