using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-54 (P-12): <c>PAY054_LegalFrameworkPacks</c> as a server runs it — on PAY053's schema, owned by a NOSUPERUSER NOBYPASSRLS role (as
/// production's <c>*_migrator</c>), with forced row security on every tenant table, in two agencies. Every row that existed gets its
/// value from a column default (the owner sees no row under FORCE, so an UPDATE would have touched none): versions are
/// <c>HashSchemaVersion = 1</c> with their fingerprint untouched, inputs are approved and of the system, brackets, holidays and rules are
/// interim, attestations rely on no exception. The three new tables are forced tenant tables: an agency neither reads nor writes
/// another's rows. And a decision id is unique inside an agency, no longer across agencies.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054MigrationPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string PreviousMigration = "20261122153208_PAY053_SalaryChangeRequests";
    private static readonly Guid TenantA = Guid.Parse("a0000054-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000054-0000-4000-8000-0000000000b1");
    private static readonly string[] NewTables = ["StatutoryDeductionRules", "PayrollConfigurationFieldSources", "PayrollConfigurationEditors"];

    private static PayrollDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseNpgsql(connection).Options);

    /// <summary>
    /// One row of <paramref name="table"/> on the schema as it is, whatever its columns: every NOT NULL column without a default takes
    /// a value of its type, and <paramref name="values"/> name the ones the test cares about. The model of this build has columns the
    /// old schema lacks, so the old rows cannot be written through EF.
    /// </summary>
    private static async Task<Guid> InsertAsync(string admin, string table, Guid tenant, params (string Column, string SqlValue)[] values)
    {
        var id = Guid.NewGuid();
        var given = values.ToDictionary(v => v.Column, v => v.SqlValue, StringComparer.Ordinal);
        given["Id"] = $"'{id}'";
        given["TenantId"] = $"'{tenant}'";

        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using (var columns = new NpgsqlCommand(
                         """
                         SELECT column_name, data_type FROM information_schema.columns
                         WHERE table_schema = 'public' AND table_name = @t AND is_nullable = 'NO' AND column_default IS NULL
                         """, connection))
        {
            columns.Parameters.AddWithValue("t", table);
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (given.ContainsKey(name)) continue;
                given[name] = reader.GetString(1) switch
                {
                    "uuid" => "gen_random_uuid()",
                    "boolean" => "false",
                    "integer" or "bigint" or "smallint" or "numeric" => "1",
                    "date" => "current_date",
                    "timestamp with time zone" or "timestamp without time zone" => "now()",
                    _ => "'x'",
                };
            }
        }

        var sql = $"""INSERT INTO "{table}" ({string.Join(",", given.Keys.Select(k => $"\"{k}\""))}) VALUES ({string.Join(",", given.Values)})""";
        await using var insert = new NpgsqlCommand(sql, connection);
        await insert.ExecuteNonQueryAsync();
        return id;
    }

    private sealed record OldRows(Guid Version, string Fingerprint, Guid Bracket, Guid Holiday, Guid Rule, Guid Attestation, Guid Allowance, Guid Deduction, Guid Adjustment);

    /// <summary>One agency's rows as PAY053 holds them.</summary>
    private static async Task<OldRows> SeedOldRowsAsync(string admin, Guid tenant, string decisionId)
    {
        var fingerprint = PayrollRegimeSeed.CreateLaw50Configuration().ContentHash!;
        var version = await InsertAsync(admin, "PayrollConfigurations", tenant,
            ("Code", "'LAW_50_2004'"), ("VersionNumber", "1"), ("PublicationStatus", "'Published'"), ("ContentHash", $"'{fingerprint}'"),
            ("IsActive", "true"), ("EffectiveFrom", "'2000-01-01T00:00:00Z'"), ("ProrationDaysBasis", "'WorkingDays'"),
            ("RoundingMode", "'AwayFromZero'"), ("CurrencyCode", "'SYP'"));
        var bracket = await InsertAsync(admin, "TaxBrackets", tenant, ("PayrollConfigurationId", $"'{version}'"));
        var holiday = await InsertAsync(admin, "OfficialHolidays", tenant, ("PayrollConfigurationId", $"'{version}'"));
        var rule = await InsertAsync(admin, "DisbursementPriorityRules", tenant);
        var attestation = await InsertAsync(admin, "LegalAttestations", tenant, ("PayrollConfigurationId", $"'{version}'"));
        await InsertAsync(admin, "LegalDecisionRecords", tenant, ("DecisionId", $"'{decisionId}'"));
        var type = await InsertAsync(admin, "CompensationTypes", tenant, ("Code", $"'ALL-{tenant.ToString()[..4]}'"));
        var allowance = await InsertAsync(admin, "AllowanceRecords", tenant, ("AllowanceTypeId", $"'{type}'"));
        var deduction = await InsertAsync(admin, "DeductionRecords", tenant);
        var adjustment = await InsertAsync(admin, "PayrollAdjustments", tenant);
        return new OldRows(version, fingerprint, bracket, holiday, rule, attestation, allowance, deduction, adjustment);
    }

    private async Task<(string Admin, string Owner)> Pay053SchemaOwnedByAMigratorAsync()
    {
        var admin = await fixture.CreateDatabaseAsync($"pay054_mig_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"pay054_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'pay054_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            GRANT CONNECT ON DATABASE "{database}" TO {role};
            GRANT USAGE, CREATE ON SCHEMA public TO {role};
            DO $do$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {role}', t.tablename);
                END LOOP;
                FOR t IN SELECT c.table_name FROM information_schema.columns c
                         JOIN pg_tables p ON p.schemaname = 'public' AND p.tablename = c.table_name
                         WHERE c.table_schema = 'public' AND c.column_name = 'TenantId' LOOP
                    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t.table_name);
                    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t.table_name);
                END LOOP;
            END
            $do$;
            """);
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "pay054_pwd" }.ConnectionString);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_gives_every_existing_row_its_value_by_default_as_a_non_superuser_owner_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay053SchemaOwnedByAMigratorAsync();
        var a = await SeedOldRowsAsync(admin, TenantA, "LD-OLD-A");
        var b = await SeedOldRowsAsync(admin, TenantB, "LD-OLD-B");

        // The control: the owner is neither superuser nor exempt, and sees no row under FORCE. An UPDATE would have changed nothing.
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "PayrollConfigurations" """));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "AllowanceRecords" """));

        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        foreach (var rows in new[] { a, b })
        {
            Assert.Equal(1, await Pol047Pg.ScalarAsync<int>(admin, $"""SELECT "HashSchemaVersion" FROM "PayrollConfigurations" WHERE "Id" = '{rows.Version}'"""));
            Assert.Equal(rows.Fingerprint, await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "ContentHash" FROM "PayrollConfigurations" WHERE "Id" = '{rows.Version}'"""));
            foreach (var (table, id) in new[] { ("AllowanceRecords", rows.Allowance), ("DeductionRecords", rows.Deduction), ("PayrollAdjustments", rows.Adjustment) })
            {
                Assert.Equal("Approved|System", await Pol047Pg.ScalarAsync<string>(admin,
                    $"""SELECT "ApprovalStatus" || '|' || "Origin" FROM "{table}" WHERE "Id" = '{id}' AND "ApprovedBy" IS NULL AND "ApprovedAt" IS NULL AND "DecisionNote" IS NULL"""));
            }

            foreach (var (table, id) in new[] { ("TaxBrackets", rows.Bracket), ("OfficialHolidays", rows.Holiday), ("DisbursementPriorityRules", rows.Rule) })
            {
                Assert.Equal("Interim", await Pol047Pg.ScalarAsync<string>(admin,
                    $"""SELECT "LegalStatus" FROM "{table}" WHERE "Id" = '{id}' AND "InterimExceptionId" IS NULL"""));
            }

            Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, $"""SELECT "PayrollConfigurationId" IS NULL FROM "DisbursementPriorityRules" WHERE "Id" = '{rows.Rule}'"""));
            Assert.Equal("[]", await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "InterimExceptionIdsJson" FROM "LegalAttestations" WHERE "Id" = '{rows.Attestation}'"""));
        }

        // No row of any old table was left without its value, in either agency.
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT (SELECT count(*) FROM "PayrollConfigurations" WHERE "HashSchemaVersion" <> 1)
                 + (SELECT count(*) FROM "AllowanceRecords" WHERE "ApprovalStatus" <> 'Approved' OR "Origin" <> 'System')
                 + (SELECT count(*) FROM "DeductionRecords" WHERE "ApprovalStatus" <> 'Approved' OR "Origin" <> 'System')
                 + (SELECT count(*) FROM "PayrollAdjustments" WHERE "ApprovalStatus" <> 'Approved' OR "Origin" <> 'System')
            """));
        output.WriteLine("PAY054: existing versions are v1 with their fingerprints; existing inputs are Approved/System; both by column defaults.");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_new_tables_are_forced_tenant_tables_and_a_decision_id_is_unique_inside_an_agency()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay053SchemaOwnedByAMigratorAsync();
        var a = await SeedOldRowsAsync(admin, TenantA, "LD-OLD-A");
        var b = await SeedOldRowsAsync(admin, TenantB, "LD-OLD-B");
        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        foreach (var table in NewTables)
        {
            Assert.True(
                await Pol047Pg.ScalarAsync<bool>(admin, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"),
                $"{table} is not under forced row security");
            Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin,
                $"SELECT count(*) FROM pg_policies WHERE tablename = '{table}' AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL"));
        }

        // Agency A writes one row in each new table, as the non-superuser owner with its tenant set (the application's situation).
        await Pol047Pg.ExecAsync(owner, $"""
            INSERT INTO "StatutoryDeductionRules" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollConfigurationId","Code","NameAr","EmployeePercent","EmployerPercent","FixedAmount","BaseKind","LegalStatus","SortOrder")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{a.Version}','DED-SOC','s',7,14,0,'GrossPay','Interim',0);
            INSERT INTO "PayrollConfigurationFieldSources" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollConfigurationId","FieldName","LegalStatus")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{a.Version}','TaxExemptionAmount','Interim');
            INSERT INTO "PayrollConfigurationEditors" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollConfigurationId","UserId","Kind","At")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{a.Version}',gen_random_uuid(),'Imported',now());
            """, TenantA);

        foreach (var table in NewTables)
        {
            Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{table}" """, TenantA));
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{table}" """, TenantB));
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{table}" """));
        }

        // Agency B cannot write a row that belongs to agency A (WITH CHECK).
        var crossWrite = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, $"""
            INSERT INTO "PayrollConfigurationFieldSources" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollConfigurationId","FieldName","LegalStatus")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{a.Version}','DailyRateDivisor','Interim');
            """, TenantB));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossWrite.SqlState);

        // The same decision id in both agencies: refused by the old index (unique across agencies), accepted by the new one.
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await Pol047Pg.ExecAsync(owner, $"""
                INSERT INTO "LegalDecisionRecords" ("Id","TenantId","CreatedAt","UpdatedAt","DecisionId","OfficialSource","EffectiveFrom","Owner","ApprovalStatus")
                VALUES (gen_random_uuid(),'{tenant}',now(),now(),'LD-TAX-BRACKETS','s',now(),'Legal','ApprovedInterim');
                """, tenant);
        }

        var twice = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, $"""
            INSERT INTO "LegalDecisionRecords" ("Id","TenantId","CreatedAt","UpdatedAt","DecisionId","OfficialSource","EffectiveFrom","Owner","ApprovalStatus")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'LD-TAX-BRACKETS','s',now(),'Legal','ApprovedInterim');
            """, TenantA));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, twice.SqlState);
        Assert.NotEqual(a.Version, b.Version);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_goes_down_and_up_again()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.CreateDatabaseAsync($"pay054_down_{Guid.NewGuid():N}"[..40]);
        await using var db = Db(admin);
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename IN ('StatutoryDeductionRules','PayrollConfigurationFieldSources','PayrollConfigurationEditors')"));

        await migrator.MigrateAsync();
        Assert.Equal(3L, await Pol047Pg.ScalarAsync<long>(admin,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename IN ('StatutoryDeductionRules','PayrollConfigurationFieldSources','PayrollConfigurationEditors')"));
    }
}
