using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;

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

    private static ClaimsPrincipal BranchManager(Guid? rootOrgUnit = null, bool includeClaim = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, HrManagementRoles.BranchManager) };
        if (includeClaim && rootOrgUnit.HasValue)
            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, rootOrgUnit.Value.ToString()));
        else if (includeClaim && !rootOrgUnit.HasValue)
            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, "bad-value"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, HrManagementRoles.Admin)], "test"));

    private static ClaimsPrincipal Unauthenticated() =>
        new(new ClaimsIdentity());

    // --- Fail-closed: missing claim = 403, not full access ---

    [Fact]
    public async Task Handler_denies_branch_manager_without_scope_claim()
    {
        var handler = new OrgScopeAuthorizationHandler();
        var user = BranchManager(includeClaim: false);

        var context = new AuthorizationHandlerContext(
            [new OrgScopeRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_denies_branch_manager_with_invalid_claim()
    {
        var handler = new OrgScopeAuthorizationHandler();
        var user = BranchManager(rootOrgUnit: null);

        var context = new AuthorizationHandlerContext(
            [new OrgScopeRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Handler_succeeds_for_branch_manager_with_valid_claim()
    {
        var handler = new OrgScopeAuthorizationHandler();
        var user = BranchManager(rootOrgUnit: Guid.NewGuid());

        var context = new AuthorizationHandlerContext(
            [new OrgScopeRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_succeeds_for_admin()
    {
        var handler = new OrgScopeAuthorizationHandler();
        var user = Admin();

        var context = new AuthorizationHandlerContext(
            [new OrgScopeRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_does_not_succeed_for_unauthenticated()
    {
        var handler = new OrgScopeAuthorizationHandler();
        var user = Unauthenticated();

        var context = new AuthorizationHandlerContext(
            [new OrgScopeRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
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
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            ArgumentList = { "-NoProfile", "-File", Path.Combine(root, "scripts", "iam002-validate.ps1"), "-RepoRoot", root },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(30_000);
        Assert.Equal(0, proc.ExitCode);
    }
}
