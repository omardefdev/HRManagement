using System.Diagnostics;
using HRManagement.SharedKernel.Currency;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-004: tests for SyrianCurrencyConverter.</summary>
public class Syr004CurrencyTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr004-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void ConversionRatio_is_100()
    {
        Assert.Equal(100, SyrianCurrencyConverter.ConversionRatio);
    }

    [Fact]
    public void ToNewLira_divides_by_100()
    {
        Assert.Equal(10m, SyrianCurrencyConverter.ToNewLira(1000m));
        Assert.Equal(1m, SyrianCurrencyConverter.ToNewLira(100m));
        Assert.Equal(0.5m, SyrianCurrencyConverter.ToNewLira(50m));
        Assert.Equal(0.01m, SyrianCurrencyConverter.ToNewLira(1m));
    }

    [Fact]
    public void ToOldLira_multiplies_by_100()
    {
        Assert.Equal(1000m, SyrianCurrencyConverter.ToOldLira(10m));
        Assert.Equal(100m, SyrianCurrencyConverter.ToOldLira(1m));
        Assert.Equal(50m, SyrianCurrencyConverter.ToOldLira(0.5m));
    }

    [Fact]
    public void Roundtrip_is_lossless()
    {
        var original = 123456.78m;
        var converted = SyrianCurrencyConverter.ToNewLira(original);
        var restored = SyrianCurrencyConverter.ToOldLira(converted);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void GetEra_returns_OldLira_before_cutover()
    {
        var beforeCutover = new DateTime(2026, 7, 30, 23, 59, 59, DateTimeKind.Utc);
        Assert.Equal(CurrencyEra.OldLira, SyrianCurrencyConverter.GetEra(beforeCutover));
    }

    [Fact]
    public void GetEra_returns_NewLira_on_cutover_date()
    {
        Assert.Equal(CurrencyEra.NewLira, SyrianCurrencyConverter.GetEra(SyrianCurrencyConverter.CutoverDate));
    }

    [Fact]
    public void GetEra_returns_NewLira_after_cutover()
    {
        var afterCutover = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(CurrencyEra.NewLira, SyrianCurrencyConverter.GetEra(afterCutover));
    }

    [Fact]
    public void Normalize_converts_old_to_new()
    {
        var result = SyrianCurrencyConverter.Normalize(1000m, CurrencyEra.OldLira, CurrencyEra.NewLira);
        Assert.Equal(10m, result);
    }

    [Fact]
    public void Normalize_converts_new_to_old()
    {
        var result = SyrianCurrencyConverter.Normalize(10m, CurrencyEra.NewLira, CurrencyEra.OldLira);
        Assert.Equal(1000m, result);
    }

    [Fact]
    public void Normalize_returns_unchanged_when_same_era()
    {
        var result = SyrianCurrencyConverter.Normalize(500m, CurrencyEra.NewLira, CurrencyEra.NewLira);
        Assert.Equal(500m, result);
    }

    [Fact]
    public void RoundOfficial_uses_bankers_rounding()
    {
        Assert.Equal(1.24m, SyrianCurrencyConverter.RoundOfficial(1.235m));
        Assert.Equal(1.24m, SyrianCurrencyConverter.RoundOfficial(1.245m));
        Assert.Equal(1.26m, SyrianCurrencyConverter.RoundOfficial(1.255m));
    }

    [Fact]
    public void Zero_converts_to_zero()
    {
        Assert.Equal(0m, SyrianCurrencyConverter.ToNewLira(0m));
        Assert.Equal(0m, SyrianCurrencyConverter.ToOldLira(0m));
    }

    [Fact]
    public void CurrencyEra_json_exists()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "currency-era.json")));
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_004_CURRENCY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Currency", "SyrianCurrencyConverter.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr004-validate.ps1");
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
