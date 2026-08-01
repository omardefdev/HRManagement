namespace HRManagement.TestSupport;

/// <summary>Shared repository root resolution for tests (replaces 149 duplicated ResolveRepoFile copies).</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> CachedRoot = new(FindRoot);

    public static string Root => CachedRoot.Value;

    public static string Resolve(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join("/", parts));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("HRManagement.slnx not found while resolving repository root.");
    }
}
