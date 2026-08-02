using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-015: tests for Syrian records retention matrix artifacts.</summary>
public class Syr015RecordsTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr015-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Retention_matrix_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-015", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Retention_matrix_has_record_classes()
    {
        var path = RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var classes = doc.RootElement.GetProperty("recordClasses");
        Assert.True(classes.GetArrayLength() >= 10);
    }

    [Fact]
    public void Retention_matrix_blocks_disposition_under_hold()
    {
        var path = RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var hold = doc.RootElement.GetProperty("legalHoldPolicy");
        Assert.True(hold.GetProperty("blocksDisposition").GetBoolean());
    }

    [Fact]
    public void Retention_matrix_rejects_foreign_standard_import()
    {
        var path = RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var disclosure = doc.RootElement.GetProperty("disclosurePolicy");
        Assert.True(disclosure.GetProperty("noForeignStandardImport").GetBoolean());
    }

    [Fact]
    public void Retention_matrix_requires_disposal_certificate()
    {
        var path = RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var disposal = doc.RootElement.GetProperty("disposalPolicy");
        Assert.True(disposal.GetProperty("requiresCertificate").GetBoolean());
    }

    [Fact]
    public void Each_record_class_has_owner_and_legal_basis()
    {
        var path = RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        foreach (var cls in doc.RootElement.GetProperty("recordClasses").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(cls.GetProperty("code").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(cls.GetProperty("owner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(cls.GetProperty("legalBasis").GetString()));
        }
    }

    [Fact]
    public void Doc_exists_and_references_retention()
    {
        var path = RepoPaths.Resolve("docs", "SYR_015_RECORDS_MATRIX.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("SYR-015", content);
        Assert.Contains("legal hold", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Law 12", content);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_015_RECORDS_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "records-retention-matrix.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr015-validate.ps1");
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
