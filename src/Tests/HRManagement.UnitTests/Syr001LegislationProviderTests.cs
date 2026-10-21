using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Syr001LegislationProviderTests
{
    private static async Task<LegislationRegisterProvider> CreateSeededProviderAsync()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new HRCoreDbContext(options);
        await LegislationRegisterSeeder.SeedAsync(db);
        return new LegislationRegisterProvider(db);
    }

    [Fact]
    public async Task ListActive_returns_seven_sources()
    {
        var provider = await CreateSeededProviderAsync();
        var sources = await provider.ListActiveAsync();
        Assert.Equal(7, sources.Count);
    }

    [Fact]
    public async Task GetById_returns_law50()
    {
        var provider = await CreateSeededProviderAsync();
        var source = await provider.GetByIdAsync(Syr001LegislationIds.Law50_2004);
        Assert.NotNull(source);
        Assert.Equal("50/2004", source!.Number);
        Assert.Equal("law", source.InstrumentType);
        Assert.Equal("active", source.Status);
        Assert.Contains("50/2004", source.DisplayLabel);
    }

    [Fact]
    public async Task ResolveAlias_law50_2004()
    {
        var provider = await CreateSeededProviderAsync();
        var source = await provider.ResolveAliasAsync("law50_2004");
        Assert.NotNull(source);
        Assert.Equal(Syr001LegislationIds.Law50_2004, source!.LegalSourceId);
    }

    [Fact]
    public async Task GetDetail_includes_key_articles_and_amended_by()
    {
        var provider = await CreateSeededProviderAsync();
        var detail = await provider.GetDetailAsync(Syr001LegislationIds.Law50_2004);
        Assert.NotNull(detail);
        Assert.Equal(7, detail!.KeyArticles.Count);
        Assert.Equal(2, detail.AmendedBy.Count);
        Assert.Empty(detail.Repeals);
        Assert.Contains(detail.AmendedBy, l => l.ToLegalSourceId == "SYR-DEC-67-2026");
    }

    [Fact]
    public async Task Exists_returns_false_for_unknown()
    {
        var provider = await CreateSeededProviderAsync();
        Assert.False(await provider.ExistsAsync("SYR-LAW-17-2010"));
        Assert.True(await provider.ExistsAsync(Syr001LegislationIds.Law50_2004));
    }

    [Fact]
    public async Task Provider_queries_are_idempotent()
    {
        var provider = await CreateSeededProviderAsync();
        var first = await provider.ListActiveAsync();
        var second = await provider.ListActiveAsync();
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].LegalSourceId, second[0].LegalSourceId);
    }
}
