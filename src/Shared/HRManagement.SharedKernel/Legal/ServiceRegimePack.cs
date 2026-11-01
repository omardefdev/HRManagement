using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HRManagement.SharedKernel.Legal;

/// <summary>The rule counts of the SYR-003 pack; the readiness check refuses a pack where any of them is zero.</summary>
public sealed record ServiceRegimePackCounts(int Regimes, int SeparationRules, int MovementTypes, int PromotionRules, int Article159Patterns)
{
    public IEnumerable<(string Name, int Count)> Required()
    {
        yield return ("regimes", Regimes);
        yield return ("separation_rules", SeparationRules);
        yield return ("movement_types", MovementTypes);
        yield return ("promotion_rules", PromotionRules);
        yield return ("article_159_patterns", Article159Patterns);
    }
}

/// <summary>
/// PLAN-37 (§6, D-INT-1): the one source of the SYR-003 service-regimes legal pack for every reader
/// (<see cref="ServiceRegimeSeparationRules"/>, <see cref="ServiceRegimeMovementRules"/>,
/// <see cref="ServiceRegimePromotionRules"/>, <see cref="ServiceRegimeArticle159Rules"/>, HR.Core's transition rules and
/// seeder). Sources in order:
/// <list type="number">
/// <item><c>LegalPacks:ServiceRegimesPath</c> when set (an external file, e.g. a volume): it must exist, it is never
/// silently replaced by the shipped copy;</item>
/// <item>the shipped copy <c>AppContext.BaseDirectory/seed-packs/syr003-service-regimes-v1/regimes.json</c> (a
/// <c>Content</c> item of HRManagement.SharedKernel, so every service that references it has it in its output and
/// publish folder);</item>
/// <item>in Development (or when a test allows it) only: <c>docs/syr/service-regimes.json</c> found walking up from the
/// base directory.</item>
/// </list>
/// The SHA-256 of the repository file is the reference; the start-up line and the readiness check report it.
/// </summary>
public sealed class ServiceRegimePack
{
    public const string PackId = "syr003-service-regimes-v1";
    public const string PathKey = "LegalPacks:ServiceRegimesPath";
    public static readonly string ContentRelativePath = Path.Combine("seed-packs", PackId, "regimes.json");

    private static readonly object Gate = new();
    private static PackOptions _options = PackOptions.FromEnvironment();
    private static Lazy<ServiceRegimePack?> _current = new(() => Locate(_options));

    private ServiceRegimePack(byte[] bytes, string source, string filePath)
    {
        Bytes = bytes;
        Source = source;
        FilePath = filePath;
        Json = Encoding.UTF8.GetString(bytes);
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Counts = CountRules(Json);
    }

    public byte[] Bytes { get; }
    public string Json { get; }
    public string Sha256 { get; }

    /// <summary><c>Path</c> (configured), <c>Content</c> (shipped) or <c>Repository</c> (Development fallback).</summary>
    public string Source { get; }

    public string FilePath { get; }
    public ServiceRegimePackCounts Counts { get; }

    /// <summary>The pack every reader uses, or null when none was found (readiness then fails).</summary>
    /// <exception cref="FileNotFoundException">The configured <c>LegalPacks:ServiceRegimesPath</c> does not exist.</exception>
    public static ServiceRegimePack? Current => _current.Value;

    /// <summary>The pack JSON for the static rule readers, or null when there is no pack.</summary>
    internal static string? CurrentJson => Current?.Json;

    /// <summary>
    /// Applied at start-up (AddServiceRegimePackCheck, during service registration) before any rule is read: the
    /// configured path and whether the repository fallback is allowed (Development). The rule readers cache what they
    /// read first, so this must run before the host starts.
    /// </summary>
    public static void Configure(string? configuredPath, bool allowRepositoryFallback)
    {
        lock (Gate)
        {
            _options = new PackOptions(string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath.Trim(), allowRepositoryFallback, AppContext.BaseDirectory);
            _current = new Lazy<ServiceRegimePack?>(() => Locate(_options));
        }
    }

    /// <summary>Loads from explicit options (tests and the readiness check use this; it does not change <see cref="Current"/>).</summary>
    public static ServiceRegimePack? Load(string? configuredPath, bool allowRepositoryFallback, string baseDirectory) =>
        Locate(new PackOptions(string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath, allowRepositoryFallback, baseDirectory));

    /// <summary>The start-up fingerprint line (one line, no secrets).</summary>
    public string StartupLine() =>
        $"LegalPack loaded pack={PackId} source={Source} sha256={Sha256} regimes={Counts.Regimes} separationRules={Counts.SeparationRules} " +
        $"movementTypes={Counts.MovementTypes} promotionRules={Counts.PromotionRules} article159Patterns={Counts.Article159Patterns}";

    private static ServiceRegimePack? Locate(PackOptions options)
    {
        if (options.ConfiguredPath is not null)
        {
            var full = Path.GetFullPath(options.ConfiguredPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"{PathKey} points to {full}, which does not exist (legal pack {PackId}).", full);
            return new ServiceRegimePack(File.ReadAllBytes(full), "Path", full);
        }

        var shipped = Path.Combine(options.BaseDirectory, ContentRelativePath);
        if (File.Exists(shipped))
            return new ServiceRegimePack(File.ReadAllBytes(shipped), "Content", shipped);

        if (!options.AllowRepositoryFallback)
            return null;

        var dir = new DirectoryInfo(options.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "syr", "service-regimes.json");
            if (File.Exists(candidate))
                return new ServiceRegimePack(File.ReadAllBytes(candidate), "Repository", candidate);
        }

        return null;
    }

    private static ServiceRegimePackCounts CountRules(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var regimes = doc.RootElement.TryGetProperty("regimes", out var p) && p.ValueKind == JsonValueKind.Array ? p.GetArrayLength() : 0;
            // The rule counts are what each reader actually loads (a renamed property or an empty entry counts as nothing),
            // so readiness cannot pass on a pack every movement, promotion or separation would then be refused against.
            return new ServiceRegimePackCounts(
                regimes,
                ServiceRegimeSeparationRules.Parse(json).Count,
                ServiceRegimeMovementRules.Parse(json).Count,
                ServiceRegimePromotionRules.Parse(json).Count,
                ServiceRegimeArticle159Rules.Parse(json).Count);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new ServiceRegimePackCounts(0, 0, 0, 0, 0);
        }
    }

    private sealed record PackOptions(string? ConfiguredPath, bool AllowRepositoryFallback, string BaseDirectory)
    {
        // Before Configure runs (static readers in tools and tests): the environment variable form of the key, and
        // the repository fallback only in Development.
        public static PackOptions FromEnvironment()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            return new PackOptions(
                Environment.GetEnvironmentVariable("LegalPacks__ServiceRegimesPath") is { Length: > 0 } p ? p : null,
                string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase),
                AppContext.BaseDirectory);
        }
    }
}
