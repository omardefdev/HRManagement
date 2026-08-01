using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>MOB-001: tests for mobile/PWA performance budget structure.</summary>
public class Mob001MobileTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "mob001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Budget_card_is_mob001_with_pwa_strategy()
    {
        var path = RepoPaths.Resolve("docs", "mob", "performance-budgets.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("MOB-001", root.GetProperty("card").GetString());
        Assert.Equal("mobile-first-pwa", root.GetProperty("pwaDecision").GetProperty("strategy").GetString());
    }

    [Fact]
    public void Budget_defines_network_profiles()
    {
        var path = RepoPaths.Resolve("docs", "mob", "performance-budgets.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var profiles = doc.RootElement.GetProperty("networkProfiles").EnumerateArray().ToList();
        Assert.True(profiles.Count >= 2);

        var ids = profiles.Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Contains("3g-slow", ids);
        Assert.Contains("intermittent", ids);
    }

    [Fact]
    public void Performance_budgets_within_limits()
    {
        var path = RepoPaths.Resolve("docs", "mob", "performance-budgets.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var perf = doc.RootElement.GetProperty("performanceBudgets");

        Assert.True(perf.GetProperty("firstContentfulPaintMs").GetInt32() <= 3000);
        Assert.True(perf.GetProperty("largestContentfulPaintMs").GetInt32() <= 5000);
        Assert.True(perf.GetProperty("timeToInteractiveMs").GetInt32() <= 6000);
    }

    [Fact]
    public void Retry_policy_forbids_double_submit()
    {
        var path = RepoPaths.Resolve("docs", "mob", "performance-budgets.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var retry = doc.RootElement.GetProperty("retryPolicy");

        Assert.True(retry.GetProperty("forbidDoubleSubmit").GetBoolean());
        Assert.True(retry.GetProperty("safeRetryEnabled").GetBoolean());
        Assert.False(string.IsNullOrEmpty(retry.GetProperty("idempotencyKeyHeader").GetString()));
    }

    [Fact]
    public void Secure_cache_forbids_pii_and_tokens()
    {
        var path = RepoPaths.Resolve("docs", "mob", "performance-budgets.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var cache = doc.RootElement.GetProperty("secureCache");

        Assert.True(cache.GetProperty("piiInCacheForbidden").GetBoolean());
        Assert.True(cache.GetProperty("tokenInCacheForbidden").GetBoolean());
        Assert.True(cache.GetProperty("remoteLogoutClearsCache").GetBoolean());
    }

    [Fact]
    public void Resumable_upload_requires_idempotency()
    {
        var path = RepoPaths.Resolve("docs", "mob", "performance-budgets.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var upload = doc.RootElement.GetProperty("resumableUpload");

        Assert.True(upload.GetProperty("enabled").GetBoolean());
        Assert.True(upload.GetProperty("idempotencyKeyRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MOB_001_MOBILE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "mob", "performance-budgets.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mob001-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
