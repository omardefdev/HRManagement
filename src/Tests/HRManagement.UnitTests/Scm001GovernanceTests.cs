using System.Text.Json;

namespace HRManagement.UnitTests;

public class Scm001GovernanceTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "SCM_001_BRANCH_PROTECTION.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(params string[] parts) =>
        System.IO.File.ReadAllText(Path.Combine(new[] { ResolveRepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void Branch_protection_policy_is_strict()
    {
        using var doc = JsonDocument.Parse(Read("docs", "scm-governance", "branch-protection-main.json"));
        var root = doc.RootElement;
        Assert.Equal("SCM-001", root.GetProperty("card").GetString());
        Assert.False(root.GetProperty("allow_force_pushes").GetBoolean());
        Assert.False(root.GetProperty("allow_deletions").GetBoolean());
        Assert.True(root.GetProperty("enforce_admins").GetBoolean());
        // PLAN-35: a value below policy is allowed only with an exceptionRefs id (scm001-branch-protection-validate.ps1
        // checks that id against docs/qa/governance-exceptions.json; Scm001BranchProtectionExceptionTests).
        var refs = root.TryGetProperty("exceptionRefs", out var r)
            ? r.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : [];
        Assert.True(root.GetProperty("required_signatures").GetBoolean() || refs.Contains("required_signatures"));
        Assert.True(root.GetProperty("required_pull_request_reviews").GetProperty("required_approving_review_count").GetInt32() >= 2
            || refs.Contains("required_pull_request_reviews.required_approving_review_count"));

        var contexts = root.GetProperty("required_status_checks").GetProperty("contexts")
            .EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CI", contexts);
        Assert.Contains("CodeQL", contexts);
        Assert.Contains("Gitleaks", contexts);
    }

    [Fact]
    public void Codeowners_covers_critical_paths()
    {
        var text = Read(".github", "CODEOWNERS");
        Assert.Contains("/.github/", text);
        Assert.Contains("/deploy/", text);
        Assert.Contains("/src/Shared/", text);
        Assert.Contains("/docs/scm-governance/", text);
    }

    [Fact]
    public void Scm001_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/SCM_001_BRANCH_PROTECTION.md",
                     "docs/SCM_001_SIGNING_POLICY.md",
                     "docs/SCM_001_DEVELOPER_IDENTITY.md",
                     "docs/SCM_001_REPO_DISASTER_RECOVERY.md",
                     "docs/scm-governance/offboarding-checklist.md",
                     "scripts/scm001-branch-protection-validate.ps1",
                     "scripts/scm001-repo-backup.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }
}
