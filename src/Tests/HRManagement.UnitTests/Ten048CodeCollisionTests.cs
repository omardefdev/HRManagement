using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§3): a counter behind a code already issued answers a collision (CodeCollisionException, a 409 with an Arabic message)
/// naming the kind, the code and the scope, never a bare 500; and the reseeding moves the counter past it, so the next number is free.
/// </summary>
public class Ten048CodeCollisionTests
{
    [Fact]
    public async Task A_counter_behind_an_issued_code_is_a_collision_and_the_reseeding_clears_it()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await CodeGenerationSeeder.SeedAsync(db);
        var year = DateTime.UtcNow.Year;

        // A seed wrote the first two numbers itself, without the generator moving the counter (PLAN-43 §10).
        foreach (var n in new[] { 1, 2 })
        {
            db.CodeIssuances.Add(new CodeIssuance
            {
                TenantId = NationalTenant.Id,
                EntityKind = CodeEntityKind.LegalCase,
                Code = $"LC-{year}-{n:D5}",
                SequenceScope = "seed",
                FactsJson = "{}"
            });
        }

        await db.SaveChangesAsync();

        var generator = new CodeGeneratorService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance);
        var request = new CodeGenerationRequest { EntityKind = CodeEntityKind.LegalCase, TenantId = NationalTenant.Id };
        var collision = await Assert.ThrowsAsync<CodeCollisionException>(() => generator.GenerateAsync(request));
        Assert.Equal(CodeEntityKind.LegalCase, collision.Kind);
        Assert.Equal($"LC-{year}-00001", collision.Code);
        Assert.Contains("صدر من قبل", collision.MessageAr);

        // The failed attempt took 1; the reseeding moves the counter to the highest issued code (2), never lower.
        db.ChangeTracker.Clear();
        var report = await CodeCounterReseeder.ReseedTenantAsync(db, NationalTenant.Id);
        var raise = Assert.Single(report.CountersRaised, r => r.Kind == CodeEntityKind.LegalCase);
        Assert.Equal((1L, 2L), (raise.Before, raise.After));
        Assert.Equal($"LC-{year}-00003", (await generator.GenerateAsync(new CodeGenerationRequest { EntityKind = CodeEntityKind.LegalCase, TenantId = NationalTenant.Id })).Code);
    }
}
