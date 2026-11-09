using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-48 (§3, D-TEN-5, D-TEN-6, D-TEN-7) on PostgreSQL, as a non-owner role under forced row security in an agency's session: the
/// real FundedPositionCodeService with the seeded national policies, many positions of one unit and grade over two budget years at
/// once (one counter, distinct codes, one shared cost centre issued once); and requisitions of two budget years at once (one
/// counter per budget year, the year in the number).
/// </summary>
[Collection("PostgreSql")]
public sealed class Ten048FundedPositionRealCodeServiceTests(PostgreSqlFixture fixture)
{
    private static readonly Guid TenantA = Guid.Parse("a0000001-0000-4000-8000-000000000001");

    private static async Task<(string Admin, string App, Guid Unit)> PrepareAsync(PostgreSqlFixture postgres, string name)
    {
        var admin = await Pol047Pg.MigratedAsync(postgres, "t48fp" + name);
        var unit = new OrgUnit { TenantId = TenantA, Code = "01.02", NameAr = "وحدة", NameEn = "Unit" };
        await using (var db = Pol047Pg.AdminContext(admin))
        {
            await CodeGenerationSeeder.SeedAsync(db);
            db.Tenants.Add(new Tenant { Id = TenantA, Code = "AGENCY-A", NameAr = "أ", NameEn = "A", IsActive = true });
            db.OrgUnits.Add(unit);
            await db.SaveChangesAsync();
        }

        foreach (var file in new[] { "ten002c-harden-tenant-rls.sql", "ten048-national-shared-read.sql" })
        {
            var script = await global::System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", file));
            await Pol047Pg.ExecAsync(admin, script.Replace(@"\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal));
        }

        // A small pool: the parallel requests queue for 8 sessions, so the shared container keeps connection slots for the
        // other tests of the collection (and the pool is cleared at the end of each test).
        var app = new NpgsqlConnectionStringBuilder(await postgres.CreateAppConnectionAsync(new NpgsqlConnectionStringBuilder(admin).Database!))
        {
            MaxPoolSize = 8
        }.ConnectionString;
        return (admin, app, unit.Id);
    }

    private static void ReleasePools(params string[] connectionStrings)
    {
        foreach (var cs in connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Positions_of_one_unit_over_two_budget_years_at_once_get_distinct_codes_and_one_cost_centre()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, unitId) = await PrepareAsync(fixture, "par");
        await using var provider = Pol047Pg.AppProvider(app);
        var cache = new MemoryCache(new MemoryCacheOptions());

        const int Count = 24;
        var created = await Task.WhenAll(Enumerable.Range(0, Count).Select(i => Pol047Pg.AsTenantAsync(provider, TenantA, async db =>
        {
            var service = new FundedPositionCodeService(db, new CodeGeneratorService(db, cache, NullLogger<CodeGeneratorService>.Instance));
            // As EstablishmentChangeApplier builds them: no tenant on the draft, the session's tenant numbers it.
            var position = await service.CreateAsync(new FundedPosition { OrgUnitId = unitId, GradeCode = "G5", BudgetYear = i % 2 == 0 ? 2026 : 2027 });
            return (position.PositionCode, position.CostCenter, position.TenantId);
        })));

        Assert.Equal(Enumerable.Range(1, Count).Select(n => $"01.02-G5-{n:D3}"), created.Select(c => c.PositionCode).OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(created, c => Assert.Equal("01.02", c.CostCenter));
        Assert.All(created, c => Assert.Equal(TenantA, c.TenantId));

        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "CodeIssuances" WHERE "EntityKind" = 'CostCenter'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, """SELECT count(*) FROM "CodeSequenceCounters" WHERE "EntityKind" = 'CostCenter'"""));
        Assert.Equal((long)Count, await Pol047Pg.ScalarAsync<long>(admin,
            $"""SELECT "LastValue" FROM "CodeSequenceCounters" WHERE "TenantId" = '{TenantA}' AND "EntityKind" = 'FundedPosition' AND "ScopeKey" = '{TenantA:N}|FundedPosition|01.02|G5' AND "Year" = 0"""));
        Assert.Equal((long)Count, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "FundedPositions" WHERE "TenantId" = '{TenantA}'"""));
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "CodeIssuances" WHERE "TenantId" <> '{TenantA}'"""));
        ReleasePools(app, admin);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Requisitions_of_two_budget_years_at_once_count_apart_and_show_the_budget_year()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, _) = await PrepareAsync(fixture, "req");
        await using var provider = Pol047Pg.AppProvider(app);
        var cache = new MemoryCache(new MemoryCacheOptions());

        const int PerYear = 30;
        var codes = await Task.WhenAll(Enumerable.Range(0, PerYear * 2).Select(i => Pol047Pg.AsTenantAsync(provider, TenantA, async db =>
        {
            var budgetYear = i % 2 == 0 ? 2026 : 2027;
            var request = HRManagement.ServiceDefaults.CodeGeneration.CodeGenerationRequests.RecruitmentRequisition(budgetYear);
            return (await new CodeGeneratorService(db, cache, NullLogger<CodeGeneratorService>.Instance).GenerateAsync(request)).Code;
        })));

        var expected = Enumerable.Range(1, PerYear).Select(n => $"REQ-2026-{n:D5}")
            .Concat(Enumerable.Range(1, PerYear).Select(n => $"REQ-2027-{n:D5}"))
            .OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(expected, codes.OrderBy(c => c, StringComparer.Ordinal));
        foreach (var year in new[] { 2026, 2027 })
        {
            Assert.Equal((long)PerYear, await Pol047Pg.ScalarAsync<long>(admin,
                $"""SELECT "LastValue" FROM "CodeSequenceCounters" WHERE "ScopeKey" = '{TenantA:N}|RecruitmentRequisition|{year}' AND "Year" = 0"""));
        }

        ReleasePools(app, admin);
    }
}
