using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.NoFork;

public sealed class NoForkPolicyProvider : INoForkPolicyProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<NoForkPolicyProvider> _logger;

    public NoForkPolicyProvider(
        IOptions<NoForkOptions> options,
        IHostEnvironment environment,
        ILogger<NoForkPolicyProvider> logger)
    {
        _logger = logger;
        Options = options.Value;
        Matrix = LoadMatrix(environment);
        LoadedPacks = LoadPacks(environment);
        ReleaseDigest = Matrix.ReleaseDigest;
    }

    public NoForkOptions Options { get; }
    public NoForkMatrixDocument Matrix { get; }
    public string ReleaseDigest { get; }
    public IReadOnlyList<AgencyConfigPack> LoadedPacks { get; }

    private NoForkMatrixDocument LoadMatrix(IHostEnvironment environment)
    {
        var path = ResolvePath(
            environment,
            Options.MatrixPath,
            "nofork-matrix.json",
            "NoFork",
            Path.Combine("docs", "str-product", "nofork-matrix.json"));

        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "STR-004 nofork-matrix.json not found. Set NoFork:MatrixPath or deploy the matrix next to the app.",
                path ?? "nofork-matrix.json");
        }

        _logger.LogInformation("Loading STR-004 nofork matrix from {Path}", path);
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<NoForkMatrixDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize nofork-matrix.json");
        if (!string.Equals(doc.Card, "STR-004", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"NoFork matrix card must be STR-004, got '{doc.Card}'.");
        return doc;
    }

    private IReadOnlyList<AgencyConfigPack> LoadPacks(IHostEnvironment environment)
    {
        var dir = ResolvePacksDirectory(environment);
        if (dir is null || !Directory.Exists(dir))
        {
            _logger.LogWarning("STR-004 agency packs directory not found; continuing with zero packs");
            return [];
        }

        var packs = new List<AgencyConfigPack>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var json = File.ReadAllText(file);
            var pack = JsonSerializer.Deserialize<AgencyConfigPack>(json, JsonOptions);
            if (pack is null) continue;
            packs.Add(pack);
            _logger.LogInformation("Loaded STR-004 agency pack {PackId} tenant={TenantId} from {File}",
                pack.PackId, pack.TenantId, file);
        }

        return packs;
    }

    private string? ResolvePacksDirectory(IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(Options.AgencyPacksDirectory))
        {
            var configured = Options.AgencyPacksDirectory;
            if (Path.IsPathRooted(configured) && Directory.Exists(configured))
                return configured;
            var fromContent = Path.Combine(environment.ContentRootPath, configured);
            if (Directory.Exists(fromContent)) return fromContent;
            var fromBase = Path.Combine(AppContext.BaseDirectory, configured);
            if (Directory.Exists(fromBase)) return fromBase;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "NoFork", "agencies"),
                     Path.Combine(environment.ContentRootPath, "NoFork", "agencies")
                 })
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        var repo = WalkForRepo(environment);
        if (repo is not null)
        {
            var testdata = Path.Combine(repo, "testdata", "str004", "agencies");
            if (Directory.Exists(testdata)) return testdata;
        }

        return null;
    }

    private static string? ResolvePath(
        IHostEnvironment environment,
        string? configured,
        string fileName,
        string linkFolder,
        string repoRelative)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathRooted(configured) && File.Exists(configured))
                return configured;
            var fromContent = Path.Combine(environment.ContentRootPath, configured);
            if (File.Exists(fromContent)) return fromContent;
            var fromBase = Path.Combine(AppContext.BaseDirectory, configured);
            if (File.Exists(fromBase)) return fromBase;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, fileName),
                     Path.Combine(AppContext.BaseDirectory, linkFolder, fileName),
                     Path.Combine(environment.ContentRootPath, fileName),
                     Path.Combine(environment.ContentRootPath, linkFolder, fileName)
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        var repo = WalkForRepo(environment);
        if (repo is not null)
        {
            var repoPath = Path.Combine(repo, repoRelative);
            if (File.Exists(repoPath)) return repoPath;
        }

        return null;
    }

    private static string? WalkForRepo(IHostEnvironment environment)
    {
        foreach (var start in new[]
                 {
                     new DirectoryInfo(environment.ContentRootPath),
                     new DirectoryInfo(AppContext.BaseDirectory)
                 })
        {
            var dir = start;
            while (dir is not null)
            {
                var marker = Path.Combine(dir.FullName, "docs", "str-product", "nofork-matrix.json");
                if (File.Exists(marker)) return dir.FullName;
                dir = dir.Parent;
            }
        }

        return null;
    }
}

public sealed class AgencyConfigResolver(INoForkPolicyProvider provider) : IAgencyConfigResolver
{
    private readonly Dictionary<Guid, AgencyConfigPack> _byTenant = provider.LoadedPacks
        .Where(p => Guid.TryParse(p.TenantId, out _))
        .ToDictionary(p => Guid.Parse(p.TenantId), p => p);

    public IReadOnlyList<AgencyConfigPack> AllPacks => provider.LoadedPacks;

    public AgencyConfigPack? TryResolve(Guid? tenantId)
    {
        if (tenantId is null) return null;
        return _byTenant.TryGetValue(tenantId.Value, out var pack) ? pack : null;
    }

    public AgencyConfigPack? TryResolve(string? tenantId) =>
        Guid.TryParse(tenantId, out var id) ? TryResolve(id) : null;
}
