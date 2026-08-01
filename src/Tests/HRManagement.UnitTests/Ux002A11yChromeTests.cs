using System.IO;
using Xunit;

namespace HRManagement.UnitTests;

/// <summary>UX-002 static checks for shared chrome a11y.</summary>
public sealed class Ux002A11yChromeTests
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

    [Fact]
    public void Shared_layout_has_skip_link_and_main_landmark()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src", "Shared", "HRManagement.Web.Shared", "Views", "Shared", "_HrSharedLayout.cshtml");
        var text = System.IO.File.ReadAllText(path);
        Assert.Contains("hr-skip-link", text, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", text, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"إظهار أو إخفاء القائمة الجانبية\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_defines_focus_visible_and_skip_styles()
    {
        var path = Path.Combine(
            RepoRoot(),
            "src", "Shared", "HRManagement.Web.Shared", "wwwroot", "css", "syrian-theme.css");
        var text = System.IO.File.ReadAllText(path);
        Assert.Contains(":focus-visible", text, StringComparison.Ordinal);
        Assert.Contains(".hr-skip-link", text, StringComparison.Ordinal);
        Assert.Contains("@media print", text, StringComparison.Ordinal);
    }
}
