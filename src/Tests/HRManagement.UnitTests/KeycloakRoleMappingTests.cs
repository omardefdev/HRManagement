using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class KeycloakRoleMappingTests
{
    [Fact]
    public void MapRealmRolesFromAccessToken_adds_realm_roles()
    {
        var token = CreateToken("""{"sub":"user1","realm_access":{"roles":["hr-admin","hr-core"]}}""");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user1")], "Test");
        var principal = new ClaimsPrincipal(identity);

        KeycloakRoleMapping.MapRealmRolesFromAccessToken(principal, token);

        Assert.True(principal.IsInRole(HrManagementRoles.Admin));
        Assert.True(principal.IsInRole(HrManagementRoles.HrCore));
    }

    [Fact]
    public void MapRealmRolesFromAccessToken_maps_multiple_realm_roles()
    {
        var token = CreateToken("""{"sub":"user2","realm_access":{"roles":["payroll","hr-contracts"]}}""");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user2")], "Test");
        var principal = new ClaimsPrincipal(identity);

        KeycloakRoleMapping.MapRealmRolesFromAccessToken(principal, token);

        Assert.True(principal.IsInRole(HrManagementRoles.Payroll));
        Assert.True(principal.IsInRole(HrManagementRoles.HrContracts));
    }

    [Fact]
    public void MapRealmRolesFromAccessToken_ignores_malformed_token()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user1")], "Test");
        var principal = new ClaimsPrincipal(identity);

        KeycloakRoleMapping.MapRealmRolesFromAccessToken(principal, "not-a-jwt");

        Assert.False(principal.IsInRole(HrManagementRoles.Admin));
    }

    private static string CreateToken(string payloadJson)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
