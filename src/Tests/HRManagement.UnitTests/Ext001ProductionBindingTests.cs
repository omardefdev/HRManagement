using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtFederation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext001ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-federation", "assurance-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtFederation:EnforceOnStartup"] = "true",
            ["ExtFederation:LiveFederationClaim"] = "false",
            ["ExtFederation:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-federation", "assurance-matrix.json")
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
        services.AddExtFederation(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtFederation:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtFederationProvider>();
        Assert.Equal("EXT-001", provider.Matrix.Card);
        Assert.True(provider.Matrix.Personas.Count >= 5);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-001" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "national-idp-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-idp-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "phishing-mfa-fleet" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-federation-conformance" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-003" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.AttributeAuthority?.NoNameOnlyLinking == true);
    }

    [Fact]
    public void Startup_fails_when_national_idp_live()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext001-idp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtFederationMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-federation", "assurance-matrix.json")))!;
            matrix.FederationTrust!.NationalIdp!.Live = true;
            var matrixPath = Path.Combine(tmp, "assurance-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtFederation:EnforceOnStartup"] = "true",
                ["ExtFederation:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtFederationStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("live", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_name_only_linking_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext001-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtFederationMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-federation", "assurance-matrix.json")))!;
            matrix.AttributeAuthority!.NoNameOnlyLinking = false;
            var matrixPath = Path.Combine(tmp, "assurance-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtFederation:EnforceOnStartup"] = "true",
                ["ExtFederation:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtFederationStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noNameOnlyLinking", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_high_persona_is_aal1()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext001-aal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtFederationMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-federation", "assurance-matrix.json")))!;
            var hr = matrix.Personas.First(p => p.Id == "hr-approver");
            hr.MinAal = "AAL1";
            var matrixPath = Path.Combine(tmp, "assurance-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtFederation:EnforceOnStartup"] = "true",
                ["ExtFederation:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtFederationStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("AAL1", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_sms_otp_allowed_for_sensitive()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext001-sms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtFederationMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-federation", "assurance-matrix.json")))!;
            matrix.MfaPolicy!.SmsOtpForbiddenForSensitive = false;
            var matrixPath = Path.Combine(tmp, "assurance-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtFederation:EnforceOnStartup"] = "true",
                ["ExtFederation:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtFederationStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("smsOtpForbiddenForSensitive", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveFederationClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtFederation:EnforceOnStartup"] = "true",
            ["ExtFederation:LiveFederationClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtFederationStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveFederationClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtFederation:EnforceOnStartup"] = "true",
            ["ExtFederation:LiveFederationClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtFederationStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IFederationAssuranceClaimGuard>();
        Assert.Null(guard.EnsureLiveFederationAllowed());
        Assert.NotNull(guard.ValidateNameOnlyLink("FullName"));
        Assert.NotNull(guard.ValidateRevocationSla(40, escalated: false));
        Assert.NotNull(guard.ValidateLiveIdpClaim(nationalLive: true, agencyLive: false));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext001-federation-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-001-production-federation-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_001_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
