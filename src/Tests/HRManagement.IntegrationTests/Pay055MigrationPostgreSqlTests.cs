using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Payroll.Domain.Constants;
using Payroll.Infrastructure.Data;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-55 (P-10): <c>PAY055_EmployeeBankAccounts</c> as a server runs it — on PAY054's schema, owned by a NOSUPERUSER NOBYPASSRLS role (as
/// production's <c>*_migrator</c>), with forced row security on every tenant table, in two agencies. An account number an older build
/// left on a pay profile moves to one account <c>PendingVerification</c> per employee, in its own agency, with no registrar and no
/// verification. The migrator sees no pay profile under FORCE, so the move works only because the migration lifts FORCE on the table it
/// reads, and puts it back. The new table is a forced tenant table: an agency neither reads nor writes another's accounts.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay055MigrationPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string PreviousMigration = "20261123074128_PAY054_LegalFrameworkPacks";
    private const string Table = "EmployeeBankAccounts";
    private static readonly Guid TenantA = Guid.Parse("a0000055-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000055-0000-4000-8000-0000000000b1");

    // Planted numbers: the shape of an IBAN, never a real account (the migration moves whatever the old field held, unchecked).
    private const string NumberA = "SY55PLAN0000000000000000A1";
    private const string OlderNumberA = "SY55PLAN0000000000000000A0";
    private const string NumberB = "SY55PLAN0000000000000000B1";

    private static PayrollDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<PayrollDbContext>().UseNpgsql(connection).Options);

    /// <summary>A pay profile as PAY054's schema holds it, with the three bank columns of the old model.</summary>
    private static async Task<Guid> ProfileAsync(
        string admin, Guid tenant, Guid employee, bool active, string effectiveFrom, string? iban, string? bank = null, bool verified = false)
    {
        var id = Guid.NewGuid();
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "EmployeePayProfiles"
                ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","BaseSalary","EffectiveFrom","IsActive","BankAccountIban","BankName","BankAccountVerifiedAt")
            VALUES ('{id}','{tenant}',now(),now(),'{employee}',4500,'{effectiveFrom}',{(active ? "true" : "false")},
                    {(iban is null ? "NULL" : $"'{iban}'")},{(bank is null ? "NULL" : $"'{bank}'")},{(verified ? "now()" : "NULL")});
            """);
        return id;
    }

    private async Task<(string Admin, string Owner)> Pay054SchemaOwnedByAMigratorAsync()
    {
        var admin = await fixture.CreateDatabaseAsync($"pay055_mig_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"pay055_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'pay055_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
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
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "pay055_pwd" }.ConnectionString);
    }

    private sealed record Planted(Guid EmployeeA, Guid EmployeeAWithoutNumber, Guid EmployeeABlank, Guid EmployeeB);

    /// <summary>
    /// Agency A: an employee with two versions that both carry a number (the active one's is taken), one with no number, one with a
    /// blank number. Agency B: an employee whose number was typed with spaces and in lower case, and marked verified by the old field.
    /// </summary>
    private static async Task<Planted> PlantAsync(string admin)
    {
        var planted = new Planted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await ProfileAsync(admin, TenantA, planted.EmployeeA, active: false, "2026-01-01T00:00:00Z", OlderNumberA, "مصرف قديم");
        await ProfileAsync(admin, TenantA, planted.EmployeeA, active: true, "2026-06-01T00:00:00Z", NumberA, "  المصرف التجاري السوري  ", verified: true);
        await ProfileAsync(admin, TenantA, planted.EmployeeAWithoutNumber, active: true, "2026-01-01T00:00:00Z", iban: null);
        await ProfileAsync(admin, TenantA, planted.EmployeeABlank, active: true, "2026-01-01T00:00:00Z", iban: "   ", bank: "مصرف");
        var typed = string.Join(' ', NumberB.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
        await ProfileAsync(admin, TenantB, planted.EmployeeB, active: true, "2026-01-01T00:00:00Z", typed, bank: null, verified: true);
        return planted;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_moves_each_planted_number_to_one_pending_account_in_its_own_agency_as_a_non_superuser_owner()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay054SchemaOwnedByAMigratorAsync();
        var planted = await PlantAsync(admin);

        // The control: the owner is neither superuser nor exempt, and sees no pay profile under FORCE. Without lifting FORCE, the
        // INSERT … SELECT of the migration would have read nothing and reported success.
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "EmployeePayProfiles" """));
        Assert.Equal(5L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "EmployeePayProfiles" """));

        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        // One account per employee that had a number, and none for the others.
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "{Table}" """));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            $"""SELECT count(*) FROM "{Table}" WHERE "EmployeeId" IN ('{planted.EmployeeAWithoutNumber}','{planted.EmployeeABlank}')"""));

        // Agency A: the active version's number, pending, in agency A, its bank's name trimmed.
        Assert.Equal($"{TenantA}|0|0|{NumberA}|المصرف التجاري السوري", await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "TenantId"::text || '|' || "Status" || '|' || "AccountKind" || '|' || "AccountNumber" || '|' || "BankNameAr" FROM "{Table}" WHERE "EmployeeId" = '{planted.EmployeeA}'"""));
        // Agency B: normalised (no spaces, upper case), pending, in agency B, no bank name.
        Assert.Equal($"{TenantB}|0|0|{NumberB}|∅", await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "TenantId"::text || '|' || "Status" || '|' || "AccountKind" || '|' || "AccountNumber" || '|' || COALESCE("BankNameAr",'∅') FROM "{Table}" WHERE "EmployeeId" = '{planted.EmployeeB}'"""));

        // Never an implicit verification, and no registrar: the old field's verification date is not carried.
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, $"""
            SELECT count(*) FROM "{Table}"
            WHERE "Status" = 0 AND "VerifiedBy" IS NULL AND "VerifiedAt" IS NULL AND "VerificationReference" IS NULL
              AND "CreatedBy" IS NULL AND "SupersedesAccountId" IS NULL
            """));

        // FORCE is back on the table the move read, and the migrator sees no pay profile again.
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'EmployeePayProfiles'"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "EmployeePayProfiles" """));

        // The old columns stay, with their values: the previous tag reads them after a rollback.
        Assert.Equal(3L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'EmployeePayProfiles' AND column_name IN ('BankAccountIban','BankName','BankAccountVerifiedAt')"""));
        Assert.Equal(NumberA, await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "BankAccountIban" FROM "EmployeePayProfiles" WHERE "EmployeeId" = '{planted.EmployeeA}' AND "IsActive" """));

        // The pinned account's columns exist and are empty: no run of the old schema pinned anything.
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE column_name = 'BankAccountId' AND table_name IN ('SalaryLines','PayrollRunInputSnapshots')"""));
        output.WriteLine("PAY055: 2 planted numbers in 2 agencies moved to 2 pending accounts, each in its agency; FORCE restored.");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_accounts_table_is_a_forced_tenant_table_and_another_agency_sees_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay054SchemaOwnedByAMigratorAsync();
        var planted = await PlantAsync(admin);
        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();

        Assert.True(
            await Pol047Pg.ScalarAsync<bool>(admin, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{Table}'"),
            $"{Table} is not under forced row security");
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin,
            $"SELECT count(*) FROM pg_policies WHERE tablename = '{Table}' AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL"));

        // Each agency reads its own moved account only; a session with no agency reads none.
        Assert.Equal(planted.EmployeeA, await Pol047Pg.ScalarAsync<Guid>(owner, $"""SELECT "EmployeeId" FROM "{Table}" """, TenantA));
        Assert.Equal(planted.EmployeeB, await Pol047Pg.ScalarAsync<Guid>(owner, $"""SELECT "EmployeeId" FROM "{Table}" """, TenantB));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{Table}" """, TenantA));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{Table}" """, TenantB));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, $"""SELECT count(*) FROM "{Table}" """));

        // Agency B cannot write an account that belongs to agency A (WITH CHECK), nor change agency A's row (USING).
        var crossWrite = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, Insert(TenantA, Guid.NewGuid(), status: 0), TenantB));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossWrite.SqlState);
        await Pol047Pg.ExecAsync(owner, $"""UPDATE "{Table}" SET "Status" = 1 WHERE "EmployeeId" = '{planted.EmployeeA}'""", TenantB);
        Assert.Equal(0, await Pol047Pg.ScalarAsync<int>(admin, $"""SELECT "Status" FROM "{Table}" WHERE "EmployeeId" = '{planted.EmployeeA}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task One_account_in_force_and_one_waiting_per_employee_and_agency()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await Pay054SchemaOwnedByAMigratorAsync();
        await using (var migrator = Db(owner))
            await migrator.Database.MigrateAsync();
        var employee = Guid.NewGuid();

        // One pending (0) and one active (1) account live side by side; any number of rejected (2) and retired (3) ones too.
        foreach (var status in new[] { 0, 1, 2, 2, 3, 3 })
            await Pol047Pg.ExecAsync(owner, Insert(TenantA, employee, status), TenantA);

        foreach (var status in new[] { 0, 1 })
        {
            var second = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, Insert(TenantA, employee, status), TenantA));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, second.SqlState);
            Assert.Equal(PayrollChangeIndexNames.OneActiveOnePendingBankAccount, second.ConstraintName);
        }

        // The index is per agency: the same employee id in another agency has its own pair.
        await Pol047Pg.ExecAsync(owner, Insert(TenantB, employee, status: 0), TenantB);
        await Pol047Pg.ExecAsync(owner, Insert(TenantB, employee, status: 1), TenantB);
        Assert.Equal(8L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "{Table}" WHERE "EmployeeId" = '{employee}'"""));

        // An account a line or a snapshot names is not deleted (Restrict).
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin, $"""
            SELECT count(*) FROM pg_constraint
            WHERE contype = 'f' AND confrelid = '"{Table}"'::regclass AND confdeltype = 'r'
              AND conrelid IN ('"SalaryLines"'::regclass, '"PayrollRunInputSnapshots"'::regclass)
            """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_goes_down_and_up_again()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.CreateDatabaseAsync($"pay055_down_{Guid.NewGuid():N}"[..40]);
        await using var db = Db(admin);
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename = '{Table}'"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE column_name = 'BankAccountId' AND table_name IN ('SalaryLines','PayrollRunInputSnapshots')"""));

        await migrator.MigrateAsync();
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename = '{Table}'"));
    }

    private static string Insert(Guid tenant, Guid employee, int status) => $"""
        INSERT INTO "{Table}" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","AccountKind","AccountNumber","Status")
        VALUES (gen_random_uuid(),'{tenant}',now(),now(),'{employee}',0,'SY55PLAN00000000000000{status}{Random.Shared.Next(100, 999)}',{status});
        """;
}
