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
/// PLAN-48 (§3, P-5) on PostgreSQL, as a NOSUPERUSER NOBYPASSRLS role under forced row security (TEN-002C and TEN-048 applied by
/// the owner): a database with the old national code policies and codes already issued in two tenants is upgraded once. FundedPosition
/// and RecruitmentRequisition get v2 (v1 archived); every tenant's counters are reseeded from its own codes, walked from the Tenants
/// table with each tenant's session, and never lowered; unparsed codes are reported; the next numbers do not collide; a second run
/// changes nothing.
/// </summary>
[Collection("PostgreSql")]
public sealed class Ten048CodePolicyUpgradeIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly Guid National = NationalTenant.Id;
    private static readonly Guid TenantA = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("a0000002-0000-4000-8000-000000000001");
    private static readonly int Year = DateTime.UtcNow.Year;

    private static string Key(Guid tenant, params string[] parts) => string.Join("|", new[] { tenant.ToString("N") }.Concat(parts));

    /// <summary>The national catalog with the two defective v1 policies of an existing database.</summary>
    private static Dictionary<(CodeEntityKind, string?), Guid> SeedOldNationalPolicies(HRCoreDbContext db)
    {
        var versions = new Dictionary<(CodeEntityKind, string?), Guid>();
        foreach (var definition in CodeGenerationSeeder.NationalPolicies)
        {
            var (segments, reset) = definition.Kind switch
            {
                CodeEntityKind.FundedPosition => (CodePolicyUpgrades.FundedPositionV1Json, CodeSequenceReset.PerScope),
                CodeEntityKind.RecruitmentRequisition => (CodePolicyUpgrades.RecruitmentRequisitionV1Json, CodeSequenceReset.Yearly),
                _ => (definition.SegmentsJson, definition.Reset)
            };
            var template = new CodePolicyTemplate
            {
                TenantId = National,
                Name = definition.Name,
                EntityKind = definition.Kind,
                Scope = CodePolicyScope.National,
                PolicyKey = definition.PolicyKey
            };
            var version = new CodePolicyVersion
            {
                TenantId = National,
                VersionNumber = 1,
                Status = CodePolicyVersionStatus.Published,
                EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                SegmentsJson = segments,
                SequenceReset = reset,
                FallbackPrefix = definition.FallbackPrefix
            };
            template.Versions.Add(version);
            db.CodePolicyTemplates.Add(template);
            versions[(definition.Kind, definition.PolicyKey)] = version.Id;
        }

        return versions;
    }

    private static CodeIssuance Issued(Guid tenant, CodeEntityKind kind, string code, Guid? version, string facts = "{}") =>
        new() { TenantId = tenant, EntityKind = kind, Code = code, PolicyVersionId = version, SequenceScope = "old", FactsJson = facts };

    private static CodeSequenceCounter Counter(Guid tenant, CodeEntityKind kind, string key, int year, long value) =>
        new() { TenantId = tenant, EntityKind = kind, ScopeKey = key, Year = year, LastValue = value };

    private static async Task<(string Admin, string App, Guid Unit)> PrepareAsync(PostgreSqlFixture postgres, string name)
    {
        var admin = await Pol047Pg.MigratedAsync(postgres, "t48up" + name);
        var unit = new OrgUnit { TenantId = TenantA, Code = "01.02", NameAr = "وحدة", NameEn = "Unit" };
        await using (var db = Pol047Pg.AdminContext(admin))
        {
            db.Tenants.AddRange(
                new Tenant { Id = TenantA, Code = "AGENCY-A", NameAr = "أ", NameEn = "A", IsActive = true },
                new Tenant { Id = TenantB, Code = "AGENCY-B", NameAr = "ب", NameEn = "B", IsActive = true });
            var versions = SeedOldNationalPolicies(db);
            db.OrgUnits.Add(unit);

            // Agency A: requisitions issued under the calendar year (one of budget year 2027), a position code issued in two budget
            // years, a demo position and a seeded acceptance requisition (other shapes), AH and VIP on the shared counter.
            db.CodeIssuances.AddRange(
                Issued(TenantA, CodeEntityKind.RecruitmentRequisition, "REQ-2026-00007", versions[(CodeEntityKind.RecruitmentRequisition, null)], """{"BudgetYear":"2026"}"""),
                Issued(TenantA, CodeEntityKind.RecruitmentRequisition, "REQ-2026-00003", versions[(CodeEntityKind.RecruitmentRequisition, null)], """{"BudgetYear":"2027"}"""),
                Issued(TenantA, CodeEntityKind.RecruitmentRequisition, "REQ-2026-A0001", null),
                Issued(TenantA, CodeEntityKind.FundedPosition, "01.02-GR-05-003", versions[(CodeEntityKind.FundedPosition, null)],
                    """{"OrgUnitCode":"01.02","GradeCode":"GR-05","BudgetYear":"2026"}"""),
                Issued(TenantA, CodeEntityKind.AssetTag, $"AH-{Year}-000010", versions[(CodeEntityKind.AssetTag, null)]),
                Issued(TenantA, CodeEntityKind.AssetTag, $"VIP-{Year}-000012", versions[(CodeEntityKind.AssetTag, "ASSET-PRIVILEGED")]),
                Issued(TenantB, CodeEntityKind.RecruitmentRequisition, "REQ-2026-00002", versions[(CodeEntityKind.RecruitmentRequisition, null)], """{"BudgetYear":"2026"}"""),
                Issued(TenantB, CodeEntityKind.AssetTag, $"AH-{Year}-000005", versions[(CodeEntityKind.AssetTag, null)]));
            db.FundedPositions.AddRange(
                new FundedPosition { TenantId = TenantA, OrgUnitId = unit.Id, PositionCode = "01.02-GR-05-003", GradeCode = "GR-05", BudgetYear = 2026 },
                new FundedPosition { TenantId = TenantA, OrgUnitId = unit.Id, PositionCode = "01.02-GR-05-003", GradeCode = "GR-05", BudgetYear = 2027 },
                new FundedPosition { TenantId = TenantA, OrgUnitId = unit.Id, PositionCode = "FP-0001", GradeCode = "GR-05", BudgetYear = 2026 });
            db.CodeSequenceCounters.AddRange(
                Counter(TenantA, CodeEntityKind.RecruitmentRequisition, Key(TenantA, "RecruitmentRequisition", "2026", "2026"), 2026, 7),
                Counter(TenantA, CodeEntityKind.RecruitmentRequisition, Key(TenantA, "RecruitmentRequisition", "2027", "2026"), 2026, 3),
                Counter(TenantA, CodeEntityKind.FundedPosition, Key(TenantA, "FundedPosition", "01.02", "GR-05", "2026"), 0, 3),
                Counter(TenantA, CodeEntityKind.FundedPosition, Key(TenantA, "FundedPosition", "01.02", "GR-05", "2027"), 0, 3),
                Counter(TenantA, CodeEntityKind.AssetTag, Key(TenantA, "AssetTag", Year.ToString(System.Globalization.CultureInfo.InvariantCulture)), Year, 12),
                Counter(TenantB, CodeEntityKind.RecruitmentRequisition, Key(TenantB, "RecruitmentRequisition", "2026", "2026"), 2026, 2),
                Counter(TenantB, CodeEntityKind.AssetTag, Key(TenantB, "AssetTag", Year.ToString(System.Globalization.CultureInfo.InvariantCulture)), Year, 5));
            await db.SaveChangesAsync();
        }

        foreach (var file in new[] { "ten002c-harden-tenant-rls.sql", "ten048-national-shared-read.sql" })
        {
            var script = await global::System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", file));
            await Pol047Pg.ExecAsync(admin, script.Replace(@"\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal));
        }

        var app = await postgres.CreateAppConnectionAsync(new NpgsqlConnectionStringBuilder(admin).Database!);
        return (admin, app, unit.Id);
    }

    private static async Task<Dictionary<string, long>> CountersAsync(string admin)
    {
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""SELECT "ScopeKey", "Year", "LastValue" FROM "CodeSequenceCounters" """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var counters = new Dictionary<string, long>();
        while (await reader.ReadAsync())
            counters[$"{reader.GetString(0)}@{reader.GetInt32(1)}"] = reader.GetInt64(2);
        return counters;
    }

    private static Task<string> GenerateAsync(ServiceProviderHolder holder, Guid tenant, CodeEntityKind kind, string? key = null, params (string Name, string Value)[] facts) =>
        Pol047Pg.AsTenantAsync(holder.Provider, tenant, async db =>
        {
            var request = new CodeGenerationRequest { EntityKind = kind, PolicyKey = key };
            foreach (var (factName, value) in facts)
                request.Facts[factName] = value;
            return (await new CodeGeneratorService(db, holder.Cache, NullLogger<CodeGeneratorService>.Instance).GenerateAsync(request)).Code;
        });

    private sealed record ServiceProviderHolder(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, IMemoryCache Cache);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_upgrade_publishes_v2_reseeds_every_tenant_never_lowers_a_counter_and_runs_once()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, _) = await PrepareAsync(fixture, "main");
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        var before = await CountersAsync(admin);

        await using var provider = Pol047Pg.AppProvider(app);
        var holder = new ServiceProviderHolder(provider, new MemoryCache(new MemoryCacheOptions()));
        var result = await Pol047Pg.AsTenantAsync(provider, National, db => CodePolicyUpgrades.ApplyAsync(db, provider, NullLogger.Instance));

        Assert.False(result.AlreadyApplied);
        Assert.Equal(2, result.Upgraded.Count);
        Assert.Equal(2, result.Reseed!.Tenants.Count(t => t == TenantA || t == TenantB));
        Assert.Empty(result.Reseed.Failures);

        // v2 published, v1 archived, recorded once.
        foreach (var (kind, json) in new[] { ("FundedPosition", CodePolicyUpgrades.FundedPositionV2Json), ("RecruitmentRequisition", CodePolicyUpgrades.RecruitmentRequisitionV2Json) })
        {
            Assert.Equal("Archived", await Pol047Pg.ScalarAsync<string>(admin,
                $"""SELECT v."Status" FROM "CodePolicyVersions" v JOIN "CodePolicyTemplates" t ON t."Id" = v."CodePolicyTemplateId" WHERE t."EntityKind" = '{kind}' AND v."VersionNumber" = 1 AND v."EffectiveTo" IS NOT NULL"""));
            var v2 = await Pol047Pg.ScalarAsync<string>(admin,
                $"""SELECT v."SegmentsJson" FROM "CodePolicyVersions" v JOIN "CodePolicyTemplates" t ON t."Id" = v."CodePolicyTemplateId" WHERE t."EntityKind" = '{kind}' AND v."VersionNumber" = 2 AND v."Status" = 'Published'""");
            Assert.True(CodePolicyUpgrades.SameSegments(json, v2));
        }

        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "SeedPackApplications" WHERE "PackId" = '{CodePolicyUpgrades.PackId}' AND "TenantId" = '{National}'"""));

        // Counters on the new keys, from each tenant's own codes; the old rows are untouched and nothing went down.
        var after = await CountersAsync(admin);
        foreach (var (key, value) in before)
            Assert.True(after[key] >= value, $"{key} went down: {value} -> {after[key]}");
        var year = Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(7, after[Key(TenantA, "RecruitmentRequisition", "2026") + "@0"]);
        Assert.Equal(3, after[Key(TenantA, "FundedPosition", "01.02", "GR-05") + "@0"]);
        Assert.Equal(12, after[Key(TenantA, "AssetTag", year) + $"@{Year}"]);
        Assert.Equal(12, after[Key(TenantA, "AssetTag", "ASSET-PRIVILEGED", year) + $"@{Year}"]);
        Assert.Equal(2, after[Key(TenantB, "RecruitmentRequisition", "2026") + "@0"]);
        Assert.False(after.ContainsKey(Key(TenantB, "AssetTag", "ASSET-PRIVILEGED", year) + $"@{Year}"));

        // Codes of another shape are reported, never guessed.
        Assert.Contains(result.Reseed.Unparsed, u => u.TenantId == TenantA && u.Code == "REQ-2026-A0001");
        Assert.Contains(result.Reseed.Unparsed, u => u.TenantId == TenantA && u.Code == "FP-0001");

        // The next numbers, in each tenant's own session, collide with nothing.
        Assert.Equal("REQ-2026-00008", await GenerateAsync(holder, TenantA, CodeEntityKind.RecruitmentRequisition, null, ("BudgetYear", "2026")));
        Assert.Equal("REQ-2027-00001", await GenerateAsync(holder, TenantA, CodeEntityKind.RecruitmentRequisition, null, ("BudgetYear", "2027")));
        Assert.Equal("01.02-GR-05-004", await GenerateAsync(holder, TenantA, CodeEntityKind.FundedPosition, null, ("OrgUnitCode", "01.02"), ("GradeCode", "GR-05")));
        Assert.Equal($"VIP-{Year}-000013", await GenerateAsync(holder, TenantA, CodeEntityKind.AssetTag, "ASSET-PRIVILEGED"));
        Assert.Equal($"AH-{Year}-000013", await GenerateAsync(holder, TenantA, CodeEntityKind.AssetTag));
        Assert.Equal("REQ-2026-00003", await GenerateAsync(holder, TenantB, CodeEntityKind.RecruitmentRequisition, null, ("BudgetYear", "2026")));
        Assert.Equal($"AH-{Year}-000006", await GenerateAsync(holder, TenantB, CodeEntityKind.AssetTag));

        // A second run changes nothing: the upgrade is recorded, and reseeding again raises no counter.
        var counters = await CountersAsync(admin);
        var again = await Pol047Pg.AsTenantAsync(provider, National, db => CodePolicyUpgrades.ApplyAsync(db, provider, NullLogger.Instance));
        Assert.True(again.AlreadyApplied);
        var reseedAgain = await Pol047Pg.AsTenantAsync(provider, National, db => CodeCounterReseeder.ReseedAllTenantsAsync(db, provider, NullLogger.Instance));
        Assert.Empty(reseedAgain.CountersRaised);
        Assert.Equal(counters, await CountersAsync(admin));
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "SeedPackApplications" WHERE "PackId" = '{CodePolicyUpgrades.PackId}'"""));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(app));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(admin));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Each_tenant_is_reseeded_in_its_own_session_never_through_a_bypass()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app, _) = await PrepareAsync(fixture, "sess");
        await using var provider = Pol047Pg.AppProvider(app);

        // In agency A's session the reseeding sees A's codes only; B's counters stay as they were.
        var report = await Pol047Pg.AsTenantAsync(provider, TenantA, db => CodeCounterReseeder.ReseedTenantAsync(db, TenantA));
        Assert.All(report.CountersRaised, r => Assert.Equal(TenantA, r.TenantId));
        Assert.DoesNotContain(report.Unparsed, u => u.TenantId != TenantA);
        Assert.Equal(0L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "CodeSequenceCounters" WHERE "TenantId" = '{TenantB}' AND "Year" = 0"""));

        // Asked for B while the session is A, row security shows it nothing of B, so it raises nothing (no cross-tenant write).
        var crossed = await Pol047Pg.AsTenantAsync(provider, TenantA, db => CodeCounterReseeder.ReseedTenantAsync(db, TenantB));
        Assert.Empty(crossed.CountersRaised);
        Assert.Equal(0, crossed.CodesRead);
        NpgsqlConnection.ClearPool(new NpgsqlConnection(app));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(admin));
    }
}
