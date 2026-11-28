using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60 (P-5): <c>SEP060_SeparationStatusGovernance</c> as a server runs it — on MSG040's schema, owned by a NOSUPERUSER NOBYPASSRLS
/// role (as production's <c>*_migrator</c>), with forced row security on every tenant table, in two agencies. Each backfill rule meets a
/// planted row: the four flag and saga combinations, a duplicate with and without a started saga, a record with no employee, transactions
/// inside and outside the five minutes. The migrator sees no row under FORCE, so the backfill works only because it lifts FORCE on each
/// table it touches, and puts it back.
/// </summary>
[Collection("PostgreSql")]
public sealed class Sep060MigrationPostgreSqlTests(PostgreSqlFixture fixture)
{
    public const string PreviousMigration = "20261104012037_MSG040_InboxStateKeys";

    private static readonly Guid TenantA = Guid.Parse("a0000060-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000060-0000-4000-8000-0000000000b1");

    private static readonly string[] TouchedTables = ["SeparationRecords", "SeparationChecklists", "SeparationSagaStates", "ServiceTransactions"];

    private static ContractsDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(connection).Options);

    /// <summary>MSG040's schema, every table owned by a role that is neither superuser nor exempt, every tenant table forced.</summary>
    public static async Task<(string Admin, string Owner)> PreviousSchemaOwnedByAMigratorAsync(PostgreSqlFixture postgres, string prefix)
    {
        var admin = await postgres.CreateDatabaseAsync($"{prefix}_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"{prefix}_own_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'sep060_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
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
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "sep060_pwd" }.ConnectionString);
    }

    /// <summary>A record as MSG040's schema holds it (the previous tag's columns only).</summary>
    public static async Task<Guid> RecordAsync(
        string admin, Guid tenant, Guid employee, bool published, bool cancelled = false, DateTime? createdAt = null)
    {
        var id = Guid.NewGuid();
        var created = (createdAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss.fff");
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "SeparationRecords"
                ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","SeparationType","EffectiveDate","IsCancelled","SeparationEventPublished")
            VALUES ('{id}','{tenant}','{created}',now(),'{employee}','Resignation','2026-10-01',{(cancelled ? "true" : "false")},{(published ? "true" : "false")});
            """);
        return id;
    }

    public static async Task SagaAsync(string admin, Guid tenant, Guid record, string state) =>
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "SeparationSagaStates"
                ("CorrelationId","CurrentState","TenantId","SeparationRecordId","EmployeeId","SeparationType","EffectiveDate",
                 "RemainingLeaveDays","PendingParties","CompletedParties","CreatedAt","UpdatedAt")
            VALUES ('{record}','{state}','{tenant}','{record}','{Guid.NewGuid()}','Resignation','2026-10-01',0,'','',now(),now());
            """);

    public static async Task<Guid> TransactionAsync(string admin, Guid tenant, Guid employee, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "ServiceTransactions" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","TransactionType","State")
            VALUES ('{id}','{tenant}','{createdAt:yyyy-MM-dd HH:mm:ss.fff}',now(),'{employee}','Separation','Draft');
            """);
        return id;
    }

    public static async Task OldItemsAsync(string admin, Guid tenant, Guid record)
    {
        foreach (var step in new[] { "ContractTerminated", "PayrollSettled", "CardRevoked", "AccountDisabled", "AssetsReturned", "Archived" })
            await Pol047Pg.ExecAsync(admin, $"""
                INSERT INTO "SeparationChecklists" ("Id","TenantId","CreatedAt","UpdatedAt","SeparationRecordId","Step","IsCompleted","IsRequired","SlaHours")
                VALUES ('{Guid.NewGuid()}','{tenant}',now(),now(),'{record}','{step}',false,true,24);
                """);
    }

    private static Task<string> StatusAsync(string admin, Guid id) =>
        Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "Status" || '|' || "IsCancelled"::text || '|' || "IsLegacyDraft"::text FROM "SeparationRecords" WHERE "Id" = '{id}'""");

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_backfill_gives_every_planted_record_its_status_as_a_non_superuser_owner_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync(fixture, "sep060m");
        var now = DateTime.UtcNow;

        // Rules 1 to 4.
        var cancelled = await RecordAsync(admin, TenantA, Guid.NewGuid(), published: false, cancelled: true);
        var completed = await RecordAsync(admin, TenantA, Guid.NewGuid(), published: true);
        await SagaAsync(admin, TenantA, completed, "Completed");
        var intervention = await RecordAsync(admin, TenantA, Guid.NewGuid(), published: true);
        await SagaAsync(admin, TenantA, intervention, "AwaitingIntervention");
        var sagaCancelled = await RecordAsync(admin, TenantA, Guid.NewGuid(), published: true);
        await SagaAsync(admin, TenantA, sagaCancelled, "Cancelled");
        var noSaga = await RecordAsync(admin, TenantA, Guid.NewGuid(), published: true);
        var faulted = await RecordAsync(admin, TenantA, Guid.NewGuid(), published: true);
        await SagaAsync(admin, TenantA, faulted, "Faulted");
        var draftEmployee = Guid.NewGuid();
        var draft = await RecordAsync(admin, TenantA, draftEmployee, published: false, createdAt: now.AddMinutes(-30));
        await OldItemsAsync(admin, TenantA, draft);
        await OldItemsAsync(admin, TenantA, cancelled);
        var noEmployee = await RecordAsync(admin, TenantA, Guid.Empty, published: false);

        // Rule 5: E1 has a started (published, saga running) older record and an unpublished newer one; E2 two unpublished.
        var e1 = Guid.NewGuid();
        var e1Started = await RecordAsync(admin, TenantA, e1, published: true, createdAt: now.AddDays(-3));
        await SagaAsync(admin, TenantA, e1Started, "AwaitingParties");
        var e1Newer = await RecordAsync(admin, TenantA, e1, published: false, createdAt: now.AddDays(-1));
        var e2 = Guid.NewGuid();
        var e2Older = await RecordAsync(admin, TenantA, e2, published: false, createdAt: now.AddDays(-2));
        var e2Newer = await RecordAsync(admin, TenantA, e2, published: false, createdAt: now.AddDays(-1));
        // The same employee in agency B is not a duplicate of agency A's.
        var e2InB = await RecordAsync(admin, TenantB, e2, published: false);

        // Rule 7: a transaction 2 minutes from the draft is linked; one 10 minutes from noSaga's record is not; of two within the
        // window of e2Newer, the nearer is linked and the other stays unlinked.
        var draftTx = await TransactionAsync(admin, TenantA, draftEmployee, now.AddMinutes(-28));
        var noSagaEmployee = await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "EmployeeId" FROM "SeparationRecords" WHERE "Id" = '{noSaga}'""");
        var noSagaCreated = await Pol047Pg.ScalarAsync<DateTime>(admin, $"""SELECT "CreatedAt" FROM "SeparationRecords" WHERE "Id" = '{noSaga}'""");
        var farTx = await TransactionAsync(admin, TenantA, noSagaEmployee, noSagaCreated.AddMinutes(10));
        var nearTx = await TransactionAsync(admin, TenantA, e2, now.AddDays(-1).AddMinutes(1));
        var fartherTx = await TransactionAsync(admin, TenantA, e2, now.AddDays(-1).AddMinutes(3));

        // The control: the owner is neither superuser nor exempt, and sees nothing under FORCE.
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "SeparationRecords" """));

        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        Assert.Equal("Cancelled|true|false", await StatusAsync(admin, cancelled));
        Assert.Equal("Completed|false|false", await StatusAsync(admin, completed));
        Assert.Equal("AwaitingIntervention|false|false", await StatusAsync(admin, intervention));
        Assert.Equal("Cancelled|true|false", await StatusAsync(admin, sagaCancelled));
        Assert.Equal("Executing|false|false", await StatusAsync(admin, noSaga));
        Assert.Equal("Executing|false|false", await StatusAsync(admin, faulted));
        Assert.Equal("Draft|false|true", await StatusAsync(admin, draft));
        Assert.Equal("Cancelled|true|false", await StatusAsync(admin, noEmployee));
        Assert.Equal("PLAN-60: سجل بلا موظف — لا يمكن اعتماده", await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "CancellationReason" FROM "SeparationRecords" WHERE "Id" = '{noEmployee}'"""));

        // Rule 5: the started record is kept, never cancelled; of the two drafts the newest is kept.
        Assert.Equal("Executing|false|false", await StatusAsync(admin, e1Started));
        Assert.Equal("Cancelled|true|false", await StatusAsync(admin, e1Newer));
        Assert.Equal("Cancelled|true|false", await StatusAsync(admin, e2Older));
        Assert.Equal("Draft|false|true", await StatusAsync(admin, e2Newer));
        Assert.Equal("Draft|false|true", await StatusAsync(admin, e2InB));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM "SeparationRecords" WHERE "CancellationReason" = 'PLAN-60: سجل مكرر'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM (SELECT 1 FROM "SeparationRecords"
                                  WHERE "Status" IN ('Draft','PendingApproval','Approved','Executing','AwaitingIntervention')
                                  GROUP BY "TenantId", "EmployeeId" HAVING count(*) > 1) d
            """));

        // Rule 6: the open draft's six items have their modes and parties, and it gained the four new items; the cancelled one did not.
        Assert.Equal(10L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "SeparationChecklists" WHERE "SeparationRecordId" = '{draft}'"""));
        Assert.Equal(6L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "SeparationChecklists" WHERE "SeparationRecordId" = '{cancelled}'"""));
        Assert.Equal("ContractTerminated:System:|DecisionIssued:System:|EmployeeNotified:Manual:|PayrollSettled:PartyAck:Payroll",
            await Pol047Pg.ScalarAsync<string>(admin, $"""
                SELECT string_agg("Step" || ':' || "CompletionMode" || ':' || coalesce("PartyName", ''), '|' ORDER BY "Step")
                FROM "SeparationChecklists"
                WHERE "SeparationRecordId" = '{draft}' AND "Step" IN ('ContractTerminated','DecisionIssued','EmployeeNotified','PayrollSettled')
                """));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM "SeparationChecklists" WHERE "Step" NOT IN ('DecisionIssued','ContractTerminated','EmployeeNotified') AND "PartyName" IS NULL"""));
        // Open records with no item before (the started and executing ones) received the four new items only.
        Assert.Equal(4L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "SeparationChecklists" WHERE "SeparationRecordId" = '{noSaga}'"""));

        // Rule 7.
        Assert.Equal(draft, await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "SeparationRecordId" FROM "ServiceTransactions" WHERE "Id" = '{draftTx}'"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, $"""SELECT "SeparationRecordId" IS NULL FROM "ServiceTransactions" WHERE "Id" = '{farTx}'"""));
        Assert.Equal(e2Newer, await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "SeparationRecordId" FROM "ServiceTransactions" WHERE "Id" = '{nearTx}'"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, $"""SELECT "SeparationRecordId" IS NULL FROM "ServiceTransactions" WHERE "Id" = '{fartherTx}'"""));

        // The indexes, after the backfill: one open record per employee, one transaction per record.
        Assert.Contains("WHERE", await Pol047Pg.ScalarAsync<string>(admin,
            """SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_SeparationRecords_Tenant_Employee_Open'"""));
        Assert.Contains("UNIQUE", await Pol047Pg.ScalarAsync<string>(admin,
            """SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_ServiceTransactions_SeparationRecordId'"""));

        // FORCE is back on every table the backfill touched, and the migrator sees no row again.
        foreach (var table in TouchedTables)
            Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"), table);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "SeparationRecords" """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_started_records_of_one_employee_stop_the_migration_with_their_ids_and_cancel_neither()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync(fixture, "sep060x");
        var employee = Guid.NewGuid();
        var first = await RecordAsync(admin, TenantA, employee, published: true, createdAt: DateTime.UtcNow.AddDays(-2));
        var second = await RecordAsync(admin, TenantA, employee, published: false, createdAt: DateTime.UtcNow.AddDays(-1));
        await SagaAsync(admin, TenantA, second, "AwaitingParties");

        await using (var migrator = Db(owner))
        {
            var stopped = await Assert.ThrowsAsync<PostgresException>(() => migrator.Database.MigrateAsync());
            Assert.Contains(first.ToString(), stopped.MessageText);
            Assert.Contains(second.ToString(), stopped.MessageText);
        }

        // The migration's transaction rolled back: the schema is MSG040's, both records as they were, FORCE as it was.
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'SeparationRecords' AND column_name = 'Status'"""));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "SeparationRecords" WHERE "EmployeeId" = '{employee}' AND NOT "IsCancelled" """));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'SeparationRecords'"));
    }
}
