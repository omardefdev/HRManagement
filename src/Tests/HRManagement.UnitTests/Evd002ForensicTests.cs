using System.Diagnostics;
using System.Text.Json;
using HRManagement.SharedKernel.Evidence;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Evd002ForensicTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "evd002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void ForensicCase_defaults_are_correct()
    {
        var fc = new ForensicCase
        {
            TenantId = Guid.NewGuid(),
            CaseNumber = "FC-2026-001",
            Title = "Suspected unauthorized access",
            LeadInvestigator = "csirt-lead"
        };

        Assert.Equal(ForensicCaseStatus.Open, fc.Status);
        Assert.Equal(ForensicCaseSeverity.Medium, fc.Severity);
        Assert.False(fc.LegalHoldActive);
        Assert.Null(fc.ClosedAt);
        Assert.NotEqual(default, fc.OpenedAt);
    }

    [Fact]
    public void ForensicCase_can_activate_legal_hold()
    {
        var fc = new ForensicCase
        {
            TenantId = Guid.NewGuid(),
            CaseNumber = "FC-2026-002",
            Title = "Data breach investigation",
            Severity = ForensicCaseSeverity.Critical,
            LeadInvestigator = "csirt-lead"
        };

        fc.LegalHoldActive = true;
        fc.Status = ForensicCaseStatus.InProgress;

        Assert.True(fc.LegalHoldActive);
        Assert.Equal(ForensicCaseStatus.InProgress, fc.Status);
    }

    [Fact]
    public void ForensicCase_can_be_closed()
    {
        var fc = new ForensicCase
        {
            TenantId = Guid.NewGuid(),
            CaseNumber = "FC-2026-003",
            Title = "False alarm",
            LeadInvestigator = "analyst-1"
        };

        fc.Status = ForensicCaseStatus.Closed;
        fc.ClosedAt = DateTimeOffset.UtcNow;

        Assert.Equal(ForensicCaseStatus.Closed, fc.Status);
        Assert.NotNull(fc.ClosedAt);
    }

    [Fact]
    public void ForensicCase_severity_enum_covers_all_levels()
    {
        var values = Enum.GetValues<ForensicCaseSeverity>();
        Assert.Contains(ForensicCaseSeverity.Low, values);
        Assert.Contains(ForensicCaseSeverity.Medium, values);
        Assert.Contains(ForensicCaseSeverity.High, values);
        Assert.Contains(ForensicCaseSeverity.Critical, values);
    }

    [Fact]
    public void Custody_chain_model_has_required_schema()
    {
        var path = RepoPaths.Resolve("docs", "evd", "custody-chain-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("EVD-002", root.GetProperty("card").GetString());

        var schema = root.GetProperty("custodyChainSchema");
        Assert.True(schema.TryGetProperty("evidenceItem", out _));
        Assert.True(schema.TryGetProperty("custodyTransfer", out _));

        var transfer = schema.GetProperty("custodyTransfer");
        Assert.True(transfer.TryGetProperty("fromCustodian", out _));
        Assert.True(transfer.TryGetProperty("toCustodian", out _));
        Assert.True(transfer.TryGetProperty("verificationHash", out _));
    }

    [Fact]
    public void Custody_model_severity_levels_have_sla()
    {
        var path = RepoPaths.Resolve("docs", "evd", "custody-chain-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var levels = doc.RootElement.GetProperty("severityLevels").EnumerateArray().ToList();

        Assert.True(levels.Count >= 4);
        foreach (var level in levels)
        {
            Assert.True(level.TryGetProperty("sla", out _));
            Assert.True(level.TryGetProperty("level", out _));
        }
    }

    [Fact]
    public void Legal_hold_overrides_retention()
    {
        var path = RepoPaths.Resolve("docs", "evd", "custody-chain-model.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var retention = doc.RootElement.GetProperty("retentionPolicy");

        Assert.True(retention.GetProperty("legalHoldOverridesRetention").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EVD_002_FORENSIC_READINESS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "evd", "custody-chain-model.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("src", "Shared", "HRManagement.SharedKernel",
            "Evidence", "ForensicCase.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "evd002-validate.ps1");
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
