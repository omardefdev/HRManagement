using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Migrations;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-57 (P-11): <c>LVA057_AttendancePeriods</c> as a server runs it — on the previous schema, owned by a NOSUPERUSER NOBYPASSRLS
/// role (as production's <c>*_migrator</c>), with forced row security on every tenant table, in two agencies. A record an approved
/// leave made («في إجازة» with the note the leave consumer wrote) gets <c>Source</c> = leave, each in its own agency; a «في إجازة»
/// typed by hand stays manual, and so does every other record; nothing is deleted. The migrator sees no attendance record under
/// FORCE, so the fill works only because the migration lifts FORCE around it, and puts it back. The four new tables are under forced
/// row security with the tenant policy.
/// </summary>
[Collection("PostgreSql")]
public sealed class Lva057MigrationPostgreSqlTests(PostgreSqlFixture fixture)
{
    private const string PreviousMigration = "20261112212437_HCM052_OrgScopeKeys";
    private const string Lva057 = "20261125205352_LVA057_AttendancePeriods";
    private const int OnLeave = 2;
    private const int Present = 0;
    private const int Manual = 0;
    private const int Leave = 2;
    private static readonly Guid TenantA = Guid.Parse("a0000057-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000057-0000-4000-8000-0000000000b1");

    private static HRCoreDbContext Db(string connection, Guid tenant)
    {
        var context = new Msg040FixedTenant(tenant);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(connection).AddInterceptors(new TenantIdStampInterceptor(context)).Options,
            context);
    }

    private async Task<(string Admin, string Owner)> PreviousSchemaOwnedByAMigratorAsync()
    {
        var admin = await fixture.CreateDatabaseAsync($"lva057_mig_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin, TenantA))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"lva057_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        var login = $"lva057-{Guid.NewGuid():N}";
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD '{login}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
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
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = login }.ConnectionString);
    }

    /// <summary>
    /// An employee of an agency, written through the model as the superuser on the previous schema (the Employees table is not
    /// changed by this migration, so the current model writes it as it stands).
    /// </summary>
    private static async Task<Guid> EmployeeAsync(string admin, Guid tenant)
    {
        var id = Guid.NewGuid();
        await using var db = Db(admin, tenant);
        db.Employees.Add(new Employee
        {
            Id = id,
            FirstName = "موظف",
            LastName = "ترحيل",
            FullName = "موظف ترحيل",
            NationalId = "N" + id.ToString("N")[..10],
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Male,
            CurrentStatus = EmploymentStatus.Active
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> RecordAsync(string admin, Guid tenant, Guid employee, string day, int status, string? notes)
    {
        var id = Guid.NewGuid();
        var note = notes is null ? "NULL" : $"'{notes}'";
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "DailyAttendanceRecords" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","AttendanceDate","Status","Notes")
            VALUES ('{id}','{tenant}',now(),now(),'{employee}','{day}',{status},{note});
            """);
        return id;
    }

    private static Task<int> SourceOfAsync(string admin, Guid record) =>
        Pol047Pg.ScalarAsync<int>(admin, $"""SELECT "Source" FROM "DailyAttendanceRecords" WHERE "Id" = '{record}'""");

    private const string RecordsChecksum = """
        SELECT md5(string_agg("Id"::text || '|' || "TenantId"::text || '|' || "EmployeeId"::text || '|' || "AttendanceDate"::text || '|' || "Status"::text || '|' || coalesce("Notes", ''), ';' ORDER BY "Id"))
        FROM "DailyAttendanceRecords"
        """;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_marks_leave_made_records_in_each_agency_and_leaves_every_other_record_manual_as_a_non_superuser_owner()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync();

        var employeeA = await EmployeeAsync(admin, TenantA);
        var employeeB = await EmployeeAsync(admin, TenantB);

        var byLeaveA = await RecordAsync(admin, TenantA, employeeA, "2026-03-02", OnLeave, "إجازة معتمدة: سنوية");
        var byLeaveA2 = await RecordAsync(admin, TenantA, employeeA, "2026-03-03", OnLeave, "إجازة معتمدة: مرضية");
        var byHandNoNote = await RecordAsync(admin, TenantA, employeeA, "2026-03-04", OnLeave, null);
        var byHandOtherNote = await RecordAsync(admin, TenantA, employeeA, "2026-03-05", OnLeave, "في إجازة بحسب إفادة المدير");
        var presentWithTheNote = await RecordAsync(admin, TenantA, employeeA, "2026-03-08", Present, "إجازة معتمدة: قُطعت وحضر");
        var present = await RecordAsync(admin, TenantA, employeeA, "2026-03-09", Present, null);
        var byLeaveB = await RecordAsync(admin, TenantB, employeeB, "2026-03-02", OnLeave, "إجازة معتمدة: بلا أجر");
        var byHandB = await RecordAsync(admin, TenantB, employeeB, "2026-03-03", OnLeave, null);
        var before = await Pol047Pg.ScalarAsync<string>(admin, RecordsChecksum);

        // The control: the owner is neither superuser nor exempt, and sees no attendance record under FORCE. Without lifting FORCE the
        // UPDATE of the migration would have read nothing, changed nothing and reported success.
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "DailyAttendanceRecords" """));
        Assert.Equal(8L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "DailyAttendanceRecords" """));

        await using (var migrator = Db(owner, TenantA))
            await migrator.GetService<IMigrator>().MigrateAsync(Lva057);

        // Leave-made records, in both agencies, each still in its own agency.
        Assert.Equal(Leave, await SourceOfAsync(admin, byLeaveA));
        Assert.Equal(Leave, await SourceOfAsync(admin, byLeaveA2));
        Assert.Equal(Leave, await SourceOfAsync(admin, byLeaveB));
        Assert.Equal(TenantA, await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "TenantId" FROM "DailyAttendanceRecords" WHERE "Id" = '{byLeaveA}'"""));
        Assert.Equal(TenantB, await Pol047Pg.ScalarAsync<Guid>(admin, $"""SELECT "TenantId" FROM "DailyAttendanceRecords" WHERE "Id" = '{byLeaveB}'"""));

        // A «في إجازة» typed by hand stays manual; so does a record that is not «في إجازة», whatever its note.
        Assert.Equal(Manual, await SourceOfAsync(admin, byHandNoNote));
        Assert.Equal(Manual, await SourceOfAsync(admin, byHandOtherNote));
        Assert.Equal(Manual, await SourceOfAsync(admin, byHandB));
        Assert.Equal(Manual, await SourceOfAsync(admin, presentWithTheNote));
        Assert.Equal(Manual, await SourceOfAsync(admin, present));

        // Nothing was deleted and nothing else changed: the same records, employees, days, statuses and notes; no reference, no
        // replaced status.
        Assert.Equal(8L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "DailyAttendanceRecords" """));
        Assert.Equal(before, await Pol047Pg.ScalarAsync<string>(admin, RecordsChecksum));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM "DailyAttendanceRecords" WHERE "SourceReference" IS NOT NULL OR "ReplacedStatus" IS NOT NULL"""));

        // FORCE is back on the attendance records: the migrator sees none again, and an agency reads its own.
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin,
            """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'DailyAttendanceRecords'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "DailyAttendanceRecords" """));
        Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "DailyAttendanceRecords" WHERE "Source" = 2""", TenantA));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "DailyAttendanceRecords" WHERE "Source" = 2""", TenantB));

        // The four new tables: forced row security with the tenant policy (USING and WITH CHECK).
        foreach (var table in LVA057_AttendancePeriods.NewTenantTables)
        {
            Assert.True(await Pol047Pg.ScalarAsync<bool>(admin,
                $"""SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"""), table);
            Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin,
                $"""SELECT count(*) FROM pg_policies WHERE tablename = '{table}' AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL"""));
        }

        // The indexes the rules rest on: one summary per period and employee, one pending correction per employee and day, one
        // period per agency and start.
        Assert.Equal(3L, await Pol047Pg.ScalarAsync<long>(admin, """
            SELECT count(*) FROM pg_indexes WHERE indexdef LIKE 'CREATE UNIQUE INDEX%' AND indexname IN
              ('IX_AttendanceEmployeeSummaries_AttendancePeriodId_EmployeeId', 'IX_AttendanceCorrections_OnePendingPerDay', 'IX_AttendancePeriods_TenantId_PeriodStart')
            """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_agency_cannot_write_a_period_a_summary_or_a_correction_of_another_agency()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync();
        var employeeA = await EmployeeAsync(admin, TenantA);
        await using (var migrator = Db(owner, TenantA))
            await migrator.GetService<IMigrator>().MigrateAsync(Lva057);

        // Agency A writes its period and a summary as the non-superuser role, inside its own session tenant.
        var period = Guid.NewGuid();
        string SummarySql(Guid id) => $"""
            INSERT INTO "AttendanceEmployeeSummaries"
                ("Id","TenantId","CreatedAt","UpdatedAt","AttendancePeriodId","EmployeeId","Version","WorkingDays","PresentDays","AbsentDays","OnLeaveDays","RemoteDays","UnrecordedWorkingDays","LastReason","PublishedAt")
            VALUES ('{id}','{TenantA}',now(),now(),'{period}','{employeeA}',1,22,22,0,0,0,0,0,now());
            """;
        await Pol047Pg.ExecAsync(owner, $"""
            INSERT INTO "AttendancePeriods" ("Id","TenantId","CreatedAt","UpdatedAt","PeriodStart","PeriodEnd","Status")
            VALUES ('{period}','{TenantA}',now(),now(),'2026-03-01','2026-03-31',1);
            """, TenantA);
        await Pol047Pg.ExecAsync(owner, SummarySql(Guid.NewGuid()), TenantA);

        // Agency B sees neither, and a row written with agency A's tenant from agency B's session is refused by WITH CHECK.
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "AttendancePeriods" """, TenantB));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "AttendanceEmployeeSummaries" """, TenantB));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "AttendanceEmployeeSummaries" """, TenantA));
        var refused = await Assert.ThrowsAnyAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, $"""
            INSERT INTO "AttendanceCorrections" ("Id","TenantId","CreatedAt","UpdatedAt","AttendancePeriodId","EmployeeId","AttendanceDate","NewStatus","Reason","Source","Status","RequestedAt")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{period}','{employeeA}','2026-03-02',1,'محاولة كتابة من جهة أخرى',0,0,now());
            """, TenantB));
        Assert.Equal("42501", refused.SqlState);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "AttendanceCorrections" """));

        // The same employee and period cannot hold a second summary (the index two concurrent closes meet on).
        var duplicate = await Assert.ThrowsAnyAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, SummarySql(Guid.NewGuid()), TenantA));
        Assert.Equal("23505", duplicate.SqlState);

        // One request waits per employee and day; a decided one does not stand in the way of the next.
        string CorrectionSql(int status) => $"""
            INSERT INTO "AttendanceCorrections" ("Id","TenantId","CreatedAt","UpdatedAt","AttendancePeriodId","EmployeeId","AttendanceDate","NewStatus","Reason","Source","Status","RequestedAt")
            VALUES (gen_random_uuid(),'{TenantA}',now(),now(),'{period}','{employeeA}','2026-03-02',1,'تصحيح يوم الثاني من آذار',0,{status},now());
            """;
        await Pol047Pg.ExecAsync(owner, CorrectionSql(status: 2), TenantA);
        await Pol047Pg.ExecAsync(owner, CorrectionSql(status: 0), TenantA);
        var secondPending = await Assert.ThrowsAnyAsync<PostgresException>(() => Pol047Pg.ExecAsync(owner, CorrectionSql(status: 0), TenantA));
        Assert.Equal("23505", secondPending.SqlState);
    }
}
