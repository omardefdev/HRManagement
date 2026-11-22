using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-53 (P-12 and the plan's integration cases) on PostgreSQL. This part proves what migration <c>PAY053_SalaryChangeRequests</c>
/// gives the database, as a role that is neither superuser nor owner (Testcontainers' own connection bypasses row security and would
/// prove nothing): agency B sees and writes no request of agency A; one open request per employee; a grade a pay profile names and a
/// scale that has grades are not deleted.
/// </summary>
[Collection("PostgreSql")]
public sealed partial class Iam053PayProfileChangePostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly Guid AgencyA = Guid.Parse("53000000-0000-4000-8000-00000000000a");
    private static readonly Guid AgencyB = Guid.Parse("53000000-0000-4000-8000-00000000000b");

    private static PayrollDbContext Context(string connection, Guid tenant)
    {
        var context = new PayrollPostgresTestSupport.FixedTenant(tenant);
        return new PayrollDbContext(
            new DbContextOptionsBuilder<PayrollDbContext>()
                .UseNpgsql(connection, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
                .AddInterceptors(new TenantIdStampInterceptor(context), new NpgsqlTenantSessionInterceptor(context))
                .Options,
            context);
    }

    /// <summary>A migrated database, and the connection of the non-superuser application role on it.</summary>
    private async Task<(string Admin, string App)> MigratedAsync()
    {
        var name = TestDatabaseNames.Unique("iam053_");
        var admin = await fixture.CreateDatabaseAsync(name);
        await using (var db = Context(admin, AgencyA))
            await db.Database.MigrateAsync();
        return (admin, await fixture.CreateAppConnectionAsync(name));
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql, Guid? tenant = null)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        if (tenant is not null)
        {
            await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", c);
            set.Parameters.AddWithValue("t", tenant.Value.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var command = new NpgsqlCommand(sql, c);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static PayProfileChangeRequest Request(Guid employee, PayProfileChangeState state = PayProfileChangeState.PendingApproval) => new()
    {
        EmployeeId = employee,
        ChangeKind = PayProfileChangeKind.BaseSalary,
        Source = PayProfileChangeSource.Manual,
        ProposedBaseSalary = 5000m,
        EffectiveFrom = new DateOnly(2026, 12, 1),
        Reason = "ترفيع استثنائي",
        LegalReference = "القرار 12 لعام 2026",
        State = state,
        RequestedBy = Guid.NewGuid(),
        RequestedByName = "معالج الرواتب",
        RequestedAt = DateTime.UtcNow,
    };

    private static PayrollReferenceChangeRequest ReferenceRequest(Guid? entity) => new()
    {
        EntityType = PayrollReferenceEntityType.PayGrade,
        EntityId = entity,
        Operation = entity is null ? PayrollReferenceOperation.Create : PayrollReferenceOperation.Update,
        ProposedJson = "{}",
        Reason = "تعديل الحد الأعلى",
        LegalReference = "المرسوم 5 لعام 2026",
        RequestedBy = Guid.NewGuid(),
        RequestedAt = DateTime.UtcNow,
    };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Row_security_as_a_non_superuser_agency_B_sees_and_writes_no_request_of_agency_A()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (admin, app) = await MigratedAsync();

        var employee = Guid.NewGuid();
        var grade = Guid.NewGuid();
        await using (var a = Context(app, AgencyA))
        {
            a.PayProfileChangeRequests.Add(Request(employee));
            a.PayrollReferenceChangeRequests.Add(ReferenceRequest(grade));
            await a.SaveChangesAsync();
        }

        // The tables carry forced row security and the tenant policy.
        foreach (var table in new[] { "PayProfileChangeRequests", "PayrollReferenceChangeRequests" })
        {
            Assert.True(await ScalarAsync<bool>(admin, $"""SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = '"{table}"'::regclass"""));
            Assert.Equal(1L, await ScalarAsync<long>(admin,
                $"SELECT count(*) FROM pg_policies WHERE tablename = '{table}' AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL"));
        }

        // The application role is not a superuser, does not bypass row security and owns neither table.
        Assert.False(await ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await ScalarAsync<long>(app,
            "SELECT count(*) FROM pg_tables WHERE tablename IN ('PayProfileChangeRequests', 'PayrollReferenceChangeRequests') AND tableowner = current_user"));

        // Agency A sees its rows; agency B and a session with no tenant see none, in SQL and through EF with the query filter off.
        const string countRequests = """SELECT count(*) FROM "PayProfileChangeRequests" """;
        const string countReference = """SELECT count(*) FROM "PayrollReferenceChangeRequests" """;
        Assert.Equal(1L, await ScalarAsync<long>(app, countRequests, AgencyA));
        Assert.Equal(1L, await ScalarAsync<long>(app, countReference, AgencyA));
        Assert.Equal(0L, await ScalarAsync<long>(app, countRequests, AgencyB));
        Assert.Equal(0L, await ScalarAsync<long>(app, countReference, AgencyB));
        Assert.Equal(0L, await ScalarAsync<long>(app, countRequests));
        Assert.Equal(0L, await ScalarAsync<long>(app, countReference));
        await using (var b = Context(app, AgencyB))
        {
            Assert.Equal(0, await b.PayProfileChangeRequests.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await b.PayrollReferenceChangeRequests.IgnoreQueryFilters().CountAsync());
        }

        // Agency B cannot write a row of agency A (WITH CHECK), nor change or delete one (USING hides it).
        var forged = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync<object>(app,
            $"""
             INSERT INTO "PayProfileChangeRequests" ("Id", "TenantId", "EmployeeId", "ChangeKind", "Source", "EffectiveFrom", "Reason", "State", "RequestedBy", "RequestedAt", "CreatedAt", "UpdatedAt")
             VALUES (gen_random_uuid(), '{AgencyA}', gen_random_uuid(), 0, 0, DATE '2026-12-01', 'x', 3, gen_random_uuid(), now(), now(), now()) RETURNING "Id"
             """, AgencyB));
        Assert.Equal("42501", forged.SqlState);
        Assert.Equal(0L, await ScalarAsync<long>(app,
            """WITH changed AS (UPDATE "PayProfileChangeRequests" SET "State" = 4 RETURNING 1) SELECT count(*) FROM changed""", AgencyB));
        Assert.Equal(0L, await ScalarAsync<long>(app,
            """WITH gone AS (DELETE FROM "PayrollReferenceChangeRequests" RETURNING 1) SELECT count(*) FROM gone""", AgencyB));
        Assert.Equal(1L, await ScalarAsync<long>(app, countRequests + """ WHERE "State" = 0""", AgencyA));
        Assert.Equal(1L, await ScalarAsync<long>(app, countReference, AgencyA));

        // The same employee id in agency B is another employee: the one-open index is per agency.
        await using (var b = Context(app, AgencyB))
        {
            b.PayProfileChangeRequests.Add(Request(employee));
            await b.SaveChangesAsync();
        }
        Assert.Equal(1L, await ScalarAsync<long>(app, countRequests, AgencyB));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_database_keeps_one_open_request_per_employee_and_per_reference_record()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (_, app) = await MigratedAsync();
        var employee = Guid.NewGuid();
        var grade = Guid.NewGuid();

        await using (var db = Context(app, AgencyA))
        {
            db.PayProfileChangeRequests.Add(Request(employee, PayProfileChangeState.Scheduled));
            db.PayrollReferenceChangeRequests.Add(ReferenceRequest(grade));
            // Two creations have no record yet: both may be open.
            db.PayrollReferenceChangeRequests.Add(ReferenceRequest(null));
            db.PayrollReferenceChangeRequests.Add(ReferenceRequest(null));
            await db.SaveChangesAsync();
        }

        await using (var db = Context(app, AgencyA))
        {
            db.PayProfileChangeRequests.Add(Request(employee));
            var second = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var pg = Assert.IsType<PostgresException>(second.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
            Assert.Equal(PayProfileChangeRequestIndexes.OneOpen, pg.ConstraintName);
        }

        await using (var db = Context(app, AgencyA))
        {
            db.PayrollReferenceChangeRequests.Add(ReferenceRequest(grade));
            var second = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PayProfileChangeRequestIndexes.OneOpenReferenceChange, Assert.IsType<PostgresException>(second.InnerException).ConstraintName);
        }

        // A closed request does not count: once the open one is cancelled, a new one is accepted.
        await using (var db = Context(app, AgencyA))
        {
            var open = await db.PayProfileChangeRequests.SingleAsync(r => r.EmployeeId == employee);
            open.State = PayProfileChangeState.Cancelled;
            db.PayProfileChangeRequests.Add(Request(employee));
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.PayProfileChangeRequests.CountAsync(r => r.EmployeeId == employee));
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_grade_a_pay_profile_names_and_a_scale_that_has_grades_are_not_deleted_by_the_database()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        var (_, app) = await MigratedAsync();

        Guid gradeId, scaleId, freeGradeId, profileId;
        await using (var db = Context(app, AgencyA))
        {
            var scale = new SalaryScale { Code = "S-53", NameAr = "سلم 2026", EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var grade = new PayGrade { SalaryScale = scale, GradeCode = "G1", GradeNameAr = "الأولى", BaseSalary = 4000, MinSalary = 3000, MaxSalary = 6000 };
            var free = new PayGrade { SalaryScale = scale, GradeCode = "G2", GradeNameAr = "الثانية", BaseSalary = 5000, MinSalary = 4000, MaxSalary = 7000 };
            var profile = TestDataFactory.CreateActivePayProfile(Guid.NewGuid(), 4500m);
            profile.PayGrade = grade;
            db.AddRange(scale, grade, free, profile);
            await db.SaveChangesAsync();
            (gradeId, scaleId, freeGradeId, profileId) = (grade.Id, scale.Id, free.Id, profile.Id);
        }

        await using (var db = Context(app, AgencyA))
        {
            db.PayGrades.Remove(await db.PayGrades.SingleAsync(g => g.Id == gradeId));
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(refused.InnerException).SqlState);
        }

        await using (var db = Context(app, AgencyA))
        {
            // Loaded without its grades, so EF sends the DELETE and the database answers (it used to cascade to the grades).
            db.SalaryScales.Remove(await db.SalaryScales.SingleAsync(s => s.Id == scaleId));
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(refused.InnerException).SqlState);
        }

        await using (var db = Context(app, AgencyA))
        {
            // The profile still names its grade (the old relation emptied it), and a grade nobody names can go.
            Assert.Equal(gradeId, (await db.EmployeePayProfiles.SingleAsync(p => p.Id == profileId)).PayGradeId);
            db.PayGrades.Remove(await db.PayGrades.SingleAsync(g => g.Id == freeGradeId));
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.PayGrades.CountAsync());
        }
    }
}
