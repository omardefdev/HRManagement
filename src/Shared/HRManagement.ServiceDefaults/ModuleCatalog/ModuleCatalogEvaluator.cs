namespace HRManagement.ServiceDefaults.ModuleCatalog;

/// <summary>Shared readiness / EnabledModules rules for startup, guard, and tests.</summary>
public static class ModuleCatalogEvaluator
{
    /// <summary>Maps gateway path segments that differ from catalog moduleId.</summary>
    public static string NormalizeModuleId(string slugOrSegment)
    {
        if (string.Equals(slugOrSegment, "files", StringComparison.OrdinalIgnoreCase))
            return "file-storage";
        return slugOrSegment;
    }

    public static List<string> Evaluate(
        ModuleCatalogDocument catalog,
        string[] enabledModules,
        bool allowNotReadyExposure,
        bool validateEvidenceFilesOnDisk,
        string? repoRoot)
    {
        var failures = new List<string>();
        var activeExceptions = catalog.Exceptions
            .Where(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(e.ModuleId))
            .Select(e => e.ModuleId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byId = catalog.Modules.ToDictionary(m => m.ModuleId, StringComparer.OrdinalIgnoreCase);
        var pilot = catalog.PilotApprovedSlugs.Count > 0
            ? catalog.PilotApprovedSlugs
            : catalog.Modules
                .Where(m => !string.Equals(m.ServiceTier, "platform", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ModuleId)
                .ToList();

        foreach (var m in catalog.Modules)
        {
            if (!string.Equals(m.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(m.UatPackRef)
                || string.IsNullOrWhiteSpace(m.RunbookRef)
                || string.IsNullOrWhiteSpace(m.SloRef))
            {
                failures.Add($"{m.ModuleId}: Ready missing uat/runbook/slo ref");
                continue;
            }

            if (validateEvidenceFilesOnDisk && !string.IsNullOrWhiteSpace(repoRoot))
            {
                foreach (var rel in new[] { m.UatPackRef!, m.RunbookRef!, m.SloRef! })
                {
                    var full = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                        failures.Add($"{m.ModuleId}: Ready evidence missing: {rel}");
                }
            }
        }

        if (enabledModules.Length == 0)
        {
            foreach (var slug in pilot)
            {
                if (!byId.TryGetValue(slug, out var m))
                {
                    failures.Add($"pilot slug '{slug}' missing from modules");
                    continue;
                }

                if (IsReadyOrWaived(m, activeExceptions, allowNotReadyExposure))
                    continue;
                failures.Add(
                    $"empty EnabledModules (all-on) forbidden while '{slug}' is {m.ReadinessStatus}");
            }

            return failures;
        }

        foreach (var raw in enabledModules)
        {
            var slug = NormalizeModuleId(raw);
            if (!byId.TryGetValue(slug, out var m))
            {
                failures.Add($"EnabledModules contains unknown slug '{raw}'");
                continue;
            }

            if (string.Equals(m.ServiceTier, "platform", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"EnabledModules must not include platform module '{slug}'");
                continue;
            }

            if (!pilot.Contains(slug, StringComparer.OrdinalIgnoreCase)
                && !activeExceptions.Contains(slug))
            {
                failures.Add($"EnabledModules slug '{slug}' not in pilotApprovedSlugs");
            }

            if (!IsReadyOrWaived(m, activeExceptions, allowNotReadyExposure))
                failures.Add($"EnabledModules contains non-Ready '{slug}' ({m.ReadinessStatus})");
        }

        return failures;
    }

    public static bool IsModuleExposed(
        ModuleCatalogDocument catalog,
        string slugOrSegment,
        string[] enabledModules,
        bool allowNotReadyExposure,
        Func<string, bool> hasActiveException)
    {
        var slug = NormalizeModuleId(slugOrSegment);
        var m = catalog.Modules.FirstOrDefault(x =>
            string.Equals(x.ModuleId, slug, StringComparison.OrdinalIgnoreCase));
        if (m is null) return false;
        if (string.Equals(m.ServiceTier, "platform", StringComparison.OrdinalIgnoreCase))
            return false;

        var waived = allowNotReadyExposure || hasActiveException(slug);
        var ready = string.Equals(m.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase);
        if (!ready && !waived)
            return false;

        if (enabledModules.Length == 0)
            return true;

        return enabledModules.Any(e =>
            string.Equals(NormalizeModuleId(e), slug, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReadyOrWaived(
        ModuleCatalogEntry m,
        HashSet<string> activeExceptions,
        bool allowNotReadyExposure)
    {
        if (string.Equals(m.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase))
            return true;
        if (allowNotReadyExposure) return true;
        return activeExceptions.Contains(m.ModuleId);
    }
}
