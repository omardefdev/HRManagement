using System.Security.Cryptography;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HRManagement.UnitTests;

/// <summary>PLAN-37 (§6): one loader for the SYR-003 pack, its three sources, its fingerprint and its readiness check.</summary>
public sealed class ServiceRegimePackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "int037-pack-" + Guid.NewGuid().ToString("N"));
    private static readonly string RepositoryFile = Path.Combine(RepoPaths.Root, "docs", "syr", "service-regimes.json");
    private static readonly string RepositorySha = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(RepositoryFile))).ToLowerInvariant();

    public ServiceRegimePackTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Shipped_copy_is_found_next_to_the_assembly_with_the_repository_fingerprint()
    {
        var pack = ServiceRegimePack.Load(null, allowRepositoryFallback: false, AppContext.BaseDirectory);

        Assert.NotNull(pack);
        Assert.Equal("Content", pack.Source);
        Assert.Equal(RepositorySha, pack.Sha256);
        Assert.Equal(RepositorySha, ServiceRegimePack.Current!.Sha256);
    }

    [Fact]
    public void Configured_path_wins_over_the_shipped_copy()
    {
        var external = Path.Combine(_root, "external.json");
        System.IO.File.Copy(RepositoryFile, external);

        var pack = ServiceRegimePack.Load(external, allowRepositoryFallback: false, AppContext.BaseDirectory);

        Assert.Equal("Path", pack!.Source);
        Assert.Equal(Path.GetFullPath(external), pack.FilePath);
    }

    [Fact]
    public void Configured_path_that_does_not_exist_is_an_error_not_a_silent_fallback()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            ServiceRegimePack.Load(Path.Combine(_root, "nonexistent.json"), allowRepositoryFallback: true, AppContext.BaseDirectory));
        Assert.Contains(ServiceRegimePack.PathKey, ex.Message);
        Assert.Contains(ServiceRegimePack.PackId, ex.Message);
    }

    [Fact]
    public void Repository_fallback_only_when_allowed()
    {
        var baseDir = Path.Combine(_root, "src", "bin");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(_root, "docs", "syr"));
        System.IO.File.Copy(RepositoryFile, Path.Combine(_root, "docs", "syr", "service-regimes.json"));

        Assert.Null(ServiceRegimePack.Load(null, allowRepositoryFallback: false, baseDir));
        var pack = ServiceRegimePack.Load(null, allowRepositoryFallback: true, baseDir);
        Assert.Equal("Repository", pack!.Source);
        Assert.Equal(RepositorySha, pack.Sha256);
    }

    [Fact]
    public void Repository_pack_has_every_required_rule_family()
    {
        var pack = ServiceRegimePack.Load(RepositoryFile, allowRepositoryFallback: false, AppContext.BaseDirectory)!;
        Assert.All(pack.Counts.Required(), c => Assert.True(c.Count > 0, $"{c.Name} is empty in docs/syr/service-regimes.json"));
        Assert.Matches(
            $"^LegalPack loaded pack=syr003-service-regimes-v1 source=Path sha256={RepositorySha} regimes=\\d+ separationRules=\\d+ movementTypes=\\d+ promotionRules=\\d+ article159Patterns=\\d+$",
            pack.StartupLine());
    }

    [Fact]
    public async Task Readiness_is_healthy_with_the_repository_pack()
    {
        var pack = ServiceRegimePack.Load(null, false, AppContext.BaseDirectory);
        var result = await new ServiceRegimePackHealthCheck(() => pack).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(RepositorySha, result.Data["sha256"]);
    }

    [Fact]
    public async Task Readiness_fails_without_a_pack_naming_it()
    {
        var result = await new ServiceRegimePackHealthCheck(() => null).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains(ServiceRegimePack.PackId, result.Description);
    }

    [Fact]
    public async Task Readiness_fails_on_an_empty_pack()
    {
        var empty = Path.Combine(_root, "empty.json");
        await System.IO.File.WriteAllTextAsync(empty,
            """{"regimes":[],"separation_rules":[],"movement_types":[],"promotion_rules":[],"article_159_patterns":[]}""");
        var pack = ServiceRegimePack.Load(empty, false, AppContext.BaseDirectory);

        var result = await new ServiceRegimePackHealthCheck(() => pack).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("syr003-service-regimes-v1 has no regimes, separation_rules, movement_types, promotion_rules, article_159_patterns", result.Description);
    }

    [Fact]
    public async Task Readiness_fails_on_entries_the_readers_cannot_use()
    {
        // Non-empty arrays whose entries have no key (or a renamed one): the readers load nothing from them.
        var unusable = Path.Combine(_root, "unusable.json");
        await System.IO.File.WriteAllTextAsync(unusable,
            """{"regimes":[{"id":"law50"}],"separation_rules":[{"regime_id":"law50"}],"movement_types":[{}],"promotion_rules":[{"name":"x"}],"article_159_patterns":[{"trigger":"t"}]}""");
        var pack = ServiceRegimePack.Load(unusable, false, AppContext.BaseDirectory);

        Assert.NotNull(pack);
        Assert.Equal(new ServiceRegimePackCounts(1, 0, 0, 0, 0), pack!.Counts);
        var result = await new ServiceRegimePackHealthCheck(() => pack).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("separation_rules, movement_types, promotion_rules, article_159_patterns", result.Description);
    }

    [Fact]
    public async Task Readiness_fails_when_the_configured_path_is_missing()
    {
        var missing = Path.Combine(_root, "nonexistent.json");
        var result = await new ServiceRegimePackHealthCheck(() => ServiceRegimePack.Load(missing, false, AppContext.BaseDirectory))
            .CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains(ServiceRegimePack.PackId, result.Description);
    }

    [Fact]
    public void Rule_readers_see_the_shipped_pack()
    {
        Assert.NotNull(ServiceRegimeSeparationRules.GetRules("law50"));
        var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(RepositoryFile));
        var firstMovement = json.RootElement.GetProperty("movement_types")[0].GetProperty("code").GetString()!;
        Assert.True(ServiceRegimeMovementRules.TryGet(firstMovement, out _));
    }
}
