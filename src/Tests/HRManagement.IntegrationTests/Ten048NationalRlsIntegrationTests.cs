using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-48 (§1, P-3) on PostgreSQL, as a NOSUPERUSER NOBYPASSRLS role under forced row security with TEN-002C and TEN-048 applied
/// by the table owner: agency B reads the national rows of the ten national shared tables, never agency A's rows, and writes no
/// national row (the database refuses; in EF the NationalSharedWriteGuard refuses first). OwnOrNational works through EF in that
/// session, PLAN-47's validator now accepts the national schema's fields for an agency, and PLAN-47's immutability triggers still
/// refuse a write to a version that is not a draft.
/// </summary>
[Collection("PostgreSql")]
public sealed class Ten048NationalRlsIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly Guid National = NationalTenant.Id;
    private static readonly Guid TenantA = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("a0000002-0000-4000-8000-000000000001");

    private sealed record Seeded(
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) NationalPolicy,
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) AgencyAPolicy,
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) AgencyBPolicy,
        Guid NationalReference,
        Guid AgencyAReference);

    private static async Task<(string Admin, string App, Seeded Seeded)> PrepareAsync(PostgreSqlFixture postgres, string name)
    {
        var admin = await Pol047Pg.MigratedAsync(postgres, "t48" + name);
        Seeded seeded;
        await using (var db = Pol047Pg.AdminContext(admin))
            seeded = await SeedAsync(db);

        await ApplyScriptAsync(admin, "ten002c-harden-tenant-rls.sql");
        await ApplyScriptAsync(admin, "ten048-national-shared-read.sql");
        var app = await postgres.CreateAppConnectionAsync(new NpgsqlConnectionStringBuilder(admin).Database!);
        return (admin, app, seeded);
    }

    private static async Task ApplyScriptAsync(string cs, string file)
    {
        var script = await global::System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", file));
        await Pol047Pg.ExecAsync(cs, script.Replace(@"\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>National rows in all ten tables, agency A's own rows beside them, agency B's own policy (to prove the triggers).</summary>
    private static async Task<Seeded> SeedAsync(HRCoreDbContext db)
    {
        var national = await Pol047Pg.SeedTemplateAsync(db, National, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);
        var agencyA = await Pol047Pg.SeedTemplateAsync(db, TenantA, PolicyTemplateScope.TenantOverride, PolicyDomains.AssetsCustody);
        var agencyB = await Pol047Pg.SeedTemplateAsync(db, TenantB, PolicyTemplateScope.TenantOverride, PolicyDomains.AssetsCustody);

        var definition = new WorkflowDefinition
        {
            TenantId = National,
            Code = "TEN048-WF",
            Name = "national",
            Version = 1,
            IsPublished = true,
            Status = WorkflowDefinitionStatus.Published,
            DomainEntityType = "Asset",
            StepsJson = """[{"level":1,"name":"approve","roleRequired":"hr-core"}]"""
        };
        db.WorkflowDefinitions.Add(definition);
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            TenantId = TenantA,
            Code = "TEN048-WF-A",
            Name = "agency A",
            Version = 1,
            IsPublished = true,
            Status = WorkflowDefinitionStatus.Published,
            DomainEntityType = "Asset",
            StepsJson = """[{"level":1,"name":"approve","roleRequired":"hr-core"}]"""
        });
        db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding { TenantId = National, Domain = PolicyDomains.AssetsCustody, WorkflowCode = "TEN048-WF" });
        db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding { TenantId = TenantA, Domain = PolicyDomains.AssetsCustody, WorkflowCode = "TEN048-WF-A" });

        foreach (var (tenant, scope, key) in new[] { (National, CodePolicyScope.National, (string?)null), (TenantA, CodePolicyScope.TenantOverride, "A-KEY") })
        {
            var template = new CodePolicyTemplate { TenantId = tenant, Name = "ten048", EntityKind = CodeEntityKind.AssetTag, Scope = scope, PolicyKey = key };
            var version = new CodePolicyVersion
            {
                TenantId = tenant,
                Status = CodePolicyVersionStatus.Published,
                SegmentsJson = """[{"kind":"Literal","value":"AH-"},{"kind":"Date","value":"YYYY"},{"kind":"Literal","value":"-"},{"kind":"Sequence","padWidth":6}]""",
                SequenceReset = CodeSequenceReset.Yearly,
                FallbackPrefix = "AH"
            };
            template.Versions.Add(version);
            template.Rules.Add(new CodePolicyRule { TenantId = tenant, Version = version, RuleKey = "R-1", WhenJson = "{}" });
            db.CodePolicyTemplates.Add(template);
        }

        var nationalReference = new ReferenceCatalogEntry { TenantId = National, CatalogKind = CatalogKind.Grade, Code = "G1", NameAr = "وطني", IsActive = true };
        var agencyReference = new ReferenceCatalogEntry { TenantId = TenantA, CatalogKind = CatalogKind.Grade, Code = "G1", NameAr = "جهة أ", IsActive = true };
        db.ReferenceCatalogEntries.AddRange(nationalReference, agencyReference);
        await db.SaveChangesAsync();
        return new Seeded(national, agencyA, agencyB, nationalReference.Id, agencyReference.Id);
    }

    private static async Task<int> NonQueryAsync(string cs, string sql, Guid? tenant)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        if (tenant is Guid t)
        {
            await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", conn);
            set.Parameters.AddWithValue("t", t.ToString());
            await set.ExecuteScalarAsync();
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Agency_B_reads_the_national_rows_of_the_ten_tables_and_never_agency_As()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, _) = await PrepareAsync(fixture, "read");

        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        foreach (var table in NationalTenant.TableNames)
        {
            Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"), table);
            var nationalRows = await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "{table}" WHERE "TenantId" = '{National}'""");
            Assert.True(nationalRows > 0, $"{table} has national rows to read");

            Assert.Equal(nationalRows, await Pol047Pg.ScalarAsync<long>(app, $"""SELECT count(*) FROM "{table}" WHERE "TenantId" = '{National}'""", TenantB));
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(app, $"""SELECT count(*) FROM "{table}" WHERE "TenantId" = '{TenantA}'""", TenantB));
        }

        // Agency A still sees its own rows and the national ones; agency B sees only its own beside the national.
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM "ReferenceCatalogEntries" WHERE "Code" = 'G1'""", TenantB));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM "ReferenceCatalogEntries" WHERE "Code" = 'G1'""", TenantA));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM "PolicyTemplates" """, TenantB));

        // The policy is SELECT only, permissive, on exactly the ten tables, and its check passes.
        var tables = await ListAsync(admin, """SELECT tablename FROM pg_policies WHERE policyname = 'national_shared_read' AND cmd = 'SELECT' AND permissive = 'PERMISSIVE' ORDER BY 1""");
        Assert.Equal(NationalTenant.TableNames.OrderBy(t => t, StringComparer.Ordinal), tables);
        Assert.Empty(await ListAsync(admin, TenantRlsMigrationHelper.VerifyNationalSharedReadSql));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Agency_B_writes_no_national_row_in_the_database_and_EF_refuses_first()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, seeded) = await PrepareAsync(fixture, "write");

        // UPDATE and DELETE see no national row to change: tenant_isolation alone governs them.
        Assert.Equal(0, await NonQueryAsync(app, $"""UPDATE "PolicyTemplates" SET "Name" = 'agency' WHERE "Id" = '{seeded.NationalPolicy.Template}'""", TenantB));
        Assert.Equal(0, await NonQueryAsync(app, $"""UPDATE "ReferenceCatalogEntries" SET "NameAr" = 'x' WHERE "Id" = '{seeded.NationalReference}'""", TenantB));
        Assert.Equal(0, await NonQueryAsync(app, """DELETE FROM "CodePolicyTemplates" """, TenantB));
        Assert.Equal(0, await NonQueryAsync(app, """DELETE FROM "WorkflowDefinitions" """, TenantB));
        // SELECT ... FOR UPDATE needs the UPDATE policy too: no national row is locked by an agency.
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(app,
            $"""SELECT count(*) FROM (SELECT 1 FROM "PolicyTemplates" WHERE "Id" = '{seeded.NationalPolicy.Template}' FOR UPDATE) l""", TenantB));

        // INSERT of a national row fails WITH CHECK; moving an own row into the national tenant fails it too.
        var insert = await Assert.ThrowsAsync<PostgresException>(() => NonQueryAsync(app, $"""
            INSERT INTO "ReferenceCatalogEntries" ("Id","TenantId","CatalogKind","Code","NameAr","IsActive","Version","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{National}','Grade','G9','x',true,1,now(),now())
            """, TenantB));
        Assert.Equal("42501", insert.SqlState);
        await NonQueryAsync(app, $"""
            INSERT INTO "ReferenceCatalogEntries" ("Id","TenantId","CatalogKind","Code","NameAr","IsActive","Version","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{TenantB}','Grade','G8','own',true,1,now(),now())
            """, TenantB);
        var move = await Assert.ThrowsAsync<PostgresException>(() => NonQueryAsync(app,
            $"""UPDATE "ReferenceCatalogEntries" SET "TenantId" = '{National}' WHERE "Code" = 'G8'""", TenantB));
        Assert.Equal("42501", move.SqlState);

        Assert.Equal("pol047", await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "Name" FROM "PolicyTemplates" WHERE "Id" = '{seeded.NationalPolicy.Template}'"""));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "CodePolicyTemplates" """));

        // In EF the guard refuses before the database is asked, so the superuser lab behaves as production.
        await using var provider = Pol047Pg.AppProvider(app);
        var refused = await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => Pol047Pg.AsTenantAsync(provider, TenantB, async db =>
        {
            var template = await db.PolicyTemplates.OwnOrNational(TenantB).FirstAsync(t => t.Id == seeded.NationalPolicy.Template);
            template.Name = "agency edit";
            return await db.SaveChangesAsync();
        }));
        Assert.Equal(nameof(PolicyTemplate), refused.EntityName);
        Assert.IsAssignableFrom<UnauthorizedAccessException>(refused);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task OwnOrNational_works_through_EF_and_the_validator_accepts_the_national_schema_for_an_agency()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (_, app, seeded) = await PrepareAsync(fixture, "ef");
        await using var provider = Pol047Pg.AppProvider(app);

        var references = await Pol047Pg.AsTenantAsync(provider, TenantB, db =>
            db.ReferenceCatalogEntries.OwnOrNational(TenantB).Where(e => e.Code == "G1").Select(e => e.Id).ToListAsync());
        Assert.Equal(new[] { seeded.NationalReference }, references);

        var agencyA = await Pol047Pg.AsTenantAsync(provider, TenantA, db =>
            db.ReferenceCatalogEntries.OwnOrNational(TenantA).Where(e => e.Code == "G1").OwnFirst(TenantA).Select(e => e.Id).ToListAsync());
        Assert.Equal(new[] { seeded.AgencyAReference, seeded.NationalReference }, agencyA);

        var definitions = await Pol047Pg.AsTenantAsync(provider, TenantB, db =>
            db.WorkflowDefinitions.OwnOrNational(TenantB).Select(d => d.Code).ToListAsync());
        Assert.Equal(new[] { "TEN048-WF" }, definitions);
        var schemas = await Pol047Pg.AsTenantAsync(provider, TenantB, db => db.PolicyFactsSchemas.NationalOnly().CountAsync());
        Assert.Equal(1, schemas);

        // PLAN-47's validator, for an agency session: the national schema's fields, the national definitions and bindings are seen.
        var issues = await Pol047Pg.AsTenantAsync(provider, TenantB, db =>
            new PolicyRuleValidationService(db, AllowAllPolicyDomainCapabilities.Instance).ValidateRuleAsync(
                PolicyDomains.AssetsCustody,
                PolicyTemplateScope.TenantOverride,
                new PolicyRuleInput("R-B-AGENCY", "agency", 10, Pol047Pg.ValidWhen,
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"TEN048-WF"}}]""")));
        Assert.DoesNotContain(issues, i => i.Code is "WHEN_NO_SCHEMA" or "WHEN_UNKNOWN_FIELD" or "THEN_WORKFLOW_UNKNOWN" or "THEN_WORKFLOW_UNBOUND");
        // PLAN-48: the national version in force (RequireEvidence only) triggers no workflow, so the ceiling lets the agency's pass.
        Assert.DoesNotContain(issues, i => i.Code.StartsWith("CEILING_", StringComparison.Ordinal));

        // A field outside the national schema is still refused (the schema is read, not ignored).
        var unknown = await Pol047Pg.AsTenantAsync(provider, TenantB, db =>
            new PolicyRuleValidationService(db, AllowAllPolicyDomainCapabilities.Instance).ValidateRuleAsync(
                PolicyDomains.AssetsCustody,
                PolicyTemplateScope.TenantOverride,
                new PolicyRuleInput("R-B-UNKNOWN", "x", 10, """{"all":[{"field":"notInSchema","operator":"Equals","value":"x"}]}""", Pol047Pg.ValidThen)));
        Assert.Contains(unknown, i => i.Code == "WHEN_UNKNOWN_FIELD");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_PLAN_47_triggers_still_refuse_a_write_to_a_version_that_is_not_a_draft()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, seeded) = await PrepareAsync(fixture, "trig");

        // Agency B's own published version: the trigger refuses a new rule.
        var own = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(app, $"""
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{seeded.AgencyBPolicy.Published}','R-SNEAK','x',5,'{Pol047Pg.ValidWhen}','{Pol047Pg.ValidThen}','{TenantB}',now(),now());
            """, TenantB));
        Assert.Equal("P0047", own.SqlState);

        // The national draft and the national published version: now visible to B's SELECT, still not to the trigger's FOR SHARE
        // lookup, so a rule of B's tenant that names them is refused.
        foreach (var version in new[] { seeded.NationalPolicy.Draft, seeded.NationalPolicy.Published })
        {
            var cross = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(app, $"""
                INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
                VALUES (gen_random_uuid(),'{version}','R-CROSS','x',5,'{Pol047Pg.ValidWhen}','{Pol047Pg.ValidThen}','{TenantB}',now(),now());
                """, TenantB));
            Assert.Equal("P0047", cross.SqlState);
        }

        // B's own draft still accepts a rule.
        await Pol047Pg.ExecAsync(app, $"""
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{seeded.AgencyBPolicy.Draft}','R-OWN','x',5,'{Pol047Pg.ValidWhen}','{Pol047Pg.ValidThen}','{TenantB}',now(),now());
            """, TenantB);

        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{seeded.NationalPolicy.Draft}'"""));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{seeded.NationalPolicy.Published}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_check_fails_when_the_policy_is_missing_on_a_listed_table_or_present_on_another()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, _, _) = await PrepareAsync(fixture, "check");

        // The script and the start-up SQL are idempotent: applying both again leaves the check green.
        await ApplyScriptAsync(admin, "ten048-national-shared-read.sql");
        await Pol047Pg.ExecAsync(admin, TenantRlsMigrationHelper.NationalSharedReadSql);
        Assert.Empty(await ListAsync(admin, TenantRlsMigrationHelper.VerifyNationalSharedReadSql));

        await Pol047Pg.ExecAsync(admin, """DROP POLICY national_shared_read ON "CodePolicyRules" """);
        await Pol047Pg.ExecAsync(admin, """CREATE POLICY national_shared_read ON "Employees" AS PERMISSIVE FOR SELECT USING (true)""");
        Assert.Equal(new[] { "CodePolicyRules(missing)", "Employees(unexpected)" }, await ListAsync(admin, TenantRlsMigrationHelper.VerifyNationalSharedReadSql));

        // The script's own check block refuses the same state.
        var script = await global::System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", "ten048-national-shared-read.sql"));
        var checkBlock = script[script.LastIndexOf("DO $$", StringComparison.Ordinal)..];
        var failed = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(admin, checkBlock));
        Assert.Contains("TEN-048 FAILED", failed.MessageText);
        Assert.Contains("Employees(unexpected)", failed.MessageText);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_session_with_no_tenant_reads_the_national_settings_only()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (_, app, _) = await PrepareAsync(fixture, "none");

        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM "ReferenceCatalogEntries" WHERE "Code" = 'G1'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(app, $"""SELECT count(*) FROM "ReferenceCatalogEntries" WHERE "TenantId" <> '{National}'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM "Employees" """));
    }

    private static async Task<List<string>> ListAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }
}
