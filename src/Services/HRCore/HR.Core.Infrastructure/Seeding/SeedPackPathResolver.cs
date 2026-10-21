namespace HR.Core.Infrastructure.Seeding;

public static class SeedPackPathResolver
{
    public static IEnumerable<string> ResolveSeedPackRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "seed-packs");
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            yield return Path.Combine(dir, "seed-packs");
            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }
    }

    public static string? FindPackFile(string relativePath)
    {
        foreach (var root in ResolveSeedPackRoots())
        {
            var path = Path.Combine(root, relativePath);
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
