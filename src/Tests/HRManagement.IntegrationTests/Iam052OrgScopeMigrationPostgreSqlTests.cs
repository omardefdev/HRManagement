using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Migrations;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-52 (P-6): <c>HCM052_OrgScopeKeys</c> as the server runs it — on the previous schema owned by a NOSUPERUSER NOBYPASSRLS role (as
/// production's <c>*_migrator</c>), with forced row security on every table it touches, in two agencies. Each workflow instance's backfilled
/// unit equals <see cref="WorkflowInstanceScopeResolver"/>'s; a Diwan record takes its related employee's unit; a Diwan record without one,
/// and every old field inspection, stay empty (central); FORCE is back on every table; <c>Down</c> works. The counts of records with no key
/// before and after are written to the test output for the report (<c>scripts/iam052-validate.ps1 -Counts</c> reads the same on a server).
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam052OrgScopeMigrationPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private const string PreviousMigration = "20261111105041_HCM050_ApprovalDelegationScope";
    private const string Hcm052 = "20261112212437_HCM052_OrgScopeKeys";
    private static readonly Guid TenantA = Guid.Parse("a0000052-0000-4000-8000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("b0000052-0000-4000-8000-0000000000b1");

    private static HRCoreDbContext Db(string connection, Guid tenant)
    {
        var context = new Msg040FixedTenant(tenant);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(connection).AddInterceptors(new TenantIdStampInterceptor(context)).Options,
            context);
    }

    private async Task<(string Admin, string Owner)> PreviousSchemaOwnedByAMigratorAsync()
    {
        var admin = await fixture.CreateDatabaseAsync($"iam052_mig_{Guid.NewGuid():N}"[..40]);
        // The national workflow pack is the default tenant's rows.
        await using (var db = Db(admin, HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await WorkflowPackSeeder.SeedAsync(db);
        }

        var role = $"iam052_owner_{Guid.NewGuid():N}"[..24];
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        var forced = string.Join("\n", HCM052_OrgScopeKeys.TablesTouched.Select(t =>
            $"""ALTER TABLE "{t}" ENABLE ROW LEVEL SECURITY; ALTER TABLE "{t}" FORCE ROW LEVEL SECURITY;"""));
        await Pol047Pg.ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD 'iam052_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
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
            {forced}
            """);
        return (admin, new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "iam052_pwd" }.ConnectionString);
    }

    private sealed record Seeded(
        Guid Unit,
        Guid Employee,
        Guid EmployeeWithoutUnit,
        Guid RequisitionInstance,
        Guid OfferInstance,
        Guid SessionInstance,
        Guid AppealInstance,
        Guid ChangeInstance,
        Guid ExternalWithout,
        Guid ExternalWith,
        Guid ExternalUnit,
        Guid DiwanWithEmployee,
        Guid DiwanWithoutEmployee,
        Guid DiwanEmployeeWithoutUnit,
        Guid Inspection);

    private static WorkflowInstance Instance(Guid tenant, WorkflowDefinition definition, string type, Guid entityId, Guid? scope = null) => new()
    {
        TenantId = tenant,
        DefinitionId = definition.Id,
        DefinitionVersion = definition.Version,
        EntityType = type,
        EntityId = entityId,
        Status = WorkflowStatus.InProgress,
        StepHistoryJson = "[]",
        StartedAt = DateTime.UtcNow.AddDays(-1),
        ScopeOrgUnitId = scope
    };

    /// <summary>One agency's rows of the old schema: every internal type with a key, one without, two external, Diwan and an inspection.</summary>
    private static async Task<Seeded> SeedAsync(string admin, Guid tenant)
    {
        await using var db = Db(admin, tenant);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking().FirstAsync(d => d.Code == "RECRUITMENT-REQUISITION-APPROVAL");
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
        var unit = position.OrgUnitId;
        var employee = new Employee { TenantId = tenant, FirstName = "A", LastName = "B", FullName = "A B", NationalId = Guid.NewGuid().ToString("N")[..11], PrimaryOrgUnitId = unit };
        var noUnit = new Employee { TenantId = tenant, FirstName = "C", LastName = "D", FullName = "C D", NationalId = Guid.NewGuid().ToString("N")[..11] };
        db.Employees.AddRange(employee, noUnit);
        var requisition = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, RequisitionStatus.PendingApproval);
        db.RecruitmentRequisitions.Add(requisition);
        var application = new RecruitmentApplication
        {
            TenantId = tenant,
            ApplicantFullName = "applicant",
            AppliedPosition = "position",
            AppliedAt = DateTime.UtcNow,
            RequisitionId = requisition.Id
        };
        db.RecruitmentApplications.Add(application);
        var offer = new OfferLetter
        {
            TenantId = tenant,
            RecruitmentApplicationId = application.Id,
            FundedPositionId = position.Id,
            OfferNumber = $"OF-{Guid.NewGuid():N}"[..20],
            OfferedSalary = 1,
            ProposedStartDate = DateTime.UtcNow,
            ValidityDays = 10
        };
        db.OfferLetters.Add(offer);
        var session = new SelectionCommitteeSession { TenantId = tenant, RequisitionId = requisition.Id, CommitteeDate = DateTime.UtcNow };
        db.SelectionCommitteeSessions.Add(session);
        var appeal = new RecruitmentAppeal { TenantId = tenant, RecruitmentApplicationId = application.Id, Reason = "reason" };
        db.RecruitmentAppeals.Add(appeal);

        var external = Guid.NewGuid();
        var instances = new[]
        {
            Instance(tenant, definition, WorkflowInstanceScopeResolver.RecruitmentRequisition, requisition.Id),
            Instance(tenant, definition, WorkflowInstanceScopeResolver.OfferLetter, offer.Id),
            Instance(tenant, definition, WorkflowInstanceScopeResolver.SelectionCommitteeSession, session.Id),
            Instance(tenant, definition, WorkflowInstanceScopeResolver.RecruitmentAppeal, appeal.Id),
            Instance(tenant, definition, "EstablishmentChangeRequest", Guid.NewGuid()),
            Instance(tenant, definition, "AssetAssignment", Guid.NewGuid()),
            Instance(tenant, definition, "AssetAssignment", Guid.NewGuid(), external),
        };
        db.WorkflowInstances.AddRange(instances);
        await db.SaveChangesAsync();

        // Diwan and inspections through SQL: the model of this build has their new column, the old schema does not.
        var diwanWith = Guid.NewGuid();
        var diwanWithout = Guid.NewGuid();
        var diwanNoUnit = Guid.NewGuid();
        var inspection = Guid.NewGuid();
        await Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "DiwanCorrespondences" ("Id","TenantId","CreatedAt","UpdatedAt","ReferenceNumber","Subject","Direction","RegisteredAt","RelatedEmployeeId")
            VALUES ('{diwanWith}','{tenant}',now(),now(),'R1','s',0,now(),'{employee.Id}'),
                   ('{diwanWithout}','{tenant}',now(),now(),'R2','s',0,now(),NULL),
                   ('{diwanNoUnit}','{tenant}',now(),now(),'R3','s',0,now(),'{noUnit.Id}');
            INSERT INTO "FieldInspectionVisits" ("Id","TenantId","CreatedAt","UpdatedAt","Title","VisitDate","LeadInspectorId","Status")
            VALUES ('{inspection}','{tenant}',now(),now(),'t',current_date,'{employee.Id}','planned');
            """);

        return new Seeded(unit, employee.Id, noUnit.Id, instances[0].Id, instances[1].Id, instances[2].Id, instances[3].Id, instances[4].Id,
            instances[5].Id, instances[6].Id, external, diwanWith, diwanWithout, diwanNoUnit, inspection);
    }

    private static Task<long> NoKeyAsync(string admin, string sql) => Pol047Pg.ScalarAsync<long>(admin, sql);

    private async Task WriteCountsAsync(string admin, string moment, bool hasColumns)
    {
        var instances = await NoKeyAsync(admin, """SELECT count(*) FROM "WorkflowInstances" WHERE "ScopeOrgUnitId" IS NULL""");
        var diwan = hasColumns
            ? await NoKeyAsync(admin, """SELECT count(*) FROM "DiwanCorrespondences" WHERE "OrgUnitId" IS NULL""")
            : await NoKeyAsync(admin, """SELECT count(*) FROM "DiwanCorrespondences" """);
        var inspections = hasColumns
            ? await NoKeyAsync(admin, """SELECT count(*) FROM "FieldInspectionVisits" WHERE "OrgUnitId" IS NULL""")
            : await NoKeyAsync(admin, """SELECT count(*) FROM "FieldInspectionVisits" """);
        output.WriteLine($"IAM052-COUNTS {moment}: WorkflowInstances without a unit={instances}; DiwanCorrespondences without a unit={diwan}; "
                         + $"FieldInspectionVisits without a unit={inspections}");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_stamps_Diwan_and_the_instances_as_the_resolver_does_as_a_non_superuser_owner_under_forced_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync();
        var a = await SeedAsync(admin, TenantA);
        var b = await SeedAsync(admin, TenantB);
        await WriteCountsAsync(admin, "before HCM052", hasColumns: false);

        // The control: the owner sees no row under FORCE, which is why a data change needs FORCE lifted inside the migration.
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(owner, """SELECT count(*) FROM "WorkflowInstances" """));
        Assert.False(await Pol047Pg.ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));

        await using (var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options))
            await migrator.Database.MigrateAsync();
        await WriteCountsAsync(admin, "after HCM052", hasColumns: true);

        foreach (var (tenant, seeded) in new[] { (TenantA, a), (TenantB, b) })
        {
            await using var db = Db(admin, tenant);
            var resolver = new WorkflowInstanceScopeResolver(db);
            foreach (var id in new[] { seeded.RequisitionInstance, seeded.OfferInstance, seeded.SessionInstance, seeded.AppealInstance })
            {
                var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == id);
                var expected = await resolver.ResolveAsync(instance.EntityType, instance.EntityId);
                Assert.NotNull(expected);
                Assert.Equal(expected, instance.ScopeOrgUnitId);
                Assert.Equal(seeded.Unit, instance.ScopeOrgUnitId);
            }

            Assert.Null((await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == seeded.ChangeInstance)).ScopeOrgUnitId);
            Assert.Null((await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == seeded.ExternalWithout)).ScopeOrgUnitId);
            Assert.Equal(seeded.ExternalUnit, (await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == seeded.ExternalWith)).ScopeOrgUnitId);

            Assert.Equal(seeded.Unit, (await db.DiwanCorrespondences.AsNoTracking().SingleAsync(d => d.Id == seeded.DiwanWithEmployee)).OrgUnitId);
            Assert.Null((await db.DiwanCorrespondences.AsNoTracking().SingleAsync(d => d.Id == seeded.DiwanWithoutEmployee)).OrgUnitId);
            Assert.Null((await db.DiwanCorrespondences.AsNoTracking().SingleAsync(d => d.Id == seeded.DiwanEmployeeWithoutUnit)).OrgUnitId);
            Assert.Null((await db.FieldInspectionVisits.AsNoTracking().SingleAsync(v => v.Id == seeded.Inspection)).OrgUnitId);
        }

        // FORCE is back on every table the migration touched.
        foreach (var table in HCM052_OrgScopeKeys.TablesTouched)
            Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, $"""SELECT relforcerowsecurity FROM pg_class WHERE oid = '"{table}"'::regclass"""),
                $"{table} lost FORCE ROW LEVEL SECURITY");

        // Down, as the same role: the columns go, the schema is the previous one, and Up runs again.
        await using (var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options))
        {
            await migrator.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
                """SELECT count(*) FROM information_schema.columns WHERE table_name IN ('DiwanCorrespondences','FieldInspectionVisits') AND column_name = 'OrgUnitId'"""));
            await migrator.GetService<IMigrator>().MigrateAsync(Hcm052);
            Assert.Equal(2L, await Pol047Pg.ScalarAsync<long>(admin,
                """SELECT count(*) FROM information_schema.columns WHERE table_name IN ('DiwanCorrespondences','FieldInspectionVisits') AND column_name = 'OrgUnitId'"""));
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_role_that_does_not_own_the_tables_fails_the_migration_and_nothing_changes()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync();
        await SeedAsync(admin, TenantA);
        await Pol047Pg.ExecAsync(admin, """ALTER TABLE "WorkflowInstances" OWNER TO postgres""");

        await using var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options);
        await Assert.ThrowsAsync<PostgresException>(() => migrator.Database.MigrateAsync());
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin,
            """SELECT count(*) FROM information_schema.columns WHERE table_name = 'DiwanCorrespondences' AND column_name = 'OrgUnitId'"""));
        Assert.True(await Pol047Pg.ScalarAsync<bool>(admin, """SELECT relforcerowsecurity FROM pg_class WHERE relname = 'DiwanCorrespondences'"""));
    }
}
