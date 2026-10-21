using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Syr001LegislationSeedTests
{
    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }

    [Fact]
    public async Task Seed_populates_seven_sources()
    {
        await using var db = CreateDb();
        await LegislationRegisterSeeder.SeedAsync(db);

        Assert.Equal(7, await db.LegislationSources.CountAsync());
    }

    [Fact]
    public async Task Seed_populates_key_articles_for_law50()
    {
        await using var db = CreateDb();
        await LegislationRegisterSeeder.SeedAsync(db);

        var count = await db.LegislationKeyArticles
            .CountAsync(a => a.LegalSourceId == Syr001LegislationIds.Law50_2004);
        Assert.Equal(7, count);
    }

    [Fact]
    public async Task Seed_creates_amended_by_links()
    {
        await using var db = CreateDb();
        await LegislationRegisterSeeder.SeedAsync(db);

        var links = await db.LegislationInstrumentLinks
            .Where(l => l.FromLegalSourceId == Syr001LegislationIds.Law50_2004
                        && l.LinkType == LegislationInstrumentLinkType.AmendedBy)
            .ToListAsync();

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.ToLegalSourceId == "SYR-DEC-67-2026");
        Assert.Contains(links, l => l.ToLegalSourceId == "SYR-DEC-68-2026");
    }

    [Fact]
    public async Task Seed_creates_syr003_aliases()
    {
        await using var db = CreateDb();
        await LegislationRegisterSeeder.SeedAsync(db);

        var law50Alias = await db.LegislationSourceAliases.FindAsync("law50_2004");
        var law53Alias = await db.LegislationSourceAliases.FindAsync("law53_2021");

        Assert.NotNull(law50Alias);
        Assert.Equal(Syr001LegislationIds.Law50_2004, law50Alias!.LegalSourceId);
        Assert.NotNull(law53Alias);
        Assert.Equal(Syr001LegislationIds.Law53_2021, law53Alias!.LegalSourceId);
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        await using var db = CreateDb();
        await LegislationRegisterSeeder.SeedAsync(db);

        var sourceCount = await db.LegislationSources.CountAsync();
        var articleCount = await db.LegislationKeyArticles.CountAsync();
        var linkCount = await db.LegislationInstrumentLinks.CountAsync();
        var aliasCount = await db.LegislationSourceAliases.CountAsync();

        await LegislationRegisterSeeder.SeedAsync(db);

        Assert.Equal(sourceCount, await db.LegislationSources.CountAsync());
        Assert.Equal(articleCount, await db.LegislationKeyArticles.CountAsync());
        Assert.Equal(linkCount, await db.LegislationInstrumentLinks.CountAsync());
        Assert.Equal(aliasCount, await db.LegislationSourceAliases.CountAsync());
    }

    [Fact]
    public async Task Seed_skips_orphan_repeals_link()
    {
        await using var db = CreateDb();
        await LegislationRegisterSeeder.SeedAsync(db);

        var orphanLink = await db.LegislationInstrumentLinks
            .AnyAsync(l => l.ToLegalSourceId == "SYR-LAW-17-2010");
        Assert.False(orphanLink);
    }
}
