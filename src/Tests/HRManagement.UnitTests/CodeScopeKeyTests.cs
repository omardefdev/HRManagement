using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>PLAN-48 (§3, G-COD-03 (e)): one scope key. The generator, the bootstrap and the reseeding name the same counter.</summary>
public class CodeScopeKeyTests
{
    private static readonly Guid Tenant = NationalTenant.Id;

    private static async Task<HRCoreDbContext> SeededAsync()
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await CodeGenerationSeeder.SeedAsync(db);
        return db;
    }

    private static CodeGeneratorService Generator(HRCoreDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance);

    [Theory]
    [InlineData(CodeEntityKind.AssetTag, null, null, null)]
    [InlineData(CodeEntityKind.AssetTag, "ASSET-PRIVILEGED", null, null)]
    [InlineData(CodeEntityKind.ServiceTransaction, null, "TypeCode", "LEAV")]
    [InlineData(CodeEntityKind.ArchiveReference, null, "EntityPrefix", "HIRE")]
    public async Task The_generator_and_the_bootstrap_name_the_same_counter(CodeEntityKind kind, string? key, string? fact, string? value)
    {
        CodeGenerationResult issued;
        await using (var db = await SeededAsync())
        {
            var request = new CodeGenerationRequest { EntityKind = kind, TenantId = Tenant, PolicyKey = key };
            if (fact is not null)
                request.Facts[fact] = value!;
            issued = await Generator(db).GenerateAsync(request);
        }

        await using var other = await SeededAsync();
        var upserts = await new CodSequenceBootstrapService(other).BootstrapFromObservedCodesAsync(Tenant, kind, [issued.Code]);
        Assert.Equal(1, upserts);
        var counter = await other.CodeSequenceCounters.SingleAsync();
        Assert.Equal(issued.SequenceScope, counter.ScopeKey);
        Assert.Equal(1, counter.LastValue);
    }

    [Fact]
    public async Task The_generator_and_the_reseeding_name_the_same_counter_for_the_corrected_kinds()
    {
        CodeGenerationResult requisition;
        await using (var db = await SeededAsync())
        {
            requisition = await Generator(db).GenerateAsync(new CodeGenerationRequest
            {
                EntityKind = CodeEntityKind.RecruitmentRequisition,
                TenantId = Tenant,
                Facts = new(StringComparer.OrdinalIgnoreCase) { ["BudgetYear"] = "2027" }
            });
        }

        Assert.Equal("REQ-2027-00001", requisition.Code);
        await using var other = await SeededAsync();
        other.CodeIssuances.Add(new CodeIssuance
        {
            TenantId = Tenant,
            EntityKind = CodeEntityKind.RecruitmentRequisition,
            Code = requisition.Code,
            SequenceScope = "anything",
            FactsJson = """{"BudgetYear":"2027"}"""
        });
        await other.SaveChangesAsync();
        var report = await CodeCounterReseeder.ReseedTenantAsync(other, Tenant);
        Assert.Equal(requisition.SequenceScope, Assert.Single(report.CountersRaised).ScopeKey);
    }

    [Fact]
    public void Without_a_policy_key_the_key_is_the_one_the_generator_built_before_PLAN_48()
    {
        Assert.Equal($"{Tenant:N}|AssetTag|2026", CodeScopeKey.Build(Tenant, CodeEntityKind.AssetTag, null, [], 2026));
        Assert.Equal($"{Tenant:N}|ServiceTransaction|LEAV|2026", CodeScopeKey.Build(Tenant, CodeEntityKind.ServiceTransaction, " ", ["LEAV"], 2026));
        Assert.Equal($"{Tenant:N}|FundedPosition|01.02|GR-05", CodeScopeKey.Build(Tenant, CodeEntityKind.FundedPosition, null, ["01.02", "GR-05"], null));
    }

    [Fact]
    public void A_policy_key_names_its_own_counter()
    {
        var plain = CodeScopeKey.Build(Tenant, CodeEntityKind.AssetTag, null, [], 2026);
        var keyed = CodeScopeKey.Build(Tenant, CodeEntityKind.AssetTag, "ASSET-PRIVILEGED", [], 2026);
        Assert.Equal($"{Tenant:N}|AssetTag|ASSET-PRIVILEGED|2026", keyed);
        Assert.NotEqual(plain, keyed);
    }

    [Theory]
    [InlineData(CodeSequenceReset.Yearly, 2026, 2026)]
    [InlineData(CodeSequenceReset.PerScope, 2026, 0)]
    [InlineData(CodeSequenceReset.Never, 2026, 0)]
    public void Only_a_yearly_reset_keeps_the_year(CodeSequenceReset reset, int year, int counterYear)
    {
        Assert.Equal(counterYear, CodeScopeKey.CounterYear(reset, year));
        Assert.Equal(reset == CodeSequenceReset.Yearly ? (int?)year : null, CodeScopeKey.KeyYear(reset, year));
    }
}
