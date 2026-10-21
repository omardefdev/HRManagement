namespace HRManagement.SharedKernel.Legal;

/// <summary>Shared paths for SYR-003 service-regimes.json pack resolution.</summary>
internal static class ServiceRegimeJsonPackPaths
{
    internal static IEnumerable<string> Resolve()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "seed-packs", "syr003-service-regimes-v1", "regimes.json");

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            yield return Path.Combine(dir, "seed-packs", "syr003-service-regimes-v1", "regimes.json");
            yield return Path.Combine(dir, "docs", "syr", "service-regimes.json");
            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }
    }

    internal static string? FindFirstExisting()
    {
        foreach (var path in Resolve())
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
