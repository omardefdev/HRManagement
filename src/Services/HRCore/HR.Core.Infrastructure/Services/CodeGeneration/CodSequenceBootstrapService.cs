using System.Text.RegularExpressions;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>
/// MIG-003 hybrid: seed sequence counters from existing codes without rewriting them.
/// </summary>
public sealed class CodSequenceBootstrapService(HRCoreDbContext db)
{
    private static readonly Regex AhTag = new(@"^AH-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ItTag = new(@"^IT-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VipTag = new(@"^VIP-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LcCase = new(@"^LC-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ArcRef = new(@"^ARC-([A-Z0-9]+)-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SrvRef = new(@"^SRV-([A-Z0-9]+)-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SkuTag = new(@"^SKU-(\d{4})-(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OrgUnitScoped = new(@"^(.+)\.(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<BootstrapResult> BootstrapHRCoreAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var tid = tenantId ?? TenantDefaults.DefaultTenantId;
        var orgCodes = await db.OrgUnits.AsNoTracking().Select(o => o.Code).ToListAsync(ct);
        var positionCodes = await db.FundedPositions.AsNoTracking().Select(p => p.PositionCode).ToListAsync(ct);

        var upserts = 0;
        upserts += await UpsertOrgUnitCountersAsync(tid, orgCodes, ct);
        upserts += await UpsertPositionCountersAsync(tid, positionCodes, ct);

        if (upserts > 0)
            await db.SaveChangesAsync(ct);

        return new BootstrapResult { CountersUpserted = upserts };
    }

    public async Task<int> BootstrapFromObservedCodesAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        IEnumerable<string> codes,
        CancellationToken ct = default)
    {
        var maxByScope = new Dictionary<(string ScopeKey, int Year), long>();
        foreach (var raw in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var code = raw.Trim();
            if (IsDemoExcluded(code))
                continue;

            if (!TryParseCounter(tenantId, entityKind, code, out var scopeKey, out var year, out var seq))
                continue;

            var key = (scopeKey, year);
            if (!maxByScope.TryGetValue(key, out var current) || seq > current)
                maxByScope[key] = seq;
        }

        var upserts = 0;
        foreach (var ((scopeKey, year), max) in maxByScope)
        {
            if (await UpsertCounterAsync(tenantId, entityKind, scopeKey, year, max, ct))
                upserts++;
        }

        if (upserts > 0)
            await db.SaveChangesAsync(ct);
        return upserts;
    }

    private async Task<int> UpsertOrgUnitCountersAsync(Guid tenantId, IEnumerable<string> codes, CancellationToken ct)
    {
        var maxByParent = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code))
                continue;
            var match = OrgUnitScoped.Match(code.Trim());
            if (!match.Success || !long.TryParse(match.Groups[2].Value, out var seq))
                continue;
            var parent = match.Groups[1].Value;
            maxByParent[parent] = Math.Max(maxByParent.GetValueOrDefault(parent), seq);
        }

        var upserts = 0;
        foreach (var (parent, max) in maxByParent)
        {
            var scopeKey = BuildScopeKey(tenantId, CodeEntityKind.OrgUnit, parent);
            if (await UpsertCounterAsync(tenantId, CodeEntityKind.OrgUnit, scopeKey, year: 0, max, ct))
                upserts++;
        }

        return upserts;
    }

    private async Task<int> UpsertPositionCountersAsync(Guid tenantId, IEnumerable<string> codes, CancellationToken ct)
    {
        var maxByScope = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code))
                continue;
            var parts = code.Trim().Split('-');
            if (parts.Length < 3 || !long.TryParse(parts[^1], out var seq))
                continue;
            var scopeFields = string.Join('-', parts[..^1]);
            maxByScope[scopeFields] = Math.Max(maxByScope.GetValueOrDefault(scopeFields), seq);
        }

        var upserts = 0;
        foreach (var (scopeFields, max) in maxByScope)
        {
            var scopeKey = BuildScopeKey(tenantId, CodeEntityKind.FundedPosition, scopeFields);
            if (await UpsertCounterAsync(tenantId, CodeEntityKind.FundedPosition, scopeKey, year: 0, max, ct))
                upserts++;
        }

        return upserts;
    }

    private async Task<bool> UpsertCounterAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        string scopeKey,
        int year,
        long lastValue,
        CancellationToken ct)
    {
        var counter = await db.CodeSequenceCounters.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.EntityKind == entityKind
                && c.ScopeKey == scopeKey && c.Year == year, ct);

        if (counter is null)
        {
            db.CodeSequenceCounters.Add(new CodeSequenceCounter
            {
                TenantId = tenantId,
                EntityKind = entityKind,
                ScopeKey = scopeKey,
                Year = year,
                LastValue = lastValue
            });
            return true;
        }

        if (lastValue > counter.LastValue)
        {
            counter.LastValue = lastValue;
            return true;
        }

        return false;
    }

    private static bool IsDemoExcluded(string code) =>
        code.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase)
        || code.StartsWith("IT-DEV-", StringComparison.OrdinalIgnoreCase)
        || code.StartsWith("LEG-", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseCounter(
        Guid tenantId,
        CodeEntityKind kind,
        string code,
        out string scopeKey,
        out int year,
        out long seq)
    {
        scopeKey = string.Empty;
        year = 0;
        seq = 0;

        Match m = kind switch
        {
            CodeEntityKind.AssetTag when code.StartsWith("VIP-", StringComparison.Ordinal) => VipTag.Match(code),
            CodeEntityKind.AssetTag => AhTag.Match(code),
            CodeEntityKind.ItAssetTag => ItTag.Match(code),
            CodeEntityKind.InventorySku => SkuTag.Match(code),
            CodeEntityKind.LegalCase => LcCase.Match(code),
            CodeEntityKind.ArchiveReference => ArcRef.Match(code),
            CodeEntityKind.ServiceTransaction => SrvRef.Match(code),
            _ => Match.Empty
        };

        if (!m.Success)
            return false;

        if (kind is CodeEntityKind.ArchiveReference or CodeEntityKind.ServiceTransaction)
        {
            year = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            seq = long.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            scopeKey = BuildScopeKey(tenantId, kind, m.Groups[1].Value, year.ToString());
            return true;
        }

        year = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        seq = long.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        scopeKey = BuildScopeKey(tenantId, kind, year.ToString());
        return true;
    }

    internal static string BuildScopeKey(Guid tenantId, CodeEntityKind entityKind, params string[] parts)
    {
        var scopeParts = new List<string> { tenantId.ToString("N"), entityKind.ToString() };
        scopeParts.AddRange(parts);
        return string.Join("|", scopeParts);
    }
}

public sealed record BootstrapResult
{
    public int CountersUpserted { get; init; }
}
