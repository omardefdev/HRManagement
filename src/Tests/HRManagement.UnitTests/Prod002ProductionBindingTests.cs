using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProdAgency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod002ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-agency", "agency-selfadmin-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdAgency:EnforceOnStartup"] = "true",
            ["ProdAgency:LiveAgencySelfAdminClaim"] = "false",
            ["ProdAgency:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-agency", "agency-selfadmin-matrix.json")
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
        services.AddProdAgency(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdAgency:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdAgencyProvider>();
        Assert.Equal("PROD-002", provider.Matrix.Card);
        Assert.True(provider.Matrix.NationalControlsImmutable);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-agency-selfadmin" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-delegated-admin-ui" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "cross-tenant-admin" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ONB-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-003" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-001" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveSelfAdminForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_national_controls_mutable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod002-nat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdAgencyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-agency", "agency-selfadmin-matrix.json")))!;
            matrix.NationalControlsImmutable = false;
            var matrixPath = Path.Combine(tmp, "agency-selfadmin-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdAgency:EnforceOnStartup"] = "true",
                ["ProdAgency:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdAgencyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("nationalControlsImmutable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_cross_tenant_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod002-xt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdAgencyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-agency", "agency-selfadmin-matrix.json")))!;
            matrix.CrossTenantAdminForbidden = false;
            var matrixPath = Path.Combine(tmp, "agency-selfadmin-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdAgency:EnforceOnStartup"] = "true",
                ["ProdAgency:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdAgencyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("crossTenantAdminForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_selfadmin_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod002-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdAgencyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-agency", "agency-selfadmin-matrix.json")))!;
            matrix.MockSandbox!.LiveSelfAdminForbidden = false;
            var matrixPath = Path.Combine(tmp, "agency-selfadmin-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdAgency:EnforceOnStartup"] = "true",
                ["ProdAgency:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdAgencyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveSelfAdminForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_same_role_sensitive_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod002-fe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdAgencyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-agency", "agency-selfadmin-matrix.json")))!;
            matrix.FourEyesSensitive!.PublisherRoleEqualsReviewerForbidden = false;
            var matrixPath = Path.Combine(tmp, "agency-selfadmin-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdAgency:EnforceOnStartup"] = "true",
                ["ProdAgency:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdAgencyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("publisherRoleEqualsReviewerForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveAgencySelfAdminClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdAgency:EnforceOnStartup"] = "true",
            ["ProdAgency:LiveAgencySelfAdminClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdAgencyStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveAgencySelfAdminClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdAgency:EnforceOnStartup"] = "true",
            ["ProdAgency:LiveAgencySelfAdminClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdAgencyStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IAgencySelfAdminClaimGuard>();
        Assert.Null(guard.EnsureLiveSelfAdminAllowed());
        Assert.NotNull(guard.ValidateDelegatedScope(true, false));
        Assert.NotNull(guard.ValidateNationalControlsImmutable(true));
        Assert.NotNull(guard.ValidateCrossTenantForbidden(false));
        Assert.NotNull(guard.ValidateFourEyesSensitive(true, "a", "a"));
        Assert.NotNull(guard.ValidateRollback(false));
        Assert.NotNull(guard.ValidatePreview(false));
    }

    [Fact]
    public void Upstream_matrices_mark_PROD002_Satisfied()
    {
        using var prod001 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-policy", "policy-platform-matrix.json")));
        var p1 = prod001.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", p1["PROD-002"]);

        using var mig007 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-onboarding", "onboarding-matrix.json")));
        var m7 = mig007.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", m7["PROD-002"]);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod002-agency-selfadmin-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-002-production-agency-selfadmin-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_002_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_002_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
