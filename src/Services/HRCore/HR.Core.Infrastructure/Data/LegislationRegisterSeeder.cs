using System.Text.Json;
using System.Text.Json.Serialization;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>SYR-001: loads national legislation register from versioned JSON pack.</summary>
public static class LegislationRegisterSeeder
{
    private static readonly (string Alias, string LegalSourceId)[] KnownAliases =
    [
        ("law50_2004", Syr001LegislationIds.Law50_2004),
        ("law53_2021", Syr001LegislationIds.Law53_2021)
    ];

    private sealed class RegisterPack
    {
        public string Version { get; set; } = "1.0.0";
        public List<RegisterEntryDto> Register { get; set; } = [];
    }

    private sealed class RegisterEntryDto
    {
        public string LegalSourceId { get; set; } = "";
        public string Type { get; set; } = "";
        public string Number { get; set; } = "";
        public string TitleAr { get; set; } = "";
        public string? TitleEn { get; set; }
        public string IssueDate { get; set; } = "";
        public string EffectiveDate { get; set; } = "";
        public string? PublicationDate { get; set; }
        public string Status { get; set; } = "active";
        public string? Scope { get; set; }
        public string Owner { get; set; } = "";
        public string? CertifiedTextHash { get; set; }
        public List<string> AmendedBy { get; set; } = [];
        public List<string> Repeals { get; set; } = [];
        public ArticlesDto? Articles { get; set; }
    }

    private sealed class ArticlesDto
    {
        public List<KeyArticleDto> KeyArticles { get; set; } = [];
    }

    private sealed class KeyArticleDto
    {
        public int Number { get; set; }
        public string Subject { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        var pack = LoadPack();
        var now = DateTime.UtcNow;
        var knownSourceIds = pack.Register
            .Select(e => e.LegalSourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in pack.Register)
        {
            if (string.IsNullOrWhiteSpace(entry.LegalSourceId))
                continue;

            if (!await db.LegislationSources.AnyAsync(s => s.LegalSourceId == entry.LegalSourceId, ct))
            {
                db.LegislationSources.Add(new LegislationSource
                {
                    LegalSourceId = entry.LegalSourceId,
                    InstrumentType = entry.Type,
                    Number = entry.Number,
                    TitleAr = entry.TitleAr,
                    TitleEn = entry.TitleEn,
                    IssueDate = ParseDateOnly(entry.IssueDate),
                    EffectiveDate = ParseDateOnly(entry.EffectiveDate),
                    PublicationDate = ParseNullableDateOnly(entry.PublicationDate),
                    Status = entry.Status,
                    Scope = entry.Scope,
                    Owner = entry.Owner,
                    CertifiedTextHash = entry.CertifiedTextHash,
                    RegisterVersion = pack.Version,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            foreach (var article in entry.Articles?.KeyArticles ?? [])
            {
                if (await db.LegislationKeyArticles.AnyAsync(
                        a => a.LegalSourceId == entry.LegalSourceId && a.ArticleNumber == article.Number, ct))
                    continue;

                db.LegislationKeyArticles.Add(new LegislationKeyArticle
                {
                    LegalSourceId = entry.LegalSourceId,
                    ArticleNumber = article.Number,
                    Subject = article.Subject
                });
            }

            foreach (var targetId in entry.AmendedBy)
                await TryAddLinkAsync(db, entry.LegalSourceId, targetId, LegislationInstrumentLinkType.AmendedBy, knownSourceIds, ct);

            foreach (var targetId in entry.Repeals)
                await TryAddLinkAsync(db, entry.LegalSourceId, targetId, LegislationInstrumentLinkType.Repeals, knownSourceIds, ct);
        }

        foreach (var (alias, legalSourceId) in KnownAliases)
        {
            if (!knownSourceIds.Contains(legalSourceId))
                continue;

            if (await db.LegislationSourceAliases.AnyAsync(a => a.Alias == alias, ct))
                continue;

            db.LegislationSourceAliases.Add(new LegislationSourceAlias
            {
                Alias = alias,
                LegalSourceId = legalSourceId
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task TryAddLinkAsync(
        HRCoreDbContext db,
        string fromLegalSourceId,
        string toLegalSourceId,
        LegislationInstrumentLinkType linkType,
        IReadOnlySet<string> knownSourceIds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toLegalSourceId)
            || !knownSourceIds.Contains(toLegalSourceId)
            || !knownSourceIds.Contains(fromLegalSourceId))
            return;

        if (await db.LegislationInstrumentLinks.AnyAsync(
                l => l.FromLegalSourceId == fromLegalSourceId
                     && l.ToLegalSourceId == toLegalSourceId
                     && l.LinkType == linkType, ct))
            return;

        db.LegislationInstrumentLinks.Add(new LegislationInstrumentLink
        {
            FromLegalSourceId = fromLegalSourceId,
            ToLegalSourceId = toLegalSourceId,
            LinkType = linkType
        });
    }

    private static DateOnly ParseDateOnly(string value) =>
        DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static DateOnly? ParseNullableDateOnly(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseDateOnly(value);

    private static RegisterPack LoadPack()
    {
        foreach (var path in ResolvePackPaths())
        {
            if (!File.Exists(path))
                continue;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RegisterPack>(json, JsonOptions)
                   ?? throw new InvalidOperationException($"Invalid SYR-001 legislation pack JSON: {path}");
        }

        throw new FileNotFoundException("SYR-001 legislation pack not found (seed-packs/syr001-legislation-v1/register.json)");
    }

    private static IEnumerable<string> ResolvePackPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "seed-packs", "syr001-legislation-v1", "register.json");

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            yield return Path.Combine(dir, "seed-packs", "syr001-legislation-v1", "register.json");
            yield return Path.Combine(dir, "docs", "syr", "legislation-register.json");
            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }
    }
}
