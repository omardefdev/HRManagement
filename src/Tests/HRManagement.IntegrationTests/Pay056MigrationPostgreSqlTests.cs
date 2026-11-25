using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Payroll.Infrastructure.Data;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (P-11): <c>PAY056_NetFormula</c> as a server runs it — on PAY055's schema, owned by a NOSUPERUSER NOBYPASSRLS role (as
/// production's <c>*_migrator</c>), with forced row security on every tenant table, in two agencies. Every existing line gets the sum
/// of its adjustment components that reference an adjustment, each in its own agency, and no amount changes. A correction line of an
/// older build, whose difference was also written into the base as a component that references nothing, is counted once. The migrator
/// sees no line and no component under FORCE, so the fill works only because the migration lifts FORCE on both tables, and puts it back.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay056MigrationPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string PreviousMigration = "20261124052855_PAY055_EmployeeBankAccounts";
    private const string NetFormula = "20261125051113_PAY056_NetFormula";
    private static readonly Guid TenantA = Guid.Parse("a0000056-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000056-0000-4000-8000-0000000000b1");

    private static PayrollDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseNpgsql(connection).Options);

    private async Task<(string Admin, string Owner)> Pay055SchemaOwnedByAMigratorAsync()
    {
        var admin = await fixture.CreateDatabaseAsync($"pay056_mig_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"pay056_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'pay056_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
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
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "pay056_pwd" }.ConnectionString);
    }

    private static async Task<Guid> RunAsync(string admin, Guid tenant, string runType, int status)
    {
        var id = Guid.NewGuid();
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "PayrollRuns"
                ("Id","TenantId","CreatedAt","UpdatedAt","PeriodStart","PeriodEnd","RunDate","Status","RunType","TotalNetPay","ResultsFrozen","ValidatedAtCalculate")
            VALUES ('{id}','{tenant}',now(),now(),'2026-03-01T00:00:00Z','2026-03-31T00:00:00Z','2026-04-01T00:00:00Z',{status},'{runType}',0,true,true);
            """);
        return id;
    }

    private static async Task<Guid> LineAsync(string admin, Guid tenant, Guid run, decimal gross, decimal deductions, decimal net)
    {
        var id = Guid.NewGuid();
        await Pol047Pg.ExecAsync(admin, FormattableString.Invariant($"""
            INSERT INTO "SalaryLines" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollRunId","EmployeeId","GrossPay","TotalDeductions","NetPay")
            VALUES ('{id}','{tenant}',now(),now(),'{run}','{Guid.NewGuid()}',{gross},{deductions},{net});
            """));
        return id;
    }

    private static Task ComponentAsync(string admin, Guid tenant, Guid line, string type, string name, decimal amount, bool referenced) =>
        Pol047Pg.ExecAsync(admin, FormattableString.Invariant($"""
            INSERT INTO "SalaryLineComponents" ("Id","TenantId","CreatedAt","UpdatedAt","SalaryLineId","ComponentType","ComponentName","Amount","ReferenceId")
            VALUES (gen_random_uuid(),'{tenant}',now(),now(),'{line}','{type}','{name}',{amount},{(referenced ? "gen_random_uuid()" : "NULL")});
            """));

    private sealed record Planted(Guid WithTwoAdjustments, Guid WithNone, Guid OtherAgency, Guid HistoricalCorrection, Guid ZeroSum);

    /// <summary>
    /// Agency A: a regular line with adjustments of +50,000 and −20,000 (posted by an older build, whose posting dropped them from the
    /// net); a line with no adjustment; a line whose adjustments cancel out; and a correction line of an older build (the difference
    /// in the base with no reference, and again as a referenced adjustment). Agency B: a line with one negative adjustment with decimals.
    /// </summary>
    private static async Task<Planted> PlantAsync(string admin)
    {
        var regularA = await RunAsync(admin, TenantA, "Regular", status: 2);
        var correctionA = await RunAsync(admin, TenantA, "Correction", status: 1);
        var regularB = await RunAsync(admin, TenantB, "Regular", status: 2);

        var two = await LineAsync(admin, TenantA, regularA, gross: 900_000m, deductions: 100_000m, net: 800_000m);
        await ComponentAsync(admin, TenantA, two, "BaseSalary", "الراتب الأساسي", 900_000m, referenced: false);
        await ComponentAsync(admin, TenantA, two, "Deduction", "تأمينات", -100_000m, referenced: true);
        await ComponentAsync(admin, TenantA, two, "Adjustment", "فرق نقل", 50_000m, referenced: true);
        await ComponentAsync(admin, TenantA, two, "Adjustment", "تسوية يدوية", -20_000m, referenced: true);

        var none = await LineAsync(admin, TenantA, regularA, gross: 700_000m, deductions: 70_000m, net: 630_000m);
        await ComponentAsync(admin, TenantA, none, "BaseSalary", "الراتب الأساسي", 700_000m, referenced: false);
        await ComponentAsync(admin, TenantA, none, "Deduction", "تأمينات", -70_000m, referenced: true);

        var zeroSum = await LineAsync(admin, TenantA, regularA, gross: 500_000m, deductions: 0m, net: 500_000m);
        await ComponentAsync(admin, TenantA, zeroSum, "BaseSalary", "الراتب الأساسي", 500_000m, referenced: false);
        await ComponentAsync(admin, TenantA, zeroSum, "Adjustment", "تسوية", 10_000m, referenced: true);
        await ComponentAsync(admin, TenantA, zeroSum, "Adjustment", "عكس التسوية", -10_000m, referenced: true);

        var correction = await LineAsync(admin, TenantA, correctionA, gross: 1_000m, deductions: 0m, net: 1_000m);
        await ComponentAsync(admin, TenantA, correction, "Adjustment", "تصحيح رجعي — فرق إجمالي", 1_000m, referenced: false);
        await ComponentAsync(admin, TenantA, correction, "Adjustment", "فرق ترفيع", 1_000m, referenced: true);

        var other = await LineAsync(admin, TenantB, regularB, gross: 600_000m, deductions: 60_000m, net: 532_499.50m);
        await ComponentAsync(admin, TenantB, other, "BaseSalary", "الراتب الأساسي", 600_000m, referenced: false);
        await ComponentAsync(admin, TenantB, other, "Deduction", "تأمينات", -60_000m, referenced: true);
        await ComponentAsync(admin, TenantB, other, "Adjustment", "استرداد", -7_500.50m, referenced: true);

        return new Planted(two, none, other, correction, zeroSum);
    }

    private const string AmountsChecksum = """
        SELECT md5(string_agg("Id"::text || '|' || "GrossPay"::text || '|' || "TotalDeductions"::text || '|' || "NetPay"::text, ';' ORDER BY "Id"))
        FROM "SalaryLines"
        """;

    private static Task<decimal> AdjustmentsOfAsync(string admin, Guid line) =>
        Pol047Pg.ScalarAsync<decimal>(admin, $"""SELECT "TotalAdjustments" FROM "SalaryLines" WHERE "Id" = '{line}'""");

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_fills_each_lines_adjustments_in_its_own_agency_and_changes_no_amount_as_a_non_superuser_owner()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay055SchemaOwnedByAMigratorAsync();
        var planted = await PlantAsync(admin);
        var amountsBefore = await Pol047Pg.ScalarAsync<string>(admin, AmountsChecksum);
        var componentsBefore = await Pol047Pg.ScalarAsync<string>(admin,
            """SELECT md5(string_agg("Id"::text || '|' || "Amount"::text, ';' ORDER BY "Id")) FROM "SalaryLineComponents" """);

        // The control: the owner is neither superuser nor exempt, and sees no line and no component under FORCE. Without lifting FORCE
        // on both tables, the UPDATE … FROM of the migration would have read nothing, changed nothing and reported success.
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "SalaryLines" """));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "SalaryLineComponents" """));
        Assert.Equal(5L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "SalaryLines" """));

        await using (var migrator = Db(owner))
            await migrator.GetService<IMigrator>().MigrateAsync(NetFormula);

        // Each line's adjustments, from its referenced adjustment components.
        Assert.Equal(30_000m, await AdjustmentsOfAsync(admin, planted.WithTwoAdjustments));
        Assert.Equal(0m, await AdjustmentsOfAsync(admin, planted.WithNone));
        Assert.Equal(0m, await AdjustmentsOfAsync(admin, planted.ZeroSum));
        Assert.Equal(-7_500.50m, await AdjustmentsOfAsync(admin, planted.OtherAgency));
        // The older correction line: its difference is counted once (the referenced adjustment), not twice.
        Assert.Equal(1_000m, await AdjustmentsOfAsync(admin, planted.HistoricalCorrection));

        // Each agency's line stayed in its agency, and a filled value is readable only there.
        Assert.Equal(TenantA, await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "TenantId" FROM "SalaryLines" WHERE "Id" = '{planted.WithTwoAdjustments}'"""));
        Assert.Equal(TenantB, await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "TenantId" FROM "SalaryLines" WHERE "Id" = '{planted.OtherAgency}'"""));
        Assert.Equal(31_000m, await Pol047Pg.ScalarAsync<decimal>(owner, """SELECT sum("TotalAdjustments") FROM "SalaryLines" """, TenantA));
        Assert.Equal(-7_500.50m, await Pol047Pg.ScalarAsync<decimal>(owner, """SELECT sum("TotalAdjustments") FROM "SalaryLines" """, TenantB));

        // No amount changed: no net, gross or deduction of any line, and no component. A line posted by an older build keeps the net
        // it was posted with, whatever the formula says of it (here 800,000, not 830,000).
        Assert.Equal(amountsBefore, await Pol047Pg.ScalarAsync<string>(admin, AmountsChecksum));
        Assert.Equal(componentsBefore, await Pol047Pg.ScalarAsync<string>(admin,
            """SELECT md5(string_agg("Id"::text || '|' || "Amount"::text, ';' ORDER BY "Id")) FROM "SalaryLineComponents" """));
        Assert.Equal(800_000m, await Pol047Pg.ScalarAsync<decimal>(admin, $"""SELECT "NetPay" FROM "SalaryLines" WHERE "Id" = '{planted.WithTwoAdjustments}'"""));

        // FORCE is back on both tables, and the migrator sees neither again.
        foreach (var table in new[] { "SalaryLines", "SalaryLineComponents" })
        {
            Assert.True(
                await Pol047Pg.ScalarAsync<bool>(admin, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"),
                $"{table} is not under forced row security after the migration");
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{table}" """));
        }

        output.WriteLine("PAY056_NetFormula: 5 planted lines in 2 agencies; 3 filled (30000, -7500.50, 1000), 2 left at zero; no amount changed; FORCE restored on both tables.");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Every_migration_after_it_also_runs_as_the_non_superuser_owner_and_keeps_the_filled_values()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay055SchemaOwnedByAMigratorAsync();
        var planted = await PlantAsync(admin);
        var amountsBefore = await Pol047Pg.ScalarAsync<string>(admin, AmountsChecksum);

        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        Assert.Equal(30_000m, await AdjustmentsOfAsync(admin, planted.WithTwoAdjustments));
        Assert.Equal(amountsBefore, await Pol047Pg.ScalarAsync<string>(admin, AmountsChecksum));
        await using var check = Db(admin);
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_goes_down_and_up_again()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.CreateDatabaseAsync($"pay056_down_{Guid.NewGuid():N}"[..40]);
        await using var db = Db(admin);
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'SalaryLines' AND column_name = 'TotalAdjustments'"""));

        await migrator.MigrateAsync();
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'SalaryLines' AND column_name = 'TotalAdjustments'"""));
    }
}
