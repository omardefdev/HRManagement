using System.Text.Json;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Seeding;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Data;

/// <summary>One defective national code policy and its correction.</summary>
public sealed record CodePolicyUpgrade(
    CodeEntityKind Kind,
    string? PolicyKey,
    string OldSegmentsJson,
    CodeSequenceReset OldReset,
    string NewSegmentsJson,
    CodeSequenceReset NewReset,
    string Reason);

/// <summary>What one run of <see cref="CodePolicyUpgrades.ApplyAsync"/> did.</summary>
public sealed record CodePolicyUpgradeResult(
    bool AlreadyApplied,
    IReadOnlyList<string> Upgraded,
    IReadOnlyList<string> Skipped,
    CodeCounterReseedReport? Reseed);

/// <summary>
/// PLAN-48 (§3, G-COD-03): corrects the defective national code policies of an existing database, once. For each one still
/// published in its old form (a normalised match of its segments and reset), v2 is published from now and v1 archived (EffectiveTo
/// now); then every tenant's counters are reseeded from the codes already issued (<see cref="CodeCounterReseeder"/>, never
/// lower); then the run is recorded in SeedPackApplications as <see cref="PackId"/>. A new database is seeded with the corrected
/// versions directly (<see cref="CodeGenerationSeeder.NationalPolicies"/>), so here it only reseeds and records.
/// <para>
/// Rollback note (P-5): after this upgrade the previous release numbers FundedPosition and RecruitmentRequisition with the v2
/// segments and the same counters, but it shares the AssetTag counter between AH and VIP again, so a VIP number issued after the
/// upgrade can collide under it. Roll forward, not back (docs/OPS_VPS_SINGLE_HOST.md).
/// </para>
/// </summary>
public static class CodePolicyUpgrades
{
    public const string PackId = "ten048-code-policy-upgrade-v1";
    public const string ReleaseDigest = "ten048-code-policy-upgrade-v1:fp-req";
    public const string RollbackRef = "roll-forward only (PLAN-48 P-5)";

    /// <summary>D-TEN-6: the corrected FundedPosition segments, as a new database is seeded with them.</summary>
    public const string FundedPositionV2Json = CodeGenerationSeeder.FundedPositionJson;

    /// <summary>D-TEN-5: the corrected RecruitmentRequisition segments, as a new database is seeded with them.</summary>
    public const string RecruitmentRequisitionV2Json = CodeGenerationSeeder.RecruitmentRequisitionJson;

    public const string FundedPositionV1Json =
        """
        [
          {"kind":"Field","value":"OrgUnitCode"},
          {"kind":"Literal","value":"-"},
          {"kind":"Field","value":"GradeCode"},
          {"kind":"Literal","value":"-"},
          {"kind":"Sequence","padWidth":3,"scopeFields":["OrgUnitCode","GradeCode","BudgetYear"]}
        ]
        """;

    public const string RecruitmentRequisitionV1Json =
        """
        [
          {"kind":"Literal","value":"REQ-"},
          {"kind":"Date","value":"YYYY"},
          {"kind":"Literal","value":"-"},
          {"kind":"Sequence","padWidth":5,"scopeFields":["BudgetYear"]}
        ]
        """;

    /// <summary>
    /// The corrections. The cost centre keeps its segments (it is now a derived kind, with no counter: D-TEN-7), and the privileged
    /// asset tag keeps its segments (its policy key now names its own counter): neither needs a new version.
    /// </summary>
    public static readonly IReadOnlyList<CodePolicyUpgrade> Upgrades =
    [
        new(CodeEntityKind.FundedPosition, null, FundedPositionV1Json, CodeSequenceReset.PerScope,
            FundedPositionV2Json, CodeSequenceReset.PerScope,
            "D-TEN-6: BudgetYear was in the counter's scope but not in the number"),
        new(CodeEntityKind.RecruitmentRequisition, null, RecruitmentRequisitionV1Json, CodeSequenceReset.Yearly,
            RecruitmentRequisitionV2Json, CodeSequenceReset.PerScope,
            "D-TEN-5: the scope was the budget year and the number showed the calendar year")
    ];

    public static async Task<CodePolicyUpgradeResult> ApplyAsync(
        HRCoreDbContext db,
        IServiceProvider services,
        ILogger? logger,
        CancellationToken ct = default)
    {
        var national = NationalTenant.Id;
        if (await db.SeedPackApplications.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .AnyAsync(p => p.TenantId == national && p.PackId == PackId, ct))
            return new CodePolicyUpgradeResult(true, [], [], null);

        var now = DateTime.UtcNow;
        var upgraded = new List<string>();
        var skipped = new List<string>();
        foreach (var upgrade in Upgrades)
        {
            var template = await db.CodePolicyTemplates.NationalOnly()
                .Include(t => t.Versions)
                .Where(t => t.Scope == CodePolicyScope.National && t.EntityKind == upgrade.Kind && t.PolicyKey == upgrade.PolicyKey)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (template is null)
            {
                skipped.Add($"{upgrade.Kind}: no national template");
                continue;
            }

            var current = template.Versions
                .Where(v => v.Status == CodePolicyVersionStatus.Published)
                .Where(v => !v.EffectiveTo.HasValue || v.EffectiveTo > now)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();
            if (current is null)
            {
                skipped.Add($"{upgrade.Kind}: no published version");
                continue;
            }

            if (current.SequenceReset != upgrade.OldReset || !SameSegments(current.SegmentsJson, upgrade.OldSegmentsJson))
            {
                skipped.Add($"{upgrade.Kind}: v{current.VersionNumber} is not the defective form (already corrected or customised)");
                continue;
            }

            current.Status = CodePolicyVersionStatus.Archived;
            current.EffectiveTo = now;
            // Added explicitly: a new row found through a tracked template's collection with its key already set would be taken
            // for an existing row and updated.
            var v2 = new CodePolicyVersion
            {
                TenantId = national,
                CodePolicyTemplateId = template.Id,
                VersionNumber = template.Versions.Max(v => v.VersionNumber) + 1,
                Status = CodePolicyVersionStatus.Published,
                EffectiveFrom = now,
                SegmentsJson = upgrade.NewSegmentsJson,
                SequenceReset = upgrade.NewReset,
                FallbackPrefix = current.FallbackPrefix
            };
            db.CodePolicyVersions.Add(v2);
            upgraded.Add($"{upgrade.Kind}: v{current.VersionNumber} archived, v{v2.VersionNumber} published ({upgrade.Reason})");
        }

        if (upgraded.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            CodeGeneratorService.InvalidateSnapshotCache();
        }

        var reseed = await CodeCounterReseeder.ReseedAllTenantsAsync(db, services, logger, ct);

        db.SeedPackApplications.Add(new SeedPackApplication
        {
            TenantId = national,
            PackId = PackId,
            ReleaseDigest = ReleaseDigest,
            RollbackRef = RollbackRef,
            AppliedAt = now
        });
        await db.SaveChangesAsync(ct);

        logger?.LogInformation(
            "PLAN-48 code policy upgrade {PackId}: upgraded [{Upgraded}]; skipped [{Skipped}]; {Raised} counter(s) raised, {Unparsed} code(s) not parsed",
            PackId, string.Join("; ", upgraded), string.Join("; ", skipped), reseed.CountersRaised.Count, reseed.Unparsed.Count);
        return new CodePolicyUpgradeResult(false, upgraded, skipped, reseed);
    }

    /// <summary>A normalised comparison of two segment lists (kind, value, pad width, scope fields), whatever the whitespace.</summary>
    public static bool SameSegments(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? segmentsJson)
    {
        try
        {
            var segments = CodeFormat.ParseSegments(segmentsJson ?? "[]");
            return string.Join(";", segments.Select(s =>
                $"{s.Kind}|{s.Value}|{s.PadWidth}|{string.Join(",", s.ScopeFields ?? [])}"));
        }
        catch (JsonException)
        {
            return "invalid:" + segmentsJson;
        }
    }
}
