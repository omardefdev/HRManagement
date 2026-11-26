using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Payroll.Domain.Constants;
using Payroll.Infrastructure.Data;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (P7): <c>PAY058_DisbursementGovernance</c> as a server runs it — on LVA057's schema, owned by a NOSUPERUSER NOBYPASSRLS role
/// (as production's <c>*_migrator</c>), with forced row security on every tenant table, in two agencies. Every existing approval counts
/// for its run's first version, every existing batch is a legacy batch with no arrears and every existing order has no hold and no file;
/// no amount changes. The two new tables are tenant tables under forced row security, with one file per sequence and one pending
/// request per target; the migration goes down and up again.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay058MigrationPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string PreviousMigration = "20261125221004_LVA057_AttendanceHandoff";
    private static readonly Guid TenantA = Guid.Parse("a0000058-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000058-0000-4000-8000-0000000000b1");

    private static PayrollDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseNpgsql(connection).Options);

    private async Task<(string Admin, string Owner)> Lva057SchemaOwnedByAMigratorAsync()
    {
        var admin = await fixture.CreateDatabaseAsync($"pay058_mig_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"pay058_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'pay058_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
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
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "pay058_pwd" }.ConnectionString);
    }

    /// <summary>A posted run of an agency with an approval, a sent batch and one order: what an older build left.</summary>
    private static async Task<Guid> PlantAsync(string admin, Guid tenant, decimal amount)
    {
        var run = Guid.NewGuid();
        var batch = Guid.NewGuid();
        await Pol047Pg.ExecAsync(admin, FormattableString.Invariant($"""
            INSERT INTO "PayrollRuns"
                ("Id","TenantId","CreatedAt","UpdatedAt","PeriodStart","PeriodEnd","RunDate","Status","RunType","TotalNetPay","ResultsFrozen","ValidatedAtCalculate")
            VALUES ('{run}','{tenant}',now(),now(),'2026-03-01T00:00:00Z','2026-03-31T00:00:00Z','2026-04-01T00:00:00Z',2,'Regular',{amount},true,true);
            INSERT INTO "PayrollRunApprovals" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollRunId","ApproverId","ApproverRole","DecidedAt","Decision")
            VALUES (gen_random_uuid(),'{tenant}',now(),now(),'{run}',gen_random_uuid(),'payroll-approver',now(),'Approved');
            INSERT INTO "PaymentBatches" ("Id","TenantId","CreatedAt","UpdatedAt","PayrollRunId","BatchKind","Status","TotalAmount","DisbursedAmount","IsPartial")
            VALUES ('{batch}','{tenant}',now(),now(),'{run}','Disbursement','Processed',{amount},{amount},false);
            INSERT INTO "PaymentOrders"
                ("Id","TenantId","CreatedAt","UpdatedAt","PayrollRunId","PaymentBatchId","SalaryLineId","EmployeeId","Amount","CurrencyCode","Channel","IdempotencyKey","Status")
            VALUES (gen_random_uuid(),'{tenant}',now(),now(),'{run}','{batch}',gen_random_uuid(),gen_random_uuid(),{amount},'SYP','treasury-file','{Guid.NewGuid():N}','Submitted');
            """));
        return batch;
    }

    private const string AmountsChecksum = """
        SELECT md5(
            (SELECT string_agg("Id"::text || '|' || "TotalAmount"::text || '|' || "DisbursedAmount"::text || '|' || "Status", ';' ORDER BY "Id") FROM "PaymentBatches")
            || '#' ||
            (SELECT string_agg("Id"::text || '|' || "Amount"::text || '|' || "Status", ';' ORDER BY "Id") FROM "PaymentOrders"))
        """;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_as_a_non_superuser_owner_keeps_every_old_row_as_it_was_and_forces_row_security_on_its_tables()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Lva057SchemaOwnedByAMigratorAsync();
        var batchA = await PlantAsync(admin, TenantA, 812_500.25m);
        await PlantAsync(admin, TenantB, 640_000m);
        var amountsBefore = await Pol047Pg.ScalarAsync<string>(admin, AmountsChecksum);

        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        // Every approval of an older build was taken on its run's first (and only counted) version.
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PayrollRunApprovals" WHERE "Revision" = 1"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PayrollRunApprovals" WHERE "Revision" <> 1"""));
        // Every batch is a legacy batch with no arrears part; every order has no hold reason and no file.
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PaymentBatches" WHERE "Source" = 0 AND "ArrearsAmount" = 0"""));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "PaymentOrders" WHERE "HoldReason" IS NULL AND "PaymentBatchFileId" IS NULL"""));
        Assert.Equal(amountsBefore, await Pol047Pg.ScalarAsync<string>(admin, AmountsChecksum));

        // The two new tables are tenant tables: forced row security with the tenant policy (USING and WITH CHECK).
        foreach (var table in new[] { "PaymentBatchFiles", "PayrollDualControlRequests" })
        {
            Assert.True(
                await Pol047Pg.ScalarAsync<bool>(admin, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"),
                $"{table} is not under forced row security");
            Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin,
                $"SELECT count(*) FROM pg_policies WHERE tablename = '{table}' AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL"));
        }

        // An agency writes and reads its own requests only, and one request waits on a target at a time.
        var target = Guid.NewGuid();
        const string Payload = """{"Reason":"x"}""";
        var request = $"""
            INSERT INTO "PayrollDualControlRequests" ("Id","TenantId","CreatedAt","UpdatedAt","Kind","TargetId","PayloadJson","RequestedBy","RequestedAt","ExpiresAt","Status")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),0,'{target}','{Payload}',gen_random_uuid(),now(),now() + interval '72 hours',0);
            """;
        await Pol047Pg.ExecAsync(owner, request, TenantA);
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "PayrollDualControlRequests" """, TenantA));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "PayrollDualControlRequests" """, TenantB));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "PayrollDualControlRequests" """));
        var crossWrite = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, request, TenantB));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossWrite.SqlState);
        var secondPending = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, request, TenantA));
        Assert.Equal((PostgresErrorCodes.UniqueViolation, PayrollDisbursementIndexNames.OnePendingDualControlRequest), (secondPending.SqlState, secondPending.ConstraintName));

        // One file per sequence of a batch.
        var file = $"""
            INSERT INTO "PaymentBatchFiles" ("Id","TenantId","CreatedAt","UpdatedAt","PaymentBatchId","Sequence","Kind","ControlCount","ControlTotal","Sha256","Content","GeneratedBy","GeneratedAt")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{batchA}',1,0,1,812500.25,repeat('a',64),'\x00'::bytea,gen_random_uuid(),now());
            """;
        await Pol047Pg.ExecAsync(owner, file, TenantA);
        var secondFile = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, file, TenantA));
        Assert.Equal((PostgresErrorCodes.UniqueViolation, PayrollDisbursementIndexNames.OneFilePerSequence), (secondFile.SqlState, secondFile.ConstraintName));

        await using var check = Db(admin);
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        output.WriteLine("PAY058 as the owner: 2 approvals at revision 1, 2 legacy batches, 2 orders unchanged; 2 new tables under forced row security.");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_goes_down_and_up_again()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.CreateDatabaseAsync($"pay058_down_{Guid.NewGuid():N}"[..40]);
        await using var db = Db(admin);
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();
        const string Exists = """SELECT count(*) FROM information_schema.tables WHERE table_name IN ('PaymentBatchFiles','PayrollDualControlRequests')""";

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, Exists));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'PaymentOrders' AND column_name = 'HoldReason'"""));

        await migrator.MigrateAsync();
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, Exists));
    }
}
