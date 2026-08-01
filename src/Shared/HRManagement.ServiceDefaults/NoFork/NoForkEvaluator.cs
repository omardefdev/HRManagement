using HRManagement.ServiceDefaults.ModuleCatalog;

namespace HRManagement.ServiceDefaults.NoFork;

public static class NoForkEvaluator
{
    public static readonly string[] SoftBlockedIds = Array.Empty<string>();

    public static List<string> EvaluateStartup(
        NoForkMatrixDocument matrix,
        IReadOnlyList<AgencyConfigPack> packs,
        string? activeReleaseDigest)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "STR-004", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be STR-004, got '{matrix.Card}'");

        if (string.IsNullOrWhiteSpace(matrix.ReleaseDigest))
            failures.Add("matrix releaseDigest missing");

        if (!string.IsNullOrWhiteSpace(activeReleaseDigest)
            && !string.Equals(activeReleaseDigest, matrix.ReleaseDigest, StringComparison.Ordinal))
        {
            failures.Add(
                $"ActiveReleaseDigest '{activeReleaseDigest}' does not match matrix '{matrix.ReleaseDigest}'");
        }

        foreach (var id in SoftBlockedIds)
        {
            var b = matrix.Blockers.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (b is null)
                failures.Add($"blocker {id} missing from matrix");
            else if (!string.Equals(b.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
                failures.Add($"blocker {id} must remain Blocked (got {b.Status})");
        }

        foreach (var pack in packs)
        {
            if (string.IsNullOrWhiteSpace(pack.AgencyId))
                failures.Add("agency pack missing agencyId");
            if (string.IsNullOrWhiteSpace(pack.ReleaseDigest))
                failures.Add($"{pack.AgencyId}: missing releaseDigest");
            if (string.IsNullOrWhiteSpace(pack.TenantId) || !Guid.TryParse(pack.TenantId, out _))
                failures.Add($"{pack.AgencyId}: missing/invalid tenantId");
            if (string.IsNullOrWhiteSpace(pack.PackId))
                failures.Add($"{pack.AgencyId}: missing packId");
            if (string.IsNullOrWhiteSpace(pack.RollbackRef))
                failures.Add($"{pack.AgencyId}: missing rollbackRef");

            if (!string.IsNullOrWhiteSpace(pack.ReleaseDigest)
                && !string.Equals(pack.ReleaseDigest, matrix.ReleaseDigest, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{pack.AgencyId}: releaseDigest '{pack.ReleaseDigest}' != matrix '{matrix.ReleaseDigest}'");
            }
        }

        if (packs.Count >= 2)
        {
            var digests = packs.Select(p => p.ReleaseDigest).Distinct(StringComparer.Ordinal).ToList();
            if (digests.Count > 1)
                failures.Add("agency packs must share a single releaseDigest");
        }

        return failures;
    }

    /// <summary>
    /// After STR-003 base exposure: mandatory Ready always; optional only if in pack enabled list and not disabled.
    /// </summary>
    public static bool IsAllowedByAgencyPack(
        ModuleCatalogDocument catalog,
        AgencyConfigPack pack,
        string slugOrSegment)
    {
        var slug = ModuleCatalogEvaluator.NormalizeModuleId(slugOrSegment);
        var m = catalog.Modules.FirstOrDefault(x =>
            string.Equals(x.ModuleId, slug, StringComparison.OrdinalIgnoreCase));
        if (m is null) return false;

        if (string.Equals(m.Availability, "mandatory", StringComparison.OrdinalIgnoreCase))
            return true;

        if (pack.DisabledOptionalModules.Any(x =>
                string.Equals(ModuleCatalogEvaluator.NormalizeModuleId(x), slug, StringComparison.OrdinalIgnoreCase)))
            return false;

        return pack.EnabledOptionalModules.Any(x =>
            string.Equals(ModuleCatalogEvaluator.NormalizeModuleId(x), slug, StringComparison.OrdinalIgnoreCase));
    }
}
