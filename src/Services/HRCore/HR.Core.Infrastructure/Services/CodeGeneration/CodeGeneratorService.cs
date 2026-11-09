using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>
/// COD-001 with PLAN-48 (§2, §3): every tenant sees the national code policies through one read path; the agency's overlay comes
/// first, then the national template, with the same kind and policy key (no key = the kind's default template, which never answers
/// a keyed request and is never answered by one). A missing policy is an explicit error (422) that burns no number; a fallback number
/// exists only in Development and Testing, and always carries a prefix. One scope key (<see cref="CodeScopeKey"/>) names the counter;
/// a derived kind (<see cref="CodePolicyValidator.DerivedKinds"/>) has no counter and is issued once; a collision is a 409.
/// </summary>
public sealed class CodeGeneratorService(
    HRCoreDbContext db,
    IMemoryCache cache,
    ILogger<CodeGeneratorService> logger,
    IOptions<CodeGenerationOptions>? options = null) : ICodeGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int _cacheGeneration;

    private readonly CodeGenerationOptions _options = options?.Value ?? new CodeGenerationOptions();

    /// <summary>PLAN-48: drops every cached snapshot of this instance (after a publication, an upgrade or an overlay).</summary>
    public static void InvalidateSnapshotCache() => Interlocked.Increment(ref _cacheGeneration);

    public Task<CodeGenerationResult> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default) =>
        GenerateCoreAsync(request, callerClientId: null, ct);

    public Task<CodeGenerationResult> GenerateForServiceAsync(CodeGenerationRequest request, string callerClientId, CancellationToken ct = default) =>
        GenerateCoreAsync(request, callerClientId, ct);

    private async Task<CodeGenerationResult> GenerateCoreAsync(CodeGenerationRequest request, string? callerClientId, CancellationToken ct)
    {
        // An empty tenant id is not a tenant: the session's tenant numbers it (PLAN-48; a draft entity often carries Guid.Empty).
        var tenantId = request.TenantId is Guid requested && requested != Guid.Empty
            ? requested
            : db.CurrentTenantId ?? NationalTenant.Id;
        var now = DateTime.UtcNow;
        var policyKey = NormalizeKey(request.PolicyKey);

        // The policy check comes before any counter moves: a missing or invalid policy burns no number.
        var resolved = await ResolveAsync(request.EntityKind, policyKey, request.Facts, tenantId, now, ct)
                       ?? FallbackOrRefuse(request.EntityKind, policyKey, tenantId);
        var format = resolved.Format;

        if (CodePolicyValidator.IsDerived(request.EntityKind))
            return await GenerateDerivedAsync(request, resolved, tenantId, now, callerClientId, ct);

        // A fact the number shows, or its counter is kept by, must be present before the counter moves.
        var scopeValues = format.ScopeValues(request.Facts);
        _ = format.Compose(request.Facts, 0, now);

        var scopeKey = CodeScopeKey.Build(tenantId, request.EntityKind, resolved.PolicyKey, scopeValues, CodeScopeKey.KeyYear(format.Reset, now.Year));
        var counterYear = CodeScopeKey.CounterYear(format.Reset, now.Year);
        var nextSeq = request.Preview
            ? await PeekSequenceAsync(tenantId, request.EntityKind, scopeKey, counterYear, ct)
            : await IncrementSequenceAsync(tenantId, request.EntityKind, scopeKey, counterYear, ct);

        var code = format.Compose(request.Facts, nextSeq, now);

        if (!request.Preview)
            await RecordIssuanceAsync(tenantId, request, code, resolved.VersionId, scopeKey, callerClientId, ct);

        logger.LogInformation(
            "Code generated {EntityKind} preview={Preview} scope={Scope}",
            request.EntityKind, request.Preview, scopeKey);

        return new CodeGenerationResult
        {
            Code = code,
            PolicyVersionId = resolved.VersionId == Guid.Empty ? null : resolved.VersionId,
            SequenceScope = scopeKey,
            IsPreview = request.Preview
        };
    }

    public async Task<CodePolicySnapshot?> GetPublishedSnapshotAsync(
        CodeEntityKind entityKind,
        Guid? tenantId,
        CancellationToken ct = default,
        string? policyKey = null)
    {
        var tid = tenantId is Guid requested && requested != Guid.Empty ? requested : db.CurrentTenantId ?? NationalTenant.Id;
        var key = NormalizeKey(policyKey);
        // PLAN-48: the key is part of the cache key, and a publication, an upgrade or an overlay drops every entry of this instance.
        // A tenant's entry is read and written only from that tenant's own session: another session (a platform operator naming the
        // tenant) may not see the tenant's overlay under row security, and its national snapshot must not answer the tenant.
        var sessionIsTenant = db.BypassTenantFilter || db.CurrentTenantId == tid;
        var cacheKey = sessionIsTenant ? $"code-policy:{Volatile.Read(ref _cacheGeneration)}:{tid:N}:{entityKind}:{key}" : null;
        if (cacheKey is not null && cache.TryGetValue(cacheKey, out CodePolicySnapshot? cached))
            return cached;

        var resolved = await ResolveAsync(entityKind, key, new Dictionary<string, string>(), tid, DateTime.UtcNow, ct);
        var snapshot = resolved?.ToSnapshot(entityKind);
        if (snapshot is not null && cacheKey is not null)
            cache.Set(cacheKey, snapshot, TimeSpan.FromMinutes(5));
        return snapshot;
    }

    /// <summary>
    /// The policy that numbers <paramref name="kind"/> (with <paramref name="policyKey"/>) for <paramref name="tenantId"/>:
    /// (1) the tenant's overlay, (2) the national template (<c>Scope = National</c>), (3) none. A template with no version in
    /// force is passed over. An invalid published version is refused (<see cref="CodePolicyInvalidException"/>), never used.
    /// </summary>
    public async Task<ResolvedCodePolicy?> ResolveAsync(
        CodeEntityKind kind,
        string? policyKey,
        IReadOnlyDictionary<string, string> facts,
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var key = NormalizeKey(policyKey);
        var national = NationalTenant.Id;
        var templates = await db.CodePolicyTemplates.OwnOrNational(tenantId).AsNoTracking()
            .Include(t => t.Versions)
            .Include(t => t.Rules)
            .Where(t => t.EntityKind == kind)
            .Where(t => key == null ? t.PolicyKey == null : t.PolicyKey == key)
            .ToListAsync(ct);

        var ordered = templates
            .Where(t => (t.TenantId == tenantId && t.Scope == CodePolicyScope.TenantOverride)
                        || (t.TenantId == national && t.Scope == CodePolicyScope.National))
            .OrderBy(t => t.Scope == CodePolicyScope.TenantOverride ? 0 : 1)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id);

        foreach (var template in ordered)
        {
            var version = PickVersion(template, facts, nowUtc);
            if (version is null)
                continue;

            var format = CodeFormat.FromJson(version.SegmentsJson, version.SequenceReset);
            var issues = CodePolicyValidator.ValidateVersion(kind, format);
            if (issues.Count > 0)
            {
                logger.LogError(
                    "PLAN-48 published code policy {VersionId} for {EntityKind} is invalid and refused: {Issues}",
                    version.Id, kind, string.Join(",", issues.Select(i => i.Code)));
                throw new CodePolicyInvalidException(kind, key, issues);
            }

            return new ResolvedCodePolicy(version.Id, template.PolicyKey, template.Scope, template.TenantId, format, version.FallbackPrefix);
        }

        return null;
    }

    private ResolvedCodePolicy FallbackOrRefuse(CodeEntityKind kind, string? policyKey, Guid tenantId)
    {
        if (_options.MissingPolicy != CodeMissingPolicyMode.Fallback)
            throw new CodePolicyMissingException(kind, policyKey, tenantId);

        var prefix = _options.FallbackPrefixFor(kind);
        logger.LogWarning("PLAN-48 no published code policy for {EntityKind}; fallback number with prefix {Prefix} (Development/Testing only)", kind, prefix);
        IReadOnlyList<CodeSegmentDefinition> segments = CodePolicyValidator.IsDerived(kind)
            ?
            [
                new() { Kind = CodeSegmentKind.Literal, Value = prefix + "-" },
                new() { Kind = CodeSegmentKind.Field, Value = "OrgUnitCode" }
            ]
            :
            [
                new() { Kind = CodeSegmentKind.Literal, Value = prefix + "-" },
                new() { Kind = CodeSegmentKind.Date, Value = "YYYY" },
                new() { Kind = CodeSegmentKind.Literal, Value = "-" },
                new() { Kind = CodeSegmentKind.Sequence, PadWidth = 5 }
            ];
        return new ResolvedCodePolicy(Guid.Empty, policyKey, CodePolicyScope.National, NationalTenant.Id,
            new CodeFormat(segments, CodePolicyValidator.IsDerived(kind) ? CodeSequenceReset.Never : CodeSequenceReset.Yearly), prefix);
    }

    private static CodePolicyVersion? PickVersion(CodePolicyTemplate template, IReadOnlyDictionary<string, string> facts, DateTime nowUtc)
    {
        var published = template.Versions
            .Where(v => v.Status == CodePolicyVersionStatus.Published)
            .Where(v => !v.EffectiveFrom.HasValue || v.EffectiveFrom <= nowUtc)
            .Where(v => !v.EffectiveTo.HasValue || v.EffectiveTo > nowUtc)
            .ToList();
        if (published.Count == 0)
            return null;

        foreach (var rule in template.Rules.OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            if (!EvaluateWhen(rule.WhenJson, facts))
                continue;
            var chosen = published.FirstOrDefault(v => v.Id == rule.CodePolicyVersionId);
            if (chosen is not null)
                return chosen;
        }

        return published
            .OrderByDescending(v => v.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(v => v.VersionNumber)
            .First();
    }

    private static bool EvaluateWhen(string whenJson, IReadOnlyDictionary<string, string> facts)
    {
        if (string.IsNullOrWhiteSpace(whenJson) || whenJson == "{}")
            return true;

        try
        {
            var group = JsonSerializer.Deserialize<PolicyConditionGroup>(whenJson, JsonOptions);
            return group?.Evaluate(facts) ?? true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeKey(string? policyKey) =>
        string.IsNullOrWhiteSpace(policyKey) ? null : policyKey.Trim();

    /// <summary>
    /// D-TEN-7: a derived code (the cost centre of a unit) comes from the facts with no counter; its issuance is recorded once,
    /// so the second position of a unit gets the same code without breaking the issuance's unique index.
    /// </summary>
    private async Task<CodeGenerationResult> GenerateDerivedAsync(
        CodeGenerationRequest request,
        ResolvedCodePolicy resolved,
        Guid tenantId,
        DateTime now,
        string? callerClientId,
        CancellationToken ct)
    {
        var code = resolved.Format.Compose(request.Facts, 0, now);
        var shown = resolved.Format.FieldNames.Select(f => request.Facts[f].Trim()).ToArray();
        var scopeKey = CodeScopeKey.Build(tenantId, request.EntityKind, resolved.PolicyKey, shown, year: null);

        if (!request.Preview)
            await RecordDerivedIssuanceOnceAsync(tenantId, request, code, resolved.VersionId, scopeKey, callerClientId, ct);

        // No request-derived value in the log (CodeQL cs/log-forging): the kind and the mode only.
        logger.LogInformation("Derived code {EntityKind} preview={Preview}", request.EntityKind, request.Preview);
        return new CodeGenerationResult
        {
            Code = code,
            PolicyVersionId = resolved.VersionId == Guid.Empty ? null : resolved.VersionId,
            SequenceScope = scopeKey,
            IsPreview = request.Preview
        };
    }

    private bool IsPostgres => string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    private async Task RecordDerivedIssuanceOnceAsync(
        Guid tenantId,
        CodeGenerationRequest request,
        string code,
        Guid versionId,
        string scopeKey,
        string? callerClientId,
        CancellationToken ct)
    {
        if (!IsPostgres)
        {
            var exists = await db.CodeIssuances.AnyAsync(
                i => i.TenantId == tenantId && i.EntityKind == request.EntityKind && i.Code == code, ct);
            if (!exists)
                await RecordIssuanceAsync(tenantId, request, code, versionId, scopeKey, callerClientId, ct);
            return;
        }

        var connection = db.Database.GetDbConnection();
        var closeAfterCommand = connection.State != ConnectionState.Open;
        if (closeAfterCommand)
            await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO "CodeIssuances"
                    ("Id", "TenantId", "EntityKind", "Code", "PolicyVersionId", "SequenceScope", "FactsJson", "IssuedAt",
                     "IssuedByClientId", "CreatedAt", "UpdatedAt")
                VALUES
                    (@id, @tenantId, @entityKind, @code, @versionId, @scope, @facts, @now, @client, @now, @now)
                ON CONFLICT ("TenantId", "EntityKind", "Code") DO NOTHING;
                """;
            AddParameter(command, "@id", Guid.NewGuid());
            AddParameter(command, "@tenantId", tenantId);
            AddParameter(command, "@entityKind", request.EntityKind.ToString());
            AddParameter(command, "@code", code);
            AddParameter(command, "@versionId", versionId == Guid.Empty ? DBNull.Value : versionId);
            AddParameter(command, "@scope", scopeKey);
            AddParameter(command, "@facts", JsonSerializer.Serialize(request.Facts, JsonOptions));
            AddParameter(command, "@now", DateTime.UtcNow);
            AddParameter(command, "@client", (object?)callerClientId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (closeAfterCommand)
                await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<long> IncrementSequenceAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        string scopeKey,
        int year,
        CancellationToken ct)
    {
        if (IsPostgres)
            return await IncrementPostgresSequenceAsync(tenantId, entityKind, scopeKey, year, ct);

        var relational = db.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        // PLAN-44: inside a decision's transaction the counter joins it.
        if (relational && db.Database.CurrentTransaction is null)
            tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        try
        {
            var counter = await db.CodeSequenceCounters
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.EntityKind == entityKind
                    && c.ScopeKey == scopeKey && c.Year == year, ct);

            if (counter is null)
            {
                counter = new CodeSequenceCounter
                {
                    TenantId = tenantId,
                    EntityKind = entityKind,
                    ScopeKey = scopeKey,
                    Year = year,
                    LastValue = 1
                };
                db.CodeSequenceCounters.Add(counter);
            }
            else
            {
                counter.LastValue++;
            }

            await db.SaveChangesAsync(ct);
            if (tx is not null)
                await tx.CommitAsync(ct);
            return counter.LastValue;
        }
        finally
        {
            if (tx is not null)
                await tx.DisposeAsync();
        }
    }

    private async Task<long> IncrementPostgresSequenceAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        string scopeKey,
        int year,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var closeAfterCommand = connection.State != ConnectionState.Open;
        if (closeAfterCommand)
            await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO "CodeSequenceCounters"
                    ("Id", "TenantId", "EntityKind", "ScopeKey", "LastValue", "Year", "CreatedAt", "UpdatedAt")
                VALUES
                    (@id, @tenantId, @entityKind, @scopeKey, 1, @year, @now, @now)
                ON CONFLICT ("TenantId", "EntityKind", "ScopeKey", "Year")
                DO UPDATE SET
                    "LastValue" = "CodeSequenceCounters"."LastValue" + 1,
                    "UpdatedAt" = EXCLUDED."UpdatedAt"
                RETURNING "LastValue";
                """;

            AddParameter(command, "@id", Guid.NewGuid());
            AddParameter(command, "@tenantId", tenantId);
            AddParameter(command, "@entityKind", entityKind.ToString());
            AddParameter(command, "@scopeKey", scopeKey);
            AddParameter(command, "@year", year);
            AddParameter(command, "@now", DateTime.UtcNow);

            var value = await command.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("PostgreSQL did not return the incremented code sequence value.");
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (closeAfterCommand)
                await db.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task<long> PeekSequenceAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        string scopeKey,
        int year,
        CancellationToken ct)
    {
        var counter = await db.CodeSequenceCounters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.EntityKind == entityKind
                && c.ScopeKey == scopeKey && c.Year == year, ct);
        return (counter?.LastValue ?? 0) + 1;
    }

    private async Task RecordIssuanceAsync(
        Guid tenantId,
        CodeGenerationRequest request,
        string code,
        Guid versionId,
        string scopeKey,
        string? callerClientId,
        CancellationToken ct)
    {
        var issuance = new CodeIssuance
        {
            TenantId = tenantId,
            EntityKind = request.EntityKind,
            Code = code,
            PolicyVersionId = versionId == Guid.Empty ? null : versionId,
            SequenceScope = scopeKey,
            FactsJson = JsonSerializer.Serialize(request.Facts, JsonOptions),
            IssuedAt = DateTime.UtcNow,
            IssuedByClientId = callerClientId
        };
        db.CodeIssuances.Add(issuance);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // PLAN-48: the counter was behind an issued code. Say which, never answer with a bare 500.
            db.Entry(issuance).State = EntityState.Detached;
            // No request-derived value in the log (CodeQL cs/log-forging): the kind and the tenant; the 409 names the number.
            logger.LogError(ex, "PLAN-48 code collision: a {EntityKind} number of tenant {TenantId} was already issued", request.EntityKind, tenantId);
            throw new CodeCollisionException(request.EntityKind, code, scopeKey, ex);
        }
    }
}

/// <summary>PLAN-48: the code policy chosen for a request: its version, key, scope, owner tenant and format.</summary>
public sealed record ResolvedCodePolicy(
    Guid VersionId,
    string? PolicyKey,
    CodePolicyScope Scope,
    Guid OwnerTenantId,
    CodeFormat Format,
    string FallbackPrefix)
{
    public CodePolicySnapshot ToSnapshot(CodeEntityKind kind) => new()
    {
        VersionId = VersionId,
        EntityKind = kind,
        Segments = Format.Segments,
        SequenceReset = Format.Reset,
        FallbackPrefix = FallbackPrefix
    };
}
