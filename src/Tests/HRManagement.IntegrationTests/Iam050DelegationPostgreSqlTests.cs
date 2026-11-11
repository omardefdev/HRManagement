using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Delegation;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-50 (P-5, P-8) on PostgreSQL. Testcontainers connects as a superuser, which row security never touches, so each proof here runs as
/// the server's kind of role: the migration <c>HCM050_ApprovalDelegationScope</c> as a non-superuser owner with FORCE on, over old rows
/// of two tenants (D-50-4); and the lookup and the internal endpoint as a role that neither owns the tables nor bypasses row security
/// (production's <c>hr_core_app</c>), which read the caller's tenant only, and nothing when no tenant is named.
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam050DelegationPostgreSqlTests(PostgreSqlFixture fixture)
{
    private const string PreviousMigration = "20261110102522_POL049_PolicyDomainRetirement";
    private const string Approver = "hr-management-approver";
    private static readonly Guid TenantA = Guid.Parse("a0000050-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000050-0000-4000-8000-0000000000b1");

    // ---------------------------------------------------------------- the migration, as the server runs it

    /// <summary>The schema of the previous migration, owned by a NOSUPERUSER NOBYPASSRLS role, with ApprovalDelegations forced.</summary>
    private async Task<(string Admin, string Owner)> PreviousSchemaOwnedByAMigratorAsync(string name)
    {
        var admin = await fixture.CreateDatabaseAsync($"iam050_{name}_{Guid.NewGuid():N}"[..40]);
        await using (var db = Pol047Pg.AdminContext(admin))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var role = $"iam050_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'iam050_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            GRANT CONNECT ON DATABASE "{database}" TO {role};
            GRANT USAGE, CREATE ON SCHEMA public TO {role};
            DO $do$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {role}', t.tablename);
                END LOOP;
            END
            $do$;
            ALTER TABLE "ApprovalDelegations" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "ApprovalDelegations" FORCE ROW LEVEL SECURITY;
            """);
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "iam050_pwd" }.ConnectionString);
    }

    /// <summary>A row of the old schema: DelegateId required, no employee, no names.</summary>
    private static async Task<Guid> OldRowAsync(string admin, Guid tenant, string? role, bool revoked = false)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO "ApprovalDelegations"
                ("Id", "TenantId", "CreatedAt", "UpdatedAt", "PrincipalId", "DelegateId", "EntityTypeScope", "RoleScope",
                 "ActiveFrom", "ActiveUntil", "Reason", "IsRevoked")
            VALUES (@id, @tenant, now(), now(), @principal, @delegate, NULL, @role, now() - interval '1 day', now() + interval '5 days',
                    'old', @revoked);
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("principal", Guid.NewGuid());
        cmd.Parameters.AddWithValue("delegate", Guid.NewGuid());
        cmd.Parameters.AddWithValue("role", (object?)role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("revoked", revoked);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static Task<string> StateAsync(string admin, Guid id) =>
        Pol047Pg.ScalarAsync<string>(admin, $"""
            SELECT CASE WHEN "IsRevoked" THEN 'revoked:' || COALESCE("RevokedReason", '-') ELSE 'active' END
            FROM "ApprovalDelegations" WHERE "Id" = '{id}'
            """);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_revokes_the_old_rows_in_two_tenants_as_a_non_superuser_owner_and_leaves_row_security_forced()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync("migrate");
        var noRoleA = await OldRowAsync(admin, TenantA, null);
        var otherRoleA = await OldRowAsync(admin, TenantA, "hr-core");
        var delegableA = await OldRowAsync(admin, TenantA, Approver);
        var alreadyRevokedA = await OldRowAsync(admin, TenantA, null, revoked: true);
        var blankB = await OldRowAsync(admin, TenantB, "  ");
        var delegableB = await OldRowAsync(admin, TenantB, " HR-Management-Approver ");
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "WorkflowDelegations" ("Id", "TenantId", "CreatedAt", "UpdatedAt", "PrincipalId", "DelegateId", "ActiveFrom", "IsRevoked")
            VALUES ('{Guid.NewGuid()}', '{TenantA}', now(), now(), '{Guid.NewGuid()}', '{Guid.NewGuid()}', now(), false);
            """);

        // The control: this role sees no row, which is why a data change in a migration finds nothing unless FORCE is lifted (PLAN-43).
        Assert.Equal(6L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "ApprovalDelegations" """));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "ApprovalDelegations" """));
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));

        await using (var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options))
            await migrator.Database.MigrateAsync();

        Assert.Equal("revoked:PLAN-50 migration", await StateAsync(admin, noRoleA));
        Assert.Equal("revoked:PLAN-50 migration", await StateAsync(admin, otherRoleA));
        Assert.Equal("active", await StateAsync(admin, delegableA));
        Assert.Equal("revoked:-", await StateAsync(admin, alreadyRevokedA));
        Assert.Equal("revoked:PLAN-50 migration", await StateAsync(admin, blankB));
        Assert.Equal("active", await StateAsync(admin, delegableB));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'ApprovalDelegations'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM pg_tables WHERE tablename = 'WorkflowDelegations'"""));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%HCM050_ApprovalDelegationScope'"""));
        Assert.Equal("YES", await Pol047Pg.ScalarAsync<string>(admin,
            """SELECT is_nullable FROM information_schema.columns WHERE table_name = 'ApprovalDelegations' AND column_name = 'DelegateId'"""));

        // The checks: no row in force without a role, no row without a delegate.
        var noRole = await Assert.ThrowsAsync<PostgresException>(() => OldRowAsync(admin, TenantA, null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, noRole.SqlState);
        var noDelegate = await Assert.ThrowsAsync<PostgresException>(() => Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "ApprovalDelegations" ("Id", "TenantId", "CreatedAt", "UpdatedAt", "PrincipalId", "RoleScope", "ActiveFrom", "ActiveUntil", "Reason", "IsRevoked")
            VALUES ('{Guid.NewGuid()}', '{TenantA}', now(), now(), '{Guid.NewGuid()}', '{Approver}', now(), now() + interval '1 day', 'x', false);
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, noDelegate.SqlState);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_role_that_does_not_own_the_table_fails_the_migration_instead_of_finding_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync("notowner");
        var row = await OldRowAsync(admin, TenantA, null);
        await Pol047Pg.ExecAsync(admin, """ALTER TABLE "ApprovalDelegations" OWNER TO postgres""");

        await using var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options);
        await Assert.ThrowsAsync<PostgresException>(() => migrator.Database.MigrateAsync());

        // The whole migration rolled back: the row is still in force and the new columns do not exist.
        Assert.False(await Pol047Pg.ScalarAsync<bool>(admin, $"""SELECT "IsRevoked" FROM "ApprovalDelegations" WHERE "Id" = '{row}'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'ApprovalDelegations' AND column_name = 'RevokedReason'"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relforcerowsecurity FROM pg_class WHERE relname = 'ApprovalDelegations'"""));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM pg_tables WHERE tablename = 'WorkflowDelegations'"""));
    }

    // ---------------------------------------------------------------- the lookups, as production's app role

    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private static async Task SeedAsync(string admin, Guid tenant, Guid delegateEmployee, string role, bool revoked = false, int days = 5)
    {
        await using var db = Pol047Pg.AdminContext(admin);
        db.ApprovalDelegations.Add(new HRManagement.SharedKernel.SeparationOfDuties.ApprovalDelegation
        {
            TenantId = tenant,
            PrincipalId = Guid.NewGuid(),
            PrincipalName = $"principal-{tenant:N}"[..20],
            DelegateEmployeeId = delegateEmployee,
            RoleScope = role,
            ActiveFrom = DateTime.UtcNow.AddDays(-1),
            ActiveUntil = DateTime.UtcNow.AddDays(days),
            Reason = "iam050",
            IsRevoked = revoked
        });
        await db.SaveChangesAsync();
    }

    private static IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation> Body(ActionResult<ApprovalDelegationsResponse> result) =>
        Assert.IsType<ApprovalDelegationsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).Delegations;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_lookup_and_the_endpoint_read_the_callers_tenant_only_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "iam050lookup");
        var app = await Pol047Pg.ForcedAppAsync(fixture, admin);
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(app, """SELECT count(*) FROM pg_tables WHERE tablename = 'ApprovalDelegations' AND tableowner = current_user"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'ApprovalDelegations'"""));

        var employee = Guid.NewGuid();
        await SeedAsync(admin, TenantA, employee, Approver);
        await SeedAsync(admin, TenantA, employee, Approver, revoked: true);
        await SeedAsync(admin, TenantA, employee, "hr-core"); // not delegable: no effect
        await SeedAsync(admin, TenantB, employee, Approver);

        await using var provider = Pol047Pg.AppProvider(app);

        async Task<(IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation> Lookup, IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation> Endpoint)> AskAsync(Guid? tenant)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenant);
            try
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                var lookup = new DbApprovalDelegationLookup(db, Configuration);
                var direct = await lookup.GetActiveForAsync(null, employee);
                var endpoint = new ApprovalDelegationsInternalController(lookup, scope.ServiceProvider.GetRequiredService<ITenantContext>())
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
                };
                var answered = Body(await endpoint.Active(null, employee, CancellationToken.None));
                return (direct, answered);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        var inA = await AskAsync(TenantA);
        var a = Assert.Single(inA.Lookup);
        Assert.Equal(Approver, a.Role);
        Assert.Equal(a.Id, Assert.Single(inA.Endpoint).Id);

        var inB = await AskAsync(TenantB);
        var b = Assert.Single(inB.Lookup);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(b.Id, Assert.Single(inB.Endpoint).Id);

        var none = await AskAsync(null);
        Assert.Empty(none.Endpoint);
        Assert.Empty(none.Lookup);
    }
}
