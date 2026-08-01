using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>SYR-007: tests for Law 12/2024 data protection compliance artifacts.</summary>
public class Syr007DataProtectionTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr007-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Compliance_matrix_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var content = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);
        Assert.Equal("SYR-007", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Compliance_matrix_has_law_reference()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var law = doc.RootElement.GetProperty("law");
        Assert.False(string.IsNullOrEmpty(law.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrEmpty(law.GetProperty("authority").GetString()));
    }

    [Fact]
    public void Compliance_matrix_defines_roles()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var roles = doc.RootElement.GetProperty("roles");
        Assert.True(roles.TryGetProperty("controller", out _));
        Assert.True(roles.TryGetProperty("processor", out _));
        Assert.True(roles.TryGetProperty("dpo", out _));
    }

    [Fact]
    public void Compliance_matrix_has_legal_bases()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var bases = doc.RootElement.GetProperty("legalBases");
        Assert.True(bases.GetArrayLength() >= 2);
    }

    [Fact]
    public void Compliance_matrix_has_data_subject_rights()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var rights = doc.RootElement.GetProperty("dataSubjectRights");
        Assert.True(rights.GetArrayLength() >= 5);
    }

    [Fact]
    public void Compliance_matrix_has_controls()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var controls = doc.RootElement.GetProperty("complianceControls");
        Assert.True(controls.GetArrayLength() >= 10);
    }

    [Fact]
    public void Compliance_matrix_has_breach_response()
    {
        var path = RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var breach = doc.RootElement.GetProperty("breachResponse");
        Assert.False(string.IsNullOrEmpty(breach.GetProperty("notifyAuthority").GetString()));
    }

    [Fact]
    public void Doc_exists_and_references_law()
    {
        var path = RepoPaths.Resolve("docs", "SYR_007_DATA_PROTECTION.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("SYR-007", content);
        Assert.Contains("Law 12", content);
        Assert.Contains("ROPA", content);
        Assert.Contains("DPO", content);
    }

    [Fact]
    public void No_consent_coercion_documented()
    {
        var path = RepoPaths.Resolve("docs", "SYR_007_DATA_PROTECTION.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("coercion", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_007_DATA_PROTECTION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "law12-2024-compliance-matrix.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr007-validate.ps1");
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
