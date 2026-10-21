using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-001: EF read provider for the national legislation register.</summary>
public sealed class LegislationRegisterProvider(HRCoreDbContext db) : ILegislationRegisterProvider
{
    public async Task<LegislationSourceSummaryDto?> GetByIdAsync(string legalSourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalSourceId))
            return null;

        return await db.LegislationSources
            .AsNoTracking()
            .Where(s => s.LegalSourceId == legalSourceId)
            .Select(s => new LegislationSourceSummaryDto(
                s.LegalSourceId,
                s.InstrumentType,
                s.Number,
                s.TitleAr,
                s.TitleEn,
                s.EffectiveDate,
                s.Status))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<LegislationSourceSummaryDto?> ResolveAliasAsync(string alias, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return null;

        return await db.LegislationSourceAliases
            .AsNoTracking()
            .Where(a => a.Alias == alias)
            .Join(
                db.LegislationSources.AsNoTracking(),
                a => a.LegalSourceId,
                s => s.LegalSourceId,
                (_, s) => new LegislationSourceSummaryDto(
                    s.LegalSourceId,
                    s.InstrumentType,
                    s.Number,
                    s.TitleAr,
                    s.TitleEn,
                    s.EffectiveDate,
                    s.Status))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<LegislationSourceSummaryDto>> ListActiveAsync(CancellationToken ct = default) =>
        await db.LegislationSources
            .AsNoTracking()
            .Where(s => s.Status == "active")
            .OrderBy(s => s.Number)
            .ThenBy(s => s.TitleAr)
            .Select(s => new LegislationSourceSummaryDto(
                s.LegalSourceId,
                s.InstrumentType,
                s.Number,
                s.TitleAr,
                s.TitleEn,
                s.EffectiveDate,
                s.Status))
            .ToListAsync(ct);

    public async Task<LegislationSourceDetailDto?> GetDetailAsync(string legalSourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalSourceId))
            return null;

        var source = await db.LegislationSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LegalSourceId == legalSourceId, ct);
        if (source is null)
            return null;

        var keyArticles = await db.LegislationKeyArticles
            .AsNoTracking()
            .Where(a => a.LegalSourceId == legalSourceId)
            .OrderBy(a => a.ArticleNumber)
            .Select(a => new LegislationKeyArticleDto(a.ArticleNumber, a.Subject))
            .ToListAsync(ct);

        var outgoingLinks = await db.LegislationInstrumentLinks
            .AsNoTracking()
            .Where(l => l.FromLegalSourceId == legalSourceId)
            .Join(
                db.LegislationSources.AsNoTracking(),
                l => l.ToLegalSourceId,
                s => s.LegalSourceId,
                (l, target) => new LegislationInstrumentLinkDto(
                    l.FromLegalSourceId,
                    l.ToLegalSourceId,
                    l.LinkType,
                    target.Number,
                    target.TitleAr))
            .ToListAsync(ct);

        return new LegislationSourceDetailDto(
            source.LegalSourceId,
            source.InstrumentType,
            source.Number,
            source.TitleAr,
            source.TitleEn,
            source.IssueDate,
            source.EffectiveDate,
            source.PublicationDate,
            source.Status,
            source.Scope,
            source.Owner,
            source.CertifiedTextHash,
            source.RegisterVersion,
            keyArticles,
            outgoingLinks.Where(l => l.LinkType == LegislationInstrumentLinkType.AmendedBy).ToList(),
            outgoingLinks.Where(l => l.LinkType == LegislationInstrumentLinkType.Repeals).ToList());
    }

    public async Task<IReadOnlyList<LegislationKeyArticleDto>> GetKeyArticlesAsync(
        string legalSourceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalSourceId))
            return [];

        return await db.LegislationKeyArticles
            .AsNoTracking()
            .Where(a => a.LegalSourceId == legalSourceId)
            .OrderBy(a => a.ArticleNumber)
            .Select(a => new LegislationKeyArticleDto(a.ArticleNumber, a.Subject))
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(string legalSourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalSourceId))
            return false;

        return await db.LegislationSources
            .AsNoTracking()
            .AnyAsync(s => s.LegalSourceId == legalSourceId, ct);
    }
}
