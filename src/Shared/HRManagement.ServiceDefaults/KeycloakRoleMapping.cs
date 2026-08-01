using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HRManagement.ServiceDefaults;

public static class KeycloakRoleMapping
{
    public static void AttachTo(OpenIdConnectOptions options)
    {
        var previous = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async ctx =>
        {
            var accessToken = ctx.TokenEndpointResponse?.AccessToken;
            MapRealmRolesFromAccessToken(ctx.Principal, accessToken);
            if (previous != null)
                await previous(ctx);
            else
                await Task.CompletedTask;
        };
    }

    public static void MapRealmRolesFromAccessToken(ClaimsPrincipal? principal, string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
            return;

        JwtSecurityToken jwt;
        try
        {
            jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        }
        catch
        {
            return;
        }

        MapRealmRolesFromJwt(principal, jwt);
    }

    public static void MapRealmRolesFromJwt(ClaimsPrincipal? principal, JwtSecurityToken jwt)
    {
        var identity = principal?.Identity as ClaimsIdentity;
        if (identity == null)
            return;

        foreach (var claim in jwt.Claims)
            ProcessClaim(identity, claim.Type, claim.Value);
    }

    public static void MapRealmRolesFromJsonWebToken(ClaimsPrincipal? principal, JsonWebToken jwt)
    {
        var identity = principal?.Identity as ClaimsIdentity;
        if (identity == null)
            return;

        foreach (var claim in jwt.Claims)
            ProcessClaim(identity, claim.Type, claim.Value);
    }

    private static void ProcessClaim(ClaimsIdentity identity, string type, string value)
    {
        if (type == "realm_access")
            AddRolesFromRealmAccessJson(identity, value);
        else if (type == "roles")
            AddSingleRole(identity, value);
        else if (type is "employeeId" or "employee_id")
            AddClaimIfMissing(identity, "employee_id", value);
        else if (type is "scope_org_unit_id")
            AddClaimIfMissing(identity, HrOrgScopeVisibility.ScopeOrgUnitClaimType, value);
        else if (type is "tenant_id")
            AddClaimIfMissing(identity, TenantContext.TenantClaimType, value);
    }

    private static void AddRolesFromRealmAccessJson(ClaimsIdentity identity, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("roles", out var roles))
                return;
            foreach (var role in roles.EnumerateArray())
            {
                var r = role.GetString();
                if (!string.IsNullOrEmpty(r))
                    AddRoleIfMissing(identity, r);
            }
        }
        catch
        {
            // ignore malformed payload
        }
    }

    private static void AddSingleRole(ClaimsIdentity identity, string value)
    {
        if (!string.IsNullOrEmpty(value))
            AddRoleIfMissing(identity, value);
    }

    private static void AddRoleIfMissing(ClaimsIdentity identity, string role)
    {
        if (!identity.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role))
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }

    private static void AddClaimIfMissing(ClaimsIdentity identity, string type, string value)
    {
        if (!string.IsNullOrEmpty(value) && !identity.HasClaim(c => c.Type == type && c.Value == value))
            identity.AddClaim(new Claim(type, value));
    }
}
