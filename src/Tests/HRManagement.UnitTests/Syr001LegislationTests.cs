using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Syr001LegislationTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Register_includes_law_50_2004()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legislation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var register = doc.RootElement.GetProperty("register").EnumerateArray().ToList();

        var law50 = register.FirstOrDefault(e =>
            e.GetProperty("number").GetString() == "50/2004");
        Assert.NotEqual(default, law50);
        Assert.Equal("law", law50.GetProperty("type").GetString());
        Assert.Equal("active", law50.GetProperty("status").GetString());
    }

    [Fact]
    public void Register_includes_law_53_2021()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legislation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var register = doc.RootElement.GetProperty("register").EnumerateArray().ToList();

        var law53 = register.FirstOrDefault(e =>
            e.GetProperty("number").GetString() == "53/2021");
        Assert.NotEqual(default, law53);
        Assert.Equal("law", law53.GetProperty("type").GetString());
    }

    [Fact]
    public void All_entries_have_required_fields()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legislation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var register = doc.RootElement.GetProperty("register").EnumerateArray().ToList();

        Assert.True(register.Count >= 2);

        foreach (var entry in register)
        {
            Assert.True(entry.TryGetProperty("legalSourceId", out var id));
            Assert.False(string.IsNullOrWhiteSpace(id.GetString()));

            Assert.True(entry.TryGetProperty("type", out var type));
            Assert.Contains(type.GetString(), new[] { "law", "decree", "regulation", "instruction", "decision" });

            Assert.True(entry.TryGetProperty("number", out _));
            Assert.True(entry.TryGetProperty("effectiveDate", out _));
            Assert.True(entry.TryGetProperty("status", out _));
            Assert.True(entry.TryGetProperty("owner", out _));
        }
    }

    [Fact]
    public void Register_card_is_syr001()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legislation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Law_50_has_key_articles()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legislation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var register = doc.RootElement.GetProperty("register").EnumerateArray().ToList();

        var law50 = register.First(e => e.GetProperty("number").GetString() == "50/2004");
        var articles = law50.GetProperty("articles").GetProperty("keyArticles").EnumerateArray().ToList();

        Assert.True(articles.Count >= 3);
        var articleNumbers = articles.Select(a => a.GetProperty("number").GetInt32()).ToList();
        Assert.Contains(1, articleNumbers);
        Assert.Contains(159, articleNumbers);
    }

    [Fact]
    public void Decree_67_amends_law_50()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legislation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var register = doc.RootElement.GetProperty("register").EnumerateArray().ToList();

        var law50 = register.First(e => e.GetProperty("number").GetString() == "50/2004");
        var amendments = law50.GetProperty("amendedBy").EnumerateArray()
            .Select(a => a.GetString()).ToList();

        Assert.Contains("SYR-DEC-67-2026", amendments);
    }

    [Fact]
    public void Legal_source_binding_doc_covers_requirements()
    {
        var path = RepoPaths.Resolve("docs", "syr", "legal-source-binding.md");
        var content = System.IO.File.ReadAllText(path);

        Assert.Contains("legal_source_id", content);
        Assert.Contains("article", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("effective", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("certified", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No press-source", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conflict", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_001_LEGISLATION_REPOSITORY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "legislation-register.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "legal-source-binding.md")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr001-validate.ps1");
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
