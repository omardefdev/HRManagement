using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-47 §3 and P-3 on PostgreSQL: the immutability trigger, proved as a NOSUPERUSER NOBYPASSRLS role under forced row security,
/// for a national template (session tenant PLATFORM, as the start-up seeder) and an agency override (session tenant A). A draft
/// accepts inserts, updates and deletes; a published version refuses them; EffectiveTo, UpdatedAt and UpdatedBy stay writable;
/// raw SQL and ExecuteUpdate meet the trigger, not only the interceptor.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol047PolicyImmutabilityPostgresTests(PostgreSqlFixture fixture)
{
    private const string EmptyJson = "{}";

    private static async Task AssertRefusedAsync(string app, Guid tenant, string sql)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(app, sql, tenant));
        Assert.Equal("P0047", ex.SqlState);
        Assert.Contains("PLAN-47 policy immutability", ex.MessageText);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData("national")]
    [InlineData("override")]
    public async Task Drafts_change_and_published_versions_refuse_as_the_app_role_under_forced_row_security(string kind)
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "imm" + kind[..3]);
        var national = kind == "national";
        var tenant = national ? Pol047Pg.National : Pol047Pg.TenantA;
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) seeded;
        await using (var db = Pol047Pg.AdminContext(admin))
            seeded = await Pol047Pg.SeedTemplateAsync(db, tenant, national ? PolicyTemplateScope.National : PolicyTemplateScope.TenantOverride, PolicyDomains.AssetsCustody);
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);

        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'PolicyRules'"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'PolicyTemplateVersions'"""));

        // A draft: insert, update and delete pass the trigger.
        var newRule = Guid.NewGuid();
        await Pol047Pg.ExecAsync(app, $"""
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{newRule}','{seeded.Draft}','R-ADD','add',5,'{Pol047Pg.ValidWhen}','{Pol047Pg.ValidThen}','{tenant}',now(),now());
            """, tenant);
        await Pol047Pg.ExecAsync(app, $"""UPDATE "PolicyRules" SET "Priority" = 6 WHERE "Id" = '{seeded.DraftRule}';""", tenant);
        await Pol047Pg.ExecAsync(app, $"""DELETE FROM "PolicyRules" WHERE "Id" = '{newRule}';""", tenant);
        await Pol047Pg.ExecAsync(app, $"""UPDATE "PolicyTemplateVersions" SET "ChangeReason" = 'draft edit' WHERE "Id" = '{seeded.Draft}';""", tenant);
        Assert.Equal(6L, await Pol047Pg.ScalarAsync<long>(app, $"""SELECT "Priority" FROM "PolicyRules" WHERE "Id" = '{seeded.DraftRule}'""", tenant));

        // A published version: its rules never change.
        await AssertRefusedAsync(app, tenant, $"""
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{seeded.Published}','R-SNEAK','x',5,'{EmptyJson}','[]','{tenant}',now(),now());
            """);
        await AssertRefusedAsync(app, tenant, $"""UPDATE "PolicyRules" SET "WhenJson" = '{EmptyJson}' WHERE "Id" = '{seeded.PublishedRule}';""");
        await AssertRefusedAsync(app, tenant, $"""DELETE FROM "PolicyRules" WHERE "Id" = '{seeded.PublishedRule}';""");
        await AssertRefusedAsync(app, tenant, $"""UPDATE "PolicyRules" SET "PolicyTemplateVersionId" = '{seeded.Published}' WHERE "Id" = '{seeded.DraftRule}';""");

        // ... and the version itself changes only in EffectiveTo, UpdatedAt and UpdatedBy.
        await Pol047Pg.ExecAsync(app, $"""UPDATE "PolicyTemplateVersions" SET "EffectiveTo" = now() + interval '1 day', "UpdatedAt" = now(), "UpdatedBy" = gen_random_uuid() WHERE "Id" = '{seeded.Published}';""", tenant);
        await AssertRefusedAsync(app, tenant, $"""UPDATE "PolicyTemplateVersions" SET "EffectiveFrom" = now() - interval '9 years' WHERE "Id" = '{seeded.Published}';""");
        await AssertRefusedAsync(app, tenant, $"""UPDATE "PolicyTemplateVersions" SET "Status" = 'Draft' WHERE "Id" = '{seeded.Published}';""");
        await AssertRefusedAsync(app, tenant, $"""DELETE FROM "PolicyTemplateVersions" WHERE "Id" = '{seeded.Published}';""");

        // ExecuteUpdate bypasses the interceptor, not the trigger.
        await using (var provider = Pol047Pg.AppProvider(app))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => Pol047Pg.AsTenantAsync(provider, tenant, db =>
                db.PolicyRules.Where(r => r.Id == seeded.PublishedRule).ExecuteUpdateAsync(s => s.SetProperty(r => r.ThenJson, "[]"))));
            Assert.Equal("P0047", (ex as PostgresException ?? ex.InnerException as PostgresException)!.SqlState);
        }

        Assert.Equal(Pol047Pg.ValidThen, await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "ThenJson" FROM "PolicyRules" WHERE "Id" = '{seeded.PublishedRule}'"""));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{seeded.Published}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_version_the_session_cannot_see_is_refused_not_let_through()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "immhidden");
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) national;
        await using (var db = Pol047Pg.AdminContext(admin))
            national = await Pol047Pg.SeedTemplateAsync(db, Pol047Pg.National, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);

        // Agency A writes a rule of its own tenant that names the national draft: row security hides the version from the trigger's
        // lookup, so the write is refused (fail closed) instead of slipping past.
        await AssertRefusedAsync(app, Pol047Pg.TenantA, $"""
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{national.Draft}','R-CROSS','x',5,'{Pol047Pg.ValidWhen}','{Pol047Pg.ValidThen}','{Pol047Pg.TenantA}',now(),now());
            """);
        // A session with no tenant sees nothing and changes nothing.
        await AssertRefusedAsync(app, Guid.Empty, $"""
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{national.Draft}','R-NOTENANT','x',5,'{Pol047Pg.ValidWhen}','{Pol047Pg.ValidThen}','{Guid.Empty}',now(),now());
            """);
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{national.Draft}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_trigger_binds_the_superuser_too()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "immsuper");
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) seeded;
        await using (var db = Pol047Pg.AdminContext(admin))
            seeded = await Pol047Pg.SeedTemplateAsync(db, Pol047Pg.National, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(admin, $"""UPDATE "PolicyRules" SET "WhenJson" = '{EmptyJson}' WHERE "Id" = '{seeded.PublishedRule}';"""));
        Assert.Equal("P0047", ex.SqlState);
    }
}
