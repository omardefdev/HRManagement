using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class Iam002OrgScopeTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "IAM_002_ORG_SCOPE.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    // --- OrgAccessScope: null != all-access ---

    [Fact]
    public void Denied_scope_rejects_all_access()
    {
        var scope = OrgAccessScope.Denied();
        Assert.True(scope.IsDenied);
        Assert.False(scope.CanAccess(Guid.NewGuid()));
        Assert.False(scope.CanAccess(null));
        Assert.True(scope.MustFilter);
    }

    [Fact]
    public void Restricted_scope_only_allows_listed_ids()
    {
        var allowed = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var scope = OrgAccessScope.Restricted(new HashSet<Guid> { allowed });

        Assert.True(scope.CanAccess(allowed));
        Assert.False(scope.CanAccess(sibling));
        Assert.False(scope.CanAccess(null));
    }

    [Fact]
    public void Unrestricted_scope_allows_everything()
    {
        var scope = OrgAccessScope.Unrestricted();
        Assert.False(scope.IsDenied);
        Assert.False(scope.MustFilter);
        Assert.True(scope.CanAccess(Guid.NewGuid()));
    }

    // --- Docs and scripts exist ---

    [Fact]
    public void Iam002_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "IAM_002_ORG_SCOPE.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "iam002-validate.ps1")));
    }

    [Fact]
    public void Iam002_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        // QA-034: RepoScriptRunner passes -ExecutionPolicy Bypass (Windows) / uses pwsh (Linux) and drains both streams.
        var (exitCode, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(Path.Combine(root, "scripts", "iam002-validate.ps1"));
        Assert.True(exitCode == 0, $"iam002-validate.ps1 failed ({exitCode}):\n{stdout}\n{stderr}");
    }
}
