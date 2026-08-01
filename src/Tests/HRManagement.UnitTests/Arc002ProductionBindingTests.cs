using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ArcCriticality;
using HRManagement.ServiceDefaults.ArcReference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Arc002ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "arc-reference", "reference-architecture-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ArcReference:EnforceOnStartup"] = "true",
            ["ArcReference:LiveReferenceClaim"] = "false",
            ["ArcReference:MatrixPath"] = Path.Combine(RepoRoot, "docs", "arc-reference", "reference-architecture-matrix.json")
        };
        foreach (var kv in overrides)
            values[kv.Key] = kv.Value;

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = environmentName,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddArcReference(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ArcReference:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IArcReferenceProvider>();
        Assert.Equal("ARC-002", provider.Matrix.Card);
        Assert.True(provider.Matrix.BoundedContexts?.NoUnjustifiedDistributedComplexityRequired == true);
        Assert.True(provider.Matrix.MultiSiteProfiles?.SameArtifactPrimaryAndDrRequired == true);
        Assert.True(provider.Matrix.CouplingGuardrails?.NoDirectDbCrossBoundaryRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveTwoSiteDeploymentForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-two-site-deployment" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "QA-005" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PHY-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ARC-001" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_unjustified_distribution_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc002-ud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcReferenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-reference", "reference-architecture-matrix.json")))!;
            matrix.BoundedContexts!.NoUnjustifiedDistributedComplexityRequired = false;
            var matrixPath = Path.Combine(tmp, "reference-architecture-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcReference:EnforceOnStartup"] = "true",
                ["ArcReference:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcReferenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noUnjustifiedDistributedComplexityRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_artifact_drift_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc002-ad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcReferenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-reference", "reference-architecture-matrix.json")))!;
            matrix.MultiSiteProfiles!.SameArtifactPrimaryAndDrRequired = false;
            var matrixPath = Path.Combine(tmp, "reference-architecture-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcReference:EnforceOnStartup"] = "true",
                ["ArcReference:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcReferenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("sameArtifactPrimaryAndDrRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_unsupported_topology_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc002-ut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcReferenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-reference", "reference-architecture-matrix.json")))!;
            matrix.SupportedTopologies!.UnsupportedTopologyRejectedRequired = false;
            var matrixPath = Path.Combine(tmp, "reference-architecture-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcReference:EnforceOnStartup"] = "true",
                ["ArcReference:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcReferenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("unsupportedTopologyRejectedRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_direct_db_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc002-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcReferenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-reference", "reference-architecture-matrix.json")))!;
            matrix.CouplingGuardrails!.NoDirectDbCrossBoundaryRequired = false;
            var matrixPath = Path.Combine(tmp, "reference-architecture-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcReference:EnforceOnStartup"] = "true",
                ["ArcReference:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcReferenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noDirectDbCrossBoundaryRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void LiveReferenceClaim_fails_until_live_two_site()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ArcReference:EnforceOnStartup"] = "true",
            ["ArcReference:LiveReferenceClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ArcReferenceStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveReferenceClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_live_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ArcReference:EnforceOnStartup"] = "true",
            ["ArcReference:LiveReferenceClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ArcReferenceStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IArcReferenceProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-two-site-deployment" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "QA-005" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PHY-001" && b.Status == "Satisfied");

        var guard = sp.GetRequiredService<IReferenceClaimGuard>();
        Assert.Null(guard.EnsureLiveReferenceAllowed());
        Assert.NotNull(guard.ValidateBoundedContexts(false, false));
        Assert.NotNull(guard.ValidateMultiSite(false, true));
        Assert.NotNull(guard.ValidateTopology(true, true));
        Assert.NotNull(guard.ValidateCoupling(true, false));
        Assert.Null(guard.ValidateBoundedContexts(true, false));
        Assert.Null(guard.ValidateMultiSite(true, true));
        Assert.Null(guard.ValidateTopology(false, true));
        Assert.Null(guard.ValidateCoupling(false, false));
    }

    [Fact]
    public void Arc001_accepts_ARC002_Satisfied_without_raising_LiveCriticality()
    {
        using var arc1 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "arc-criticality", "criticality-matrix.json")));
        var a1 = arc1.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", a1["ARC-002"]);
        Assert.Equal("Satisfied", a1["PHY-001"]);

        var failures = ArcCriticalityEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<ArcCriticalityMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "arc-criticality", "criticality-matrix.json")))!);
        Assert.DoesNotContain(failures, f => f.Contains("ARC-002", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "arc002-reference-runtime-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"runtime validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_binding_docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-ARC-002-production-reference-architecture-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_002_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_002_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production reference-architecture binding)", backlog, StringComparison.Ordinal);
    }
}
