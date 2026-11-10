using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-42 §6: a TenantId in the body is ignored; the counter moves in the caller's tenant alone (RUN_TESTCONTAINERS=true).</summary>
public class S2s042CodeIssuanceTenantPostgresTests : IAsyncLifetime
{
    private static readonly Guid CallerTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private PostgreSqlContainer? _postgres;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private HRCoreDbContext Context() => new(
        new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), n => n.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
            .Options,
        new FixedTenantContext(CallerTenant));

    private static CodeGenerationInternalController Controller(HRCoreDbContext db) =>
        new(new CodeGeneratorService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance), new FixedTenantContext(CallerTenant))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", "svc-admin-logistics")], "Bearer"))
                }
            }
        };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_tenant_in_the_body_is_ignored_and_the_issuance_names_the_client()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = Context();
        await db.Database.MigrateAsync();
        // PLAN-48: the national catalog is an explicit seed (production seeds it in the national session).
        using (TenantStampScope.AllowExplicitTenant())
            await CodeGenerationSeeder.SeedAsync(db);
        TenantIdFlow.Set(CallerTenant);

        try
        {
            var result = await Controller(db).Generate(
                new CodeGenerationRequest { EntityKind = CodeEntityKind.AssetTag, TenantId = OtherTenant },
                CancellationToken.None);
            Assert.IsType<OkObjectResult>(result);

            var issuance = await db.CodeIssuances.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Equal(CallerTenant, issuance.TenantId);
            Assert.Equal("svc-admin-logistics", issuance.IssuedByClientId);

            var counters = await db.CodeSequenceCounters.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            Assert.All(counters, c => Assert.Equal(CallerTenant, c.TenantId));
            Assert.Equal(1, counters.Sum(c => c.LastValue));
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_refused_kind_moves_no_counter()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = Context();
        await db.Database.MigrateAsync();
        // PLAN-48: the national catalog is an explicit seed (production seeds it in the national session).
        using (TenantStampScope.AllowExplicitTenant())
            await CodeGenerationSeeder.SeedAsync(db);
        TenantIdFlow.Set(CallerTenant);

        try
        {
            var result = await Controller(db).Generate(
                new CodeGenerationRequest { EntityKind = CodeEntityKind.AdministrativeDecision },
                CancellationToken.None);

            Assert.IsType<ForbidResult>(result);
            Assert.Equal(0, await db.CodeSequenceCounters.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.CodeIssuances.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_number_issued_inside_HR_Core_has_no_client()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = Context();
        await db.Database.MigrateAsync();
        // PLAN-48: the national catalog is an explicit seed (production seeds it in the national session).
        using (TenantStampScope.AllowExplicitTenant())
            await CodeGenerationSeeder.SeedAsync(db);
        TenantIdFlow.Set(CallerTenant);

        try
        {
            var generator = new CodeGeneratorService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance);
            await generator.GenerateAsync(new CodeGenerationRequest { EntityKind = CodeEntityKind.OfferLetter, TenantId = CallerTenant });

            var issuance = await db.CodeIssuances.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            Assert.Null(issuance.IssuedByClientId);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_adds_the_issuer_column()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = Context();
        await db.Database.MigrateAsync();

        var columns = await db.Database
            .SqlQueryRaw<string>("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name = 'CodeIssuances'""")
            .ToListAsync();

        Assert.Contains("IssuedByClientId", columns);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
