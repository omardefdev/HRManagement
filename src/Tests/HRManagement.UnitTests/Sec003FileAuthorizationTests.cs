using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Files;
using HRManagement.SharedKernel.Files;

namespace HRManagement.UnitTests;

public class Sec003FileAuthorizationTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "SEC_003_FILE_AUTHORIZATION.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static FileAccessGrant MakeGrant(
        Guid? tenant = null,
        FileVisibility visibility = FileVisibility.Private,
        string? granteeId = null,
        bool revoked = false,
        string holderId = "other") => new()
        {
            TenantId = tenant ?? TenantA,
            Module = "HR.Core",
            HolderType = "Employee",
            HolderId = holderId,
            FileReference = "files/test.pdf",
            Visibility = visibility,
            GranteeId = granteeId,
            IsRevoked = revoked
        };

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, HrManagementRoles.Admin)], "test"));

    private static ClaimsPrincipal AuthenticatedUser(string? sub = null, Guid? employeeId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, "hr-employee") };
        if (sub is not null) claims.Add(new("sub", sub));
        if (employeeId.HasValue) claims.Add(new("employee_id", employeeId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal FileStorageService() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, HrManagementRoles.FileStorage)], "test"));

    private readonly FileAccessPolicy _policy = new();

    // --- Cross-tenant denial ---

    [Fact]
    public void Cross_tenant_access_denied()
    {
        var grant = MakeGrant(tenant: TenantA, visibility: FileVisibility.Public);
        var decision = _policy.Evaluate(Admin(), grant, requestTenantId: TenantB);
        Assert.False(decision.Allowed);
        Assert.Contains("Cross-tenant", decision.Reason);
    }

    [Fact]
    public void Cross_tenant_delete_denied()
    {
        var grant = MakeGrant(tenant: TenantA);
        var decision = _policy.EvaluateDelete(Admin(), grant, requestTenantId: TenantB);
        Assert.False(decision.Allowed);
    }

    // --- Revocation ---

    [Fact]
    public void Revoked_grant_denied()
    {
        var grant = MakeGrant(revoked: true, visibility: FileVisibility.Public);
        var decision = _policy.Evaluate(Admin(), grant, requestTenantId: TenantA);
        Assert.False(decision.Allowed);
        Assert.Contains("revoked", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // --- Admin access ---

    [Fact]
    public void Admin_can_access_private_file()
    {
        var grant = MakeGrant(visibility: FileVisibility.Private);
        var decision = _policy.Evaluate(Admin(), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Admin_can_delete()
    {
        var grant = MakeGrant();
        var decision = _policy.EvaluateDelete(Admin(), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    // --- Visibility levels ---

    [Fact]
    public void Public_file_accessible_to_any_authenticated_user()
    {
        var grant = MakeGrant(visibility: FileVisibility.Public);
        var decision = _policy.Evaluate(AuthenticatedUser(), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Tenant_visible_file_accessible_to_tenant_user()
    {
        var grant = MakeGrant(visibility: FileVisibility.Tenant);
        var decision = _policy.Evaluate(AuthenticatedUser(), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Private_file_denied_to_non_owner()
    {
        var grant = MakeGrant(visibility: FileVisibility.Private);
        var decision = _policy.Evaluate(AuthenticatedUser(), grant, requestTenantId: TenantA);
        Assert.False(decision.Allowed);
    }

    // --- Holder match ---

    [Fact]
    public void Holder_can_access_own_private_file()
    {
        var grant = MakeGrant(visibility: FileVisibility.Private, holderId: EmployeeId.ToString());
        var decision = _policy.Evaluate(
            AuthenticatedUser(employeeId: EmployeeId), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    // --- Explicit grantee ---

    [Fact]
    public void Explicit_grantee_can_access()
    {
        var grant = MakeGrant(visibility: FileVisibility.Private, granteeId: "user-123");
        var decision = _policy.Evaluate(AuthenticatedUser(sub: "user-123"), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Wrong_grantee_denied()
    {
        var grant = MakeGrant(visibility: FileVisibility.Private, granteeId: "user-123");
        var decision = _policy.Evaluate(AuthenticatedUser(sub: "user-999"), grant, requestTenantId: TenantA);
        Assert.False(decision.Allowed);
    }

    // --- Delete restrictions ---

    [Fact]
    public void Regular_user_cannot_delete()
    {
        var grant = MakeGrant();
        var decision = _policy.EvaluateDelete(AuthenticatedUser(), grant, requestTenantId: TenantA);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void FileStorage_service_can_delete()
    {
        var grant = MakeGrant();
        var decision = _policy.EvaluateDelete(FileStorageService(), grant, requestTenantId: TenantA);
        Assert.True(decision.Allowed);
    }

    // --- Presigned URL constraint ---

    [Fact]
    public void Presigned_url_max_lifetime_capped_at_300()
    {
        var grant = new FileAccessGrant();
        Assert.True(grant.PresignedUrlMaxLifetimeSeconds <= 300);
    }

    // --- Docs and scripts ---

    [Fact]
    public void Sec003_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "SEC_003_FILE_AUTHORIZATION.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "sec003-validate.ps1")));
    }

    [Fact]
    public void Sec003_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(root, "scripts", "sec003-validate.ps1")}\" -RepoRoot \"{root}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        Assert.True(proc.ExitCode == 0, $"validate failed: {stdout}\n{stderr}");
    }
}
