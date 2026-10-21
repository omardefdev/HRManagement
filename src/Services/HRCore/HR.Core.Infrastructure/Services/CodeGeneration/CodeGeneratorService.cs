using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

public sealed class CodeGeneratorService(
    HRCoreDbContext db,
    IMemoryCache cache,
    ILogger<CodeGeneratorService> logger) : ICodeGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<CodeGenerationResult> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default)
    {
        var tenantId = request.TenantId ?? db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var snapshot = await ResolveSnapshotAsync(request, tenantId, ct)
            ?? BuildFallbackSnapshot(request.EntityKind);

        var scopeKey = BuildSequenceScopeKey(snapshot, request.EntityKind, tenantId, request.Facts);
        var nextSeq = request.Preview
            ? await PeekSequenceAsync(tenantId, request.EntityKind, scopeKey, snapshot.SequenceReset, ct)
            : await IncrementSequenceAsync(tenantId, request.EntityKind, scopeKey, snapshot.SequenceReset, ct);

        var code = ComposeCode(snapshot.Segments, request.Facts, nextSeq);

        if (!request.Preview)
        {
            await RecordIssuanceAsync(tenantId, request, code, snapshot.VersionId, scopeKey, ct);
        }

        logger.LogInformation(
            "Code generated {EntityKind} preview={Preview} scope={Scope}",
            request.EntityKind, request.Preview, scopeKey);

        return new CodeGenerationResult
        {
            Code = code,
            PolicyVersionId = snapshot.VersionId == Guid.Empty ? null : snapshot.VersionId,
            SequenceScope = scopeKey,
            IsPreview = request.Preview
        };
    }

    public async Task<CodePolicySnapshot?> GetPublishedSnapshotAsync(
        CodeEntityKind entityKind,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var tid = tenantId ?? db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var cacheKey = $"code-policy:{tid:N}:{entityKind}";
        if (cache.TryGetValue(cacheKey, out CodePolicySnapshot? cached))
            return cached;

        var request = new CodeGenerationRequest { EntityKind = entityKind, TenantId = tid };
        var snapshot = await ResolveSnapshotAsync(request, tid, ct);
        if (snapshot is not null)
            cache.Set(cacheKey, snapshot, TimeSpan.FromMinutes(5));
        return snapshot;
    }

    private async Task<CodePolicySnapshot?> ResolveSnapshotAsync(
        CodeGenerationRequest request,
        Guid tenantId,
        CancellationToken ct)
    {
        var template = await db.CodePolicyTemplates.AsNoTracking()
            .Include(t => t.Versions)
            .Include(t => t.Rules)
            .Where(t => t.EntityKind == request.EntityKind)
            .Where(t => t.TenantId == tenantId || t.Scope == CodePolicyScope.National)
            .OrderByDescending(t => t.Scope == CodePolicyScope.TenantOverride ? 1 : 0)
            .ThenByDescending(t => t.TenantId == tenantId ? 1 : 0)
            .FirstOrDefaultAsync(ct);

        if (template is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.PolicyKey)
            && !string.Equals(template.PolicyKey, request.PolicyKey, StringComparison.OrdinalIgnoreCase))
        {
            template = await db.CodePolicyTemplates.AsNoTracking()
                .Include(t => t.Versions)
                .Include(t => t.Rules)
                .Where(t => t.EntityKind == request.EntityKind)
                .Where(t => t.PolicyKey == request.PolicyKey)
                .Where(t => t.TenantId == tenantId || t.Scope == CodePolicyScope.National)
                .OrderByDescending(t => t.Scope == CodePolicyScope.TenantOverride ? 1 : 0)
                .FirstOrDefaultAsync(ct);
        }

        if (template is null)
            return null;

        var published = template.Versions
            .Where(v => v.Status == CodePolicyVersionStatus.Published)
            .Where(v => !v.EffectiveFrom.HasValue || v.EffectiveFrom <= DateTime.UtcNow)
            .Where(v => !v.EffectiveTo.HasValue || v.EffectiveTo > DateTime.UtcNow)
            .ToList();

        if (published.Count == 0)
            return null;

        CodePolicyVersion? version = null;
        foreach (var rule in template.Rules.OrderBy(r => r.Priority))
        {
            if (!EvaluateWhen(rule.WhenJson, request.Facts))
                continue;
            version = published.FirstOrDefault(v => v.Id == rule.CodePolicyVersionId);
            if (version is not null)
                break;
        }

        version ??= published.OrderByDescending(v => v.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(v => v.VersionNumber)
            .First();

        return ToSnapshot(version, template.EntityKind);
    }

    private static CodePolicySnapshot BuildFallbackSnapshot(CodeEntityKind kind) =>
        kind switch
        {
            CodeEntityKind.OrgUnit => new CodePolicySnapshot
            {
                EntityKind = kind,
                FallbackPrefix = "OU",
                SequenceReset = CodeSequenceReset.PerScope,
                Segments =
                [
                    new() { Kind = CodeSegmentKind.Field, Value = "ParentCode" },
                    new() { Kind = CodeSegmentKind.Literal, Value = "." },
                    new() { Kind = CodeSegmentKind.Sequence, PadWidth = 2, ScopeFields = ["ParentCode"] }
                ]
            },
            CodeEntityKind.AssetTag => new CodePolicySnapshot
            {
                EntityKind = kind,
                FallbackPrefix = "AH",
                SequenceReset = CodeSequenceReset.Yearly,
                Segments =
                [
                    new() { Kind = CodeSegmentKind.Literal, Value = "AH-" },
                    new() { Kind = CodeSegmentKind.Date, Value = "YYYY" },
                    new() { Kind = CodeSegmentKind.Literal, Value = "-" },
                    new() { Kind = CodeSegmentKind.Sequence, PadWidth = 6 }
                ]
            },
            CodeEntityKind.InventorySku => new CodePolicySnapshot
            {
                EntityKind = kind,
                FallbackPrefix = "SKU",
                SequenceReset = CodeSequenceReset.Yearly,
                Segments =
                [
                    new() { Kind = CodeSegmentKind.Literal, Value = "SKU-" },
                    new() { Kind = CodeSegmentKind.Date, Value = "YYYY" },
                    new() { Kind = CodeSegmentKind.Literal, Value = "-" },
                    new() { Kind = CodeSegmentKind.Sequence, PadWidth = 6 }
                ]
            },
            _ => new CodePolicySnapshot
            {
                EntityKind = kind,
                FallbackPrefix = kind.ToString().ToUpperInvariant()[..Math.Min(3, kind.ToString().Length)],
                SequenceReset = CodeSequenceReset.Yearly,
                Segments =
                [
                    new() { Kind = CodeSegmentKind.Field, Value = "EntityPrefix" },
                    new() { Kind = CodeSegmentKind.Literal, Value = "-" },
                    new() { Kind = CodeSegmentKind.Date, Value = "YYYY" },
                    new() { Kind = CodeSegmentKind.Literal, Value = "-" },
                    new() { Kind = CodeSegmentKind.Sequence, PadWidth = 5 }
                ]
            }
        };

    private static CodePolicySnapshot ToSnapshot(CodePolicyVersion version, CodeEntityKind entityKind)
    {
        var segments = JsonSerializer.Deserialize<List<CodeSegmentDefinition>>(version.SegmentsJson, JsonOptions) ?? [];
        return new CodePolicySnapshot
        {
            VersionId = version.Id,
            EntityKind = entityKind,
            Segments = segments,
            SequenceReset = version.SequenceReset,
            FallbackPrefix = version.FallbackPrefix
        };
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

    private static string BuildSequenceScopeKey(
        CodePolicySnapshot snapshot,
        CodeEntityKind entityKind,
        Guid tenantId,
        IReadOnlyDictionary<string, string> facts)
    {
        var parts = new List<string> { tenantId.ToString("N"), entityKind.ToString() };
        foreach (var seg in snapshot.Segments.Where(s => s.Kind == CodeSegmentKind.Sequence))
        {
            if (seg.ScopeFields is null)
                continue;
            foreach (var field in seg.ScopeFields)
            {
                if (facts.TryGetValue(field, out var v) && !string.IsNullOrWhiteSpace(v))
                    parts.Add(v.Trim());
            }
        }

        if (snapshot.SequenceReset == CodeSequenceReset.Yearly)
            parts.Add(DateTime.UtcNow.Year.ToString());

        return string.Join("|", parts);
    }

    private static string ComposeCode(
        IReadOnlyList<CodeSegmentDefinition> segments,
        IReadOnlyDictionary<string, string> facts,
        long sequence)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            sb.Append(seg.Kind switch
            {
                CodeSegmentKind.Literal => seg.Value,
                CodeSegmentKind.Field or CodeSegmentKind.ParentCode or CodeSegmentKind.LookupPrefix
                    => facts.TryGetValue(seg.Value, out var f) ? f : string.Empty,
                CodeSegmentKind.Date => FormatDate(seg.Value),
                CodeSegmentKind.Sequence => sequence.ToString().PadLeft(Math.Max(seg.PadWidth, 1), '0'),
                _ => string.Empty
            });
        }

        return sb.ToString();
    }

    private static string FormatDate(string format) =>
        format switch
        {
            "YYYY" => DateTime.UtcNow.Year.ToString(),
            "YY" => (DateTime.UtcNow.Year % 100).ToString("D2"),
            "yyyyMMdd" => DateTime.UtcNow.ToString("yyyyMMdd"),
            _ => DateTime.UtcNow.ToString(format)
        };

    private async Task<long> IncrementSequenceAsync(
        Guid tenantId,
        CodeEntityKind entityKind,
        string scopeKey,
        CodeSequenceReset reset,
        CancellationToken ct)
    {
        var year = reset == CodeSequenceReset.Yearly ? DateTime.UtcNow.Year : 0;

        if (string.Equals(
                db.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            return await IncrementPostgresSequenceAsync(tenantId, entityKind, scopeKey, year, ct);
        }

        var relational = db.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (relational)
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
        CodeSequenceReset reset,
        CancellationToken ct)
    {
        var year = reset == CodeSequenceReset.Yearly ? DateTime.UtcNow.Year : 0;
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
        CancellationToken ct)
    {
        db.CodeIssuances.Add(new CodeIssuance
        {
            TenantId = tenantId,
            EntityKind = request.EntityKind,
            Code = code,
            PolicyVersionId = versionId == Guid.Empty ? null : versionId,
            SequenceScope = scopeKey,
            FactsJson = JsonSerializer.Serialize(request.Facts, JsonOptions),
            IssuedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
