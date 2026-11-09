using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>A counter the reseeding raised.</summary>
public sealed record CodeCounterRaise(Guid TenantId, CodeEntityKind Kind, string ScopeKey, int Year, long Before, long After);

/// <summary>An existing code that no format in force reads. It is reported, never guessed (D-TEN-4).</summary>
public sealed record UnparsedCode(Guid TenantId, CodeEntityKind Kind, string Source, string Code);

/// <summary>What one reseeding did, over one or every tenant.</summary>
public sealed class CodeCounterReseedReport
{
    public List<Guid> Tenants { get; } = [];

    public int CodesRead { get; set; }

    public List<CodeCounterRaise> CountersRaised { get; } = [];

    public List<UnparsedCode> Unparsed { get; } = [];

    public List<string> Failures { get; } = [];

    public void Add(CodeCounterReseedReport other)
    {
        Tenants.AddRange(other.Tenants);
        CodesRead += other.CodesRead;
        CountersRaised.AddRange(other.CountersRaised);
        Unparsed.AddRange(other.Unparsed);
        Failures.AddRange(other.Failures);
    }
}

/// <summary>
/// PLAN-48 (§3, P-5): sets every counter to at least the highest code already issued in its scope, under the formats in force
/// (<see cref="CodeFormat"/>, <see cref="CodeScopeKey"/>), so the next number never collides with one that exists. A counter is
/// never lowered (<c>GREATEST</c>). A code no format reads is reported, never guessed.
/// <para>
/// Sources: <c>CodeIssuances</c> (every kind, with the issuance's facts and policy version), and the HR.Core columns that hold
/// numbers a seed may have written without the generator (positions, requisitions, offers, citizen references, competitions,
/// establishment change requests, org units, the job catalog).
/// </para>
/// It walks the tenants from the platform table <c>Tenants</c> (outside row security) and reseeds each in its own scope with
/// <see cref="TenantIdFlow"/> set before the scope opens a connection, so it reads and writes under forced row security as the
/// tenant itself; it never relies on a tenant-filter bypass to see other tenants.
/// </summary>
public static class CodeCounterReseeder
{
    public static async Task<CodeCounterReseedReport> ReseedAllTenantsAsync(
        HRCoreDbContext db,
        IServiceProvider services,
        ILogger? logger,
        CancellationToken ct = default)
    {
        var report = new CodeCounterReseedReport();
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).ToListAsync(ct);
        var scopeFactory = services.GetService<IServiceScopeFactory>();
        var scoped = scopeFactory is not null
                     && services.GetService<IServiceProviderIsService>()?.IsService(typeof(HRCoreDbContext)) == true;
        // Tooling with no DI context and no tenant context (design-time, tests) reads every tenant through the one context it was
        // given, as its EF filter does. Anything else must reseed each tenant in its own scope: a tenant session never sees another.
        if (!scoped && !db.BypassTenantFilter)
            throw new InvalidOperationException(
                "PLAN-48: counter reseeding needs HRCoreDbContext in the service provider, to reseed each tenant in its own session.");

        foreach (var tenantId in tenants)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenantId);
            try
            {
                if (!scoped)
                {
                    report.Add(await ReseedTenantAsync(db, tenantId, ct));
                    continue;
                }

                await using var scope = scopeFactory!.CreateAsyncScope();
                var tenantDb = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                report.Add(await ReseedTenantAsync(tenantDb, tenantId, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not hide the others; it is reported and the run continues.
                report.Failures.Add($"{tenantId}: {ex.GetType().Name}: {ex.Message}");
                logger?.LogError(ex, "PLAN-48 counter reseeding failed for tenant {TenantId}", tenantId);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        logger?.LogInformation(
            "PLAN-48 counter reseeding: {Tenants} tenant(s), {Codes} code(s) read, {Raised} counter(s) raised, {Unparsed} not parsed, {Failures} failure(s)",
            report.Tenants.Count, report.CodesRead, report.CountersRaised.Count, report.Unparsed.Count, report.Failures.Count);
        foreach (var raise in report.CountersRaised)
            logger?.LogInformation("PLAN-48 counter raised: {Kind} {Scope} year {Year}: {Before} -> {After}", raise.Kind, raise.ScopeKey, raise.Year, raise.Before, raise.After);
        foreach (var unparsed in report.Unparsed.Take(50))
            logger?.LogWarning("PLAN-48 code not parsed (reported, not guessed): {Tenant} {Kind} {Source} {Code}", unparsed.TenantId, unparsed.Kind, unparsed.Source, unparsed.Code);

        if (report.Failures.Count > 0)
            throw new InvalidOperationException("PLAN-48 counter reseeding failed for: " + string.Join("; ", report.Failures));
        return report;
    }

    /// <summary>One tenant, in a context whose session is that tenant.</summary>
    public static async Task<CodeCounterReseedReport> ReseedTenantAsync(HRCoreDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var report = new CodeCounterReseedReport();
        report.Tenants.Add(tenantId);
        var formats = await LoadFormatsAsync(db, tenantId, ct);
        var maxima = new Dictionary<(CodeEntityKind Kind, string ScopeKey, int Year), long>();

        await foreach (var source in ReadCodesAsync(db, tenantId, formats, ct))
        {
            if (string.IsNullOrWhiteSpace(source.Code))
                continue;
            report.CodesRead++;
            if (!Collect(tenantId, source, formats, maxima))
                report.Unparsed.Add(new UnparsedCode(tenantId, source.Kind, source.Source, source.Code));
        }

        foreach (var ((kind, scopeKey, year), max) in maxima.OrderBy(m => m.Key.Kind).ThenBy(m => m.Key.ScopeKey, StringComparer.Ordinal))
        {
            var raise = await RaiseAsync(db, tenantId, kind, scopeKey, year, max, ct);
            if (raise is not null)
                report.CountersRaised.Add(raise);
        }

        return report;
    }

    /// <summary>The formats in force for the tenant, per kind and policy key (overlay first, then national), with a sequence.</summary>
    public static async Task<IReadOnlyList<TenantCodeFormat>> LoadFormatsAsync(HRCoreDbContext db, Guid tenantId, CancellationToken ct)
    {
        var national = NationalTenant.Id;
        var now = DateTime.UtcNow;
        var templates = await db.CodePolicyTemplates.OwnOrNational(tenantId).AsNoTracking()
            .Include(t => t.Versions)
            .ToListAsync(ct);

        var formats = new List<TenantCodeFormat>();
        foreach (var group in templates
                     .Where(t => (t.TenantId == tenantId && t.Scope == CodePolicyScope.TenantOverride)
                                 || (t.TenantId == national && t.Scope == CodePolicyScope.National))
                     .GroupBy(t => (t.EntityKind, Key: t.PolicyKey ?? string.Empty)))
        {
            foreach (var template in group.OrderBy(t => t.Scope == CodePolicyScope.TenantOverride ? 0 : 1).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id))
            {
                var version = template.Versions
                    .Where(v => v.Status == CodePolicyVersionStatus.Published)
                    .Where(v => !v.EffectiveFrom.HasValue || v.EffectiveFrom <= now)
                    .Where(v => !v.EffectiveTo.HasValue || v.EffectiveTo > now)
                    .OrderByDescending(v => v.EffectiveFrom ?? DateTime.MinValue)
                    .ThenByDescending(v => v.VersionNumber)
                    .FirstOrDefault();
                if (version is null)
                    continue;

                var format = CodeFormat.FromJson(version.SegmentsJson, version.SequenceReset);
                if (format.HasSequence && CodePolicyValidator.ValidateVersion(template.EntityKind, format).Count == 0)
                {
                    formats.Add(new TenantCodeFormat(
                        template.EntityKind,
                        template.PolicyKey,
                        format,
                        template.Versions.Select(v => v.Id).ToHashSet()));
                }

                break;
            }
        }

        return formats;
    }

    private static bool Collect(
        Guid tenantId,
        CodeSource source,
        IReadOnlyList<TenantCodeFormat> formats,
        Dictionary<(CodeEntityKind, string, int), long> maxima)
    {
        var candidates = formats.Where(f => f.Kind == source.Kind).ToList();
        if (source.PolicyVersionId is Guid versionId && candidates.FirstOrDefault(f => f.VersionIds.Contains(versionId)) is { } own)
            candidates = [own];

        var parsed = false;
        foreach (var candidate in candidates)
        {
            if (!candidate.Format.TryParse(source.Code, out var scopeValues, out var seq, out var year, source.KnownFacts)
                && !candidate.Format.TryParse(source.Code, out scopeValues, out seq, out year))
                continue;
            if (candidate.Format.Reset == CodeSequenceReset.Yearly && year is null)
                continue;

            parsed = true;
            var counterYear = candidate.Format.Reset == CodeSequenceReset.Yearly ? year!.Value : 0;
            var scopeKey = CodeScopeKey.Build(tenantId, source.Kind, candidate.PolicyKey, scopeValues,
                candidate.Format.Reset == CodeSequenceReset.Yearly ? year : null);
            var key = (source.Kind, scopeKey, counterYear);
            maxima[key] = maxima.TryGetValue(key, out var current) ? Math.Max(current, seq) : seq;
        }

        return parsed;
    }

    private static async Task<CodeCounterRaise?> RaiseAsync(
        HRCoreDbContext db,
        Guid tenantId,
        CodeEntityKind kind,
        string scopeKey,
        int year,
        long max,
        CancellationToken ct)
    {
        var existing = await db.CodeSequenceCounters.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EntityKind == kind && c.ScopeKey == scopeKey && c.Year == year)
            .Select(c => (long?)c.LastValue)
            .FirstOrDefaultAsync(ct);
        if (existing >= max)
            return null;

        if (string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            var after = await UpsertGreatestAsync(db, tenantId, kind, scopeKey, year, max, ct);
            return after > (existing ?? 0) ? new CodeCounterRaise(tenantId, kind, scopeKey, year, existing ?? 0, after) : null;
        }

        var counter = await db.CodeSequenceCounters
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.EntityKind == kind && c.ScopeKey == scopeKey && c.Year == year, ct);
        if (counter is null)
        {
            db.CodeSequenceCounters.Add(new CodeSequenceCounter
            {
                TenantId = tenantId,
                EntityKind = kind,
                ScopeKey = scopeKey,
                Year = year,
                LastValue = max
            });
        }
        else
        {
            counter.LastValue = Math.Max(counter.LastValue, max);
        }

        await db.SaveChangesAsync(ct);
        return new CodeCounterRaise(tenantId, kind, scopeKey, year, existing ?? 0, max);
    }

    /// <summary>The counter becomes GREATEST(its value, <paramref name="max"/>); a concurrent increment is never lost.</summary>
    private static async Task<long> UpsertGreatestAsync(
        HRCoreDbContext db,
        Guid tenantId,
        CodeEntityKind kind,
        string scopeKey,
        int year,
        long max,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close)
            await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO "CodeSequenceCounters"
                    ("Id", "TenantId", "EntityKind", "ScopeKey", "LastValue", "Year", "CreatedAt", "UpdatedAt")
                VALUES
                    (@id, @tenantId, @kind, @scopeKey, @max, @year, @now, @now)
                ON CONFLICT ("TenantId", "EntityKind", "ScopeKey", "Year")
                DO UPDATE SET
                    "LastValue" = GREATEST("CodeSequenceCounters"."LastValue", EXCLUDED."LastValue"),
                    "UpdatedAt" = CASE WHEN EXCLUDED."LastValue" > "CodeSequenceCounters"."LastValue"
                                       THEN EXCLUDED."UpdatedAt" ELSE "CodeSequenceCounters"."UpdatedAt" END
                RETURNING "LastValue";
                """;
            Add(command, "@id", Guid.NewGuid());
            Add(command, "@tenantId", tenantId);
            Add(command, "@kind", kind.ToString());
            Add(command, "@scopeKey", scopeKey);
            Add(command, "@max", max);
            Add(command, "@year", year);
            Add(command, "@now", DateTime.UtcNow);
            var value = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (close)
                await db.Database.CloseConnectionAsync();
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static readonly JsonSerializerOptions FactsJson = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyDictionary<string, string>? ParseFacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var facts = JsonSerializer.Deserialize<Dictionary<string, string>>(json, FactsJson);
            return facts is null ? null : new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every existing code of the tenant, from the issuances and from the entity columns that hold numbers.</summary>
    private static async IAsyncEnumerable<CodeSource> ReadCodesAsync(
        HRCoreDbContext db,
        Guid tenantId,
        IReadOnlyList<TenantCodeFormat> formats,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var kinds = formats.Select(f => f.Kind).ToHashSet();

        await foreach (var issuance in db.CodeIssuances.AsNoTracking()
                           .Where(i => i.TenantId == tenantId)
                           .Select(i => new { i.EntityKind, i.Code, i.FactsJson, i.PolicyVersionId })
                           .AsAsyncEnumerable()
                           .WithCancellation(ct))
        {
            if (kinds.Contains(issuance.EntityKind))
                yield return new CodeSource(issuance.EntityKind, "CodeIssuances", issuance.Code, ParseFacts(issuance.FactsJson), issuance.PolicyVersionId);
        }

        if (kinds.Contains(CodeEntityKind.FundedPosition))
        {
            var positions = await db.FundedPositions.AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .Select(p => new { p.PositionCode, p.GradeCode, UnitCode = db.OrgUnits.Where(o => o.Id == p.OrgUnitId).Select(o => o.Code).FirstOrDefault() })
                .ToListAsync(ct);
            foreach (var p in positions)
            {
                var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(p.UnitCode))
                    facts["OrgUnitCode"] = p.UnitCode;
                if (!string.IsNullOrWhiteSpace(p.GradeCode))
                    facts["GradeCode"] = p.GradeCode;
                yield return new CodeSource(CodeEntityKind.FundedPosition, "FundedPositions.PositionCode", p.PositionCode, facts, null);
            }
        }

        if (kinds.Contains(CodeEntityKind.OrgUnit))
        {
            var units = await db.OrgUnits.AsNoTracking()
                .Where(o => o.TenantId == tenantId)
                .Select(o => new { o.Code, ParentCode = db.OrgUnits.Where(p => p.Id == o.ParentId).Select(p => p.Code).FirstOrDefault() })
                .ToListAsync(ct);
            foreach (var u in units)
            {
                var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ParentCode"] = u.ParentCode ?? "00" };
                yield return new CodeSource(CodeEntityKind.OrgUnit, "OrgUnits.Code", u.Code, facts, null);
            }
        }

        if (kinds.Contains(CodeEntityKind.JobCatalogEntry))
        {
            foreach (var j in await db.JobCatalogEntries.AsNoTracking().Where(j => j.TenantId == tenantId)
                         .Select(j => new { j.JobCode, j.GradeCode }).ToListAsync(ct))
            {
                var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(j.GradeCode))
                    facts["GradeCode"] = j.GradeCode;
                yield return new CodeSource(CodeEntityKind.JobCatalogEntry, "JobCatalogEntries.JobCode", j.JobCode, facts, null);
            }
        }

        if (kinds.Contains(CodeEntityKind.RecruitmentRequisition))
            foreach (var code in await db.RecruitmentRequisitions.AsNoTracking().Where(r => r.TenantId == tenantId).Select(r => r.RequisitionNumber).ToListAsync(ct))
                yield return new CodeSource(CodeEntityKind.RecruitmentRequisition, "RecruitmentRequisitions.RequisitionNumber", code, null, null);

        if (kinds.Contains(CodeEntityKind.OfferLetter))
            foreach (var code in await db.OfferLetters.AsNoTracking().Where(o => o.TenantId == tenantId).Select(o => o.OfferNumber).ToListAsync(ct))
                yield return new CodeSource(CodeEntityKind.OfferLetter, "OfferLetters.OfferNumber", code, null, null);

        if (kinds.Contains(CodeEntityKind.CitizenReference))
            foreach (var code in await db.CitizenApplications.AsNoTracking().Where(a => a.TenantId == tenantId).Select(a => a.ReferenceNumber).ToListAsync(ct))
                yield return new CodeSource(CodeEntityKind.CitizenReference, "CitizenApplications.ReferenceNumber", code, null, null);

        if (kinds.Contains(CodeEntityKind.RecruitmentCompetition))
            foreach (var code in await db.RecruitmentCompetitions.AsNoTracking().Where(c => c.TenantId == tenantId).Select(c => c.Number).ToListAsync(ct))
                yield return new CodeSource(CodeEntityKind.RecruitmentCompetition, "RecruitmentCompetitions.Number", code, null, null);

        if (kinds.Contains(CodeEntityKind.EstablishmentChangeRequest))
            foreach (var code in await db.EstablishmentChangeRequests.AsNoTracking().Where(r => r.TenantId == tenantId).Select(r => r.RequestNumber).ToListAsync(ct))
                yield return new CodeSource(CodeEntityKind.EstablishmentChangeRequest, "EstablishmentChangeRequests.RequestNumber", code, null, null);
    }

    private sealed record CodeSource(
        CodeEntityKind Kind,
        string Source,
        string Code,
        IReadOnlyDictionary<string, string>? KnownFacts,
        Guid? PolicyVersionId);
}

/// <summary>A format in force for one tenant: the kind, the template's policy key, and every version id of the template.</summary>
public sealed record TenantCodeFormat(CodeEntityKind Kind, string? PolicyKey, CodeFormat Format, IReadOnlySet<Guid> VersionIds);
