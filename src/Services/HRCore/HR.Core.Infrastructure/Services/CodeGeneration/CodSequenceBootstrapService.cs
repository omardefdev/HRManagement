using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>
/// MIG-003 hybrid: seed sequence counters from existing codes without rewriting them. PLAN-48 (§3, G-COD-03 (e)): rebuilt on the
/// generator's own format (<see cref="CodeFormat"/>) and scope key (<see cref="CodeScopeKey"/>), so a bootstrapped counter is the
/// counter the generator increments next; its own regexes and keys are gone. A counter is never lowered.
/// </summary>
public sealed class CodSequenceBootstrapService(HRCoreDbContext db)
{
    /// <summary>HR.Core's own numbers (org units, positions, requisitions, offers ...) of one tenant, read in that tenant's session.</summary>
    public async Task<BootstrapResult> BootstrapHRCoreAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var tid = tenantId ?? db.CurrentTenantId ?? NationalTenant.Id;
        var report = await CodeCounterReseeder.ReseedTenantAsync(db, tid, ct);
        return new BootstrapResult { CountersUpserted = report.CountersRaised.Count, Unparsed = report.Unparsed.Select(u => u.Code).ToList() };
    }

    /// <summary>
    /// Codes another service holds (asset tags, IT tags, archive and case references ...), read with the formats in force for the
    /// tenant, or with the national catalog when the database has no published policy yet (a migration before the first seed).
    /// </summary>
    public async Task<int> BootstrapFromObservedCodesAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        IEnumerable<string> codes,
        CancellationToken ct = default)
    {
        var formats = (await CodeCounterReseeder.LoadFormatsAsync(db, tenantId, ct)).Where(f => f.Kind == entityKind).ToList();
        if (formats.Count == 0)
        {
            formats = CodeGenerationSeeder.NationalPolicies
                .Where(p => p.Kind == entityKind && p.Format.HasSequence)
                .Select(p => new TenantCodeFormat(p.Kind, p.PolicyKey, p.Format, new HashSet<Guid>()))
                .ToList();
        }

        var maxByScope = new Dictionary<(string ScopeKey, int Year), long>();
        foreach (var raw in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var code = raw.Trim();
            if (IsDemoExcluded(code))
                continue;

            foreach (var candidate in formats)
            {
                if (!candidate.Format.TryParse(code, out var scopeValues, out var seq, out var year))
                    continue;
                if (candidate.Format.Reset == CodeSequenceReset.Yearly && year is null)
                    continue;

                var keyYear = candidate.Format.Reset == CodeSequenceReset.Yearly ? year : null;
                var scopeKey = CodeScopeKey.Build(tenantId, entityKind, candidate.PolicyKey, scopeValues, keyYear);
                var key = (scopeKey, keyYear ?? 0);
                if (!maxByScope.TryGetValue(key, out var current) || seq > current)
                    maxByScope[key] = seq;
            }
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

    /// <summary>
    /// Never lowers a counter, also against a concurrent increment: on PostgreSQL the counter becomes GREATEST(its value, the
    /// highest observed number) in one statement (PLAN-48 review), as the reseeder does.
    /// </summary>
    private async Task<bool> UpsertCounterAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        string scopeKey,
        int year,
        long lastValue,
        CancellationToken ct) =>
        await CodeCounterReseeder.RaiseAsync(db, tenantId, entityKind, scopeKey, year, lastValue, ct) is not null;

    private static bool IsDemoExcluded(string code) =>
        code.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase)
        || code.StartsWith("IT-DEV-", StringComparison.OrdinalIgnoreCase)
        || code.StartsWith("LEG-", StringComparison.OrdinalIgnoreCase);
}

public sealed record BootstrapResult
{
    public int CountersUpserted { get; init; }

    /// <summary>PLAN-48: codes no format in force reads; reported, never guessed.</summary>
    public IReadOnlyList<string> Unparsed { get; init; } = [];
}
