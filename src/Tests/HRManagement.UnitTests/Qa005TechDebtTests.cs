using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>QA-005: tests for technical debt baseline structure.</summary>
public class Qa005TechDebtTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "qa005-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Baseline_card_is_qa005_measured_in_ci()
    {
        var path = RepoPaths.Resolve("docs", "qa", "debt-baseline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("QA-005", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("measuredInCi").GetBoolean());
        Assert.True(root.GetProperty("forbidManualReportOnly").GetBoolean());
    }

    [Fact]
    public void Baseline_defines_core_metrics()
    {
        var path = RepoPaths.Resolve("docs", "qa", "debt-baseline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var metrics = doc.RootElement.GetProperty("metrics").EnumerateArray().ToList();
        Assert.True(metrics.Count >= 5);

        var ids = metrics.Select(m => m.GetProperty("id").GetString()).ToList();
        Assert.Contains("cyclomatic_complexity", ids);
        Assert.Contains("duplication", ids);
        Assert.Contains("circular_dependencies", ids);
        Assert.Contains("blocking_warnings", ids);
    }

    [Fact]
    public void Metrics_enforce_on_diff()
    {
        var path = RepoPaths.Resolve("docs", "qa", "debt-baseline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var metrics = doc.RootElement.GetProperty("metrics").EnumerateArray().ToList();
        Assert.All(metrics, m => Assert.True(m.GetProperty("enforceOnDiff").GetBoolean()));
    }

    [Fact]
    public void Debt_budget_allows_zero_new_debt_without_waiver()
    {
        var path = RepoPaths.Resolve("docs", "qa", "debt-baseline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var budget = doc.RootElement.GetProperty("debtBudget");

        Assert.Equal(0, budget.GetProperty("maxNewDebtPointsPerPr").GetInt32());
        Assert.True(budget.GetProperty("exceptionRequiresSignedWaiver").GetBoolean());
    }

    [Fact]
    public void Architecture_fitness_tests_run_in_ci()
    {
        var path = RepoPaths.Resolve("docs", "qa", "debt-baseline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var fitness = doc.RootElement.GetProperty("architectureFitnessTests");

        Assert.True(fitness.GetProperty("runInCi").GetBoolean());
        Assert.True(fitness.GetProperty("continuousNotOneTime").GetBoolean());
    }

    [Fact]
    public void Shared_kernel_policy_forbids_duplicated_logic()
    {
        var path = RepoPaths.Resolve("docs", "qa", "debt-baseline.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("sharedKernelPolicy");

        Assert.True(policy.GetProperty("forbidDuplicatedBusinessLogic").GetBoolean());
        Assert.True(policy.GetProperty("ownershipDecisionRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "QA_005_TECH_DEBT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "qa", "debt-baseline.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "qa005-validate.ps1");
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
