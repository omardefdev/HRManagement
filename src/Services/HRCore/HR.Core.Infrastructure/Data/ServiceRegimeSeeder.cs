using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Legal;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Data;

/// <summary>SYR-003: loads service regimes from versioned JSON pack and resolves SYR-001 aliases.</summary>
public static class ServiceRegimeSeeder
{
    private sealed class RegimePack
    {
        public List<RegimeEntryDto> Regimes { get; set; } = [];
    }

    private sealed class RegimeEntryDto
    {
        [JsonPropertyName("legal_regime_id")]
        public string LegalRegimeId { get; set; } = "";

        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        [JsonPropertyName("legislation_ref")]
        public string LegislationRef { get; set; } = "";

        [JsonPropertyName("employment_types")]
        public List<string> EmploymentTypes { get; set; } = [];

        [JsonPropertyName("grade_structure")]
        public string? GradeStructure { get; set; }

        [JsonPropertyName("has_promotion_rules")]
        public bool HasPromotionRules { get; set; }

        [JsonPropertyName("covers_pension")]
        public bool CoversPension { get; set; }

        [JsonPropertyName("effective_from")]
        public string EffectiveFrom { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SeedAsync(
        HRCoreDbContext db,
        ILegislationRegisterProvider legislationProvider,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var pack = LoadPack();
        var now = DateTime.UtcNow;

        foreach (var entry in pack.Regimes)
        {
            if (string.IsNullOrWhiteSpace(entry.LegalRegimeId))
                continue;

            var resolved = await legislationProvider.ResolveAliasAsync(entry.LegislationRef, ct);
            var resolvedId = resolved?.LegalSourceId;

            if (ServiceRegimeRules.RequiresResolvedLegislation(entry.LegalRegimeId) && resolvedId is null)
            {
                logger?.LogError(
                    "SYR-003 seed: regime {RegimeId} alias {Alias} did not resolve in SYR-001 register",
                    entry.LegalRegimeId,
                    entry.LegislationRef);
            }

            var existing = await db.LegalRegimes
                .FirstOrDefaultAsync(r => r.LegalRegimeId == entry.LegalRegimeId, ct);

            if (existing is null)
            {
                db.LegalRegimes.Add(new LegalRegime
                {
                    LegalRegimeId = entry.LegalRegimeId,
                    RegimeName = entry.Name,
                    Type = ParseRegimeType(entry.Type),
                    LegislationRef = entry.LegislationRef,
                    ResolvedLegalSourceId = resolvedId,
                    AllowedEmploymentTypes = entry.EmploymentTypes.Count == 0
                        ? null
                        : string.Join(',', entry.EmploymentTypes),
                    GradeStructureId = entry.GradeStructure,
                    HasPromotionRules = entry.HasPromotionRules,
                    CoversPension = entry.CoversPension,
                    EffectiveFrom = ParseUtcDate(entry.EffectiveFrom),
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.RegimeName = entry.Name;
                existing.Type = ParseRegimeType(entry.Type);
                existing.LegislationRef = entry.LegislationRef;
                existing.ResolvedLegalSourceId = resolvedId;
                existing.AllowedEmploymentTypes = entry.EmploymentTypes.Count == 0
                    ? null
                    : string.Join(',', entry.EmploymentTypes);
                existing.GradeStructureId = entry.GradeStructure;
                existing.HasPromotionRules = entry.HasPromotionRules;
                existing.CoversPension = entry.CoversPension;
                existing.EffectiveFrom = ParseUtcDate(entry.EffectiveFrom);
                existing.IsActive = true;
                existing.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static RegimeType ParseRegimeType(string value) =>
        Enum.TryParse<RegimeType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : RegimeType.Special;

    private static DateTime ParseUtcDate(string value) =>
        DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static RegimePack LoadPack()
    {
        // PLAN-37: one source for the pack (ServiceRegimePack).
        var pack = ServiceRegimePack.Current
            ?? throw new FileNotFoundException($"SYR-003 service regimes pack not found ({ServiceRegimePack.ContentRelativePath})");
        return JsonSerializer.Deserialize<RegimePack>(pack.Json, JsonOptions)
               ?? throw new InvalidOperationException($"Invalid SYR-003 service regimes pack JSON: {pack.FilePath}");
    }

}
