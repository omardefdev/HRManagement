using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace HRManagement.UnitTests;

/// <summary>UX-001 — static + corpus checks for XSS hardening.</summary>
public sealed class Ux001XssCorpusTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static string EscapeHtml(string? value)
    {
        if (value is null) return string.Empty;
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<img onerror=alert(1)>", "&lt;img onerror=alert(1)&gt;")]
    [InlineData("\"onclick=alert(1)", "&quot;onclick=alert(1)")]
    [InlineData("A & B", "A &amp; B")]
    public void EscapeHtml_neutralizes_xss_payloads(string input, string expected)
    {
        Assert.Equal(expected, EscapeHtml(input));
        Assert.DoesNotContain("<img", EscapeHtml(input), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void First_party_js_has_no_innerHTML_assignments()
    {
        var root = Path.Combine(RepoRoot(), "src", "Shared", "HRManagement.Web.Shared", "wwwroot", "js");
        var files = new[]
        {
            "shared-ui.js",
            "entity-pickers.js",
            "file-upload.js",
            "live-notifications.js",
            "display-names.js"
        };

        foreach (var name in files)
        {
            var path = Path.Combine(root, name);
            Assert.True(System.IO.File.Exists(path), path);
            var text = System.IO.File.ReadAllText(path);
            Assert.False(
                Regex.IsMatch(text, @"\.innerHTML\s*="),
                $"{name} must not assign innerHTML (UX-001)");
        }
    }

    [Fact]
    public void Shared_layout_does_not_reference_public_cdns()
    {
        var layout = Path.Combine(
            RepoRoot(),
            "src", "Shared", "HRManagement.Web.Shared", "Views", "Shared", "_HrSharedLayout.cshtml");
        var text = System.IO.File.ReadAllText(layout);
        Assert.DoesNotContain("cdn.jsdelivr.net", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdnjs.cloudflare.com", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("~/lib/", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Org_tree_mount_helper_is_exported_for_safe_dom()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src", "Shared", "HRManagement.Web.Shared", "wwwroot", "js", "entity-pickers.js");
        var text = System.IO.File.ReadAllText(path);
        Assert.Contains("_mountOrgUnitTreeForTest", text, StringComparison.Ordinal);
        Assert.Contains("createElement", text, StringComparison.Ordinal);
        Assert.DoesNotContain("html += '<li", text, StringComparison.Ordinal);
    }
}
