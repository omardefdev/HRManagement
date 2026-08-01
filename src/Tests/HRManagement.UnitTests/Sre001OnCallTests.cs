namespace HRManagement.UnitTests;

public class Sre001OnCallTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "ON_CALL_ROSTER.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Oncall_artifacts_define_slas_and_tiers()
    {
        var root = ResolveRepoRoot();
        var roster = System.IO.File.ReadAllText(Path.Combine(root, "docs", "ON_CALL_ROSTER.md"));
        Assert.Contains("Tier-0", roster);
        Assert.Contains("Tier-1", roster);
        Assert.Contains("24x7", roster);

        var esc = System.IO.File.ReadAllText(Path.Combine(root, "docs", "SRE_001_ESCALATION_POLICY.md"));
        Assert.Contains("5 minutes", esc);
        Assert.Contains("10 minutes", esc);

        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "SRE_001_ACCESS_MATRIX.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "SRE_001_DRILL_PLAYBOOK.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "sre001-oncall-validate.ps1")));
    }
}
