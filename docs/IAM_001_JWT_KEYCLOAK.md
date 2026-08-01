# IAM-001 — JWT / Keycloak / TLS Production Hardening

**Card:** IAM-001  
**Status:** Done  
**Priority:** P0  
**Owner:** Identity + Platform

## Overview

This document codifies the JWT validation, Keycloak configuration, and TLS requirements
for production deployment of the HRManagement system. All services must refuse startup
when critical identity configuration is missing or misconfigured.

## Architecture

```
Browser ──HTTPS──▶ Gateway (Cookie+OIDC / JwtBearer policy scheme)
                       │
                       ├──Bearer──▶ HR.Core.Web    (JwtBearer only, BehindGateway=true)
                       ├──Bearer──▶ Payroll.Web    (JwtBearer only)
                       ├──Bearer──▶ HR.Movements   (JwtBearer only)
                       └──Bearer──▶ …14 services   (JwtBearer only)
```

- **Gateway** uses `AddGatewayKeycloakAuthentication` — policy scheme routes Bearer→JwtBearer, else Cookie→OIDC.
- **Modules** use `AddModuleAuthentication` — production enforces `BehindGateway=true` (JwtBearer only); dev allows Cookie+OIDC escape hatch.
- Non-development + `BehindGateway=false` throws `InvalidOperationException` at startup.

## JWT Validation Rules

| Parameter | Required | Value |
|-----------|----------|-------|
| `Keycloak:Authority` | Yes | Must be a valid HTTPS URL in production (e.g. `https://keycloak.hr.gov.sy/realms/hrmanagement`) |
| `Keycloak:ClientId` | Yes | Must match the Keycloak client configured for this service |
| `Keycloak:RequireHttpsMetadata` | Yes (prod) | Must be `true` in production; `false` only in Development |
| `ValidateAudience` | Always | `true` — tokens without matching audience are rejected |
| `ValidateIssuer` | Always | `true` — tokens from unknown issuers are rejected |
| `ClockSkew` | Default | 2 minutes maximum |
| `NameClaimType` | Default | `preferred_username` |
| `RoleClaimType` | Default | `System.Security.Claims.ClaimTypes.Role` |

### Audience Resolution

Audiences are built from (in order):
1. `Keycloak:ClientId`
2. `Keycloak:AdditionalValidAudiences` (semicolon-separated)
3. `ServiceIdentity:ApiAudience` (for resource server scenarios)
4. `account` (Keycloak default)

### Issuer Resolution

Valid issuers include:
1. `Keycloak:Authority` URL
2. `Keycloak:AdditionalValidIssuers` (semicolon-separated)

## Production Configuration Checklist

### Gateway (`appsettings.Production.json`)

- [ ] `Keycloak:Authority` set to production Keycloak URL (HTTPS)
- [ ] `Keycloak:ClientId` set to `hr-gateway-client`
- [ ] `Keycloak:ClientSecret` sourced from secret manager (not in config)
- [ ] `Keycloak:RequireHttpsMetadata` = `true`
- [ ] `HrAuth:CookieSecureAlways` = `true`
- [ ] `HrAuth:PostLogoutRedirectUri` set to production portal URL
- [ ] CORS `AllowedOrigins` restricted to production portal host(s)

### Module Services (`appsettings.Production.json`)

- [ ] `Keycloak:Authority` set to **internal** Keycloak URL (HTTPS)
- [ ] `Keycloak:ClientId` set to service-specific client
- [ ] `HrAuth:BehindGateway` = `true` (default, do not set to false)
- [ ] `Keycloak:RequireHttpsMetadata` = `true`
- [ ] No `ClientSecret` needed (JwtBearer only, no OIDC flow)

### Keycloak Realm

- [ ] Realm `hrmanagement` exists with correct signing algorithm (RS256)
- [ ] Gateway client: confidential, standard flow enabled, valid redirect URIs for production
- [ ] Service clients: bearer-only or service-account, correct audience mappers
- [ ] Realm roles match `HrManagementRoles` constants
- [ ] Token lifespan ≤ 5 minutes for access tokens
- [ ] Refresh token rotation enabled
- [ ] Brute force protection enabled
- [ ] Admin console not exposed to public network

### TLS

- [ ] All inter-service communication over TLS or within trusted network boundary
- [ ] TLS 1.2+ only; TLS 1.0/1.1 disabled
- [ ] Certificates from trusted CA (national CA for SYR deployments)
- [ ] OCSP stapling enabled on load balancer/reverse proxy
- [ ] HSTS header configured on Gateway (`EnableHsts: true`)

## Fail-Closed Guards

### Production Startup Guard (`ModuleAuthenticationExtensions.cs`)

```csharp
if (!environment.IsDevelopment() && !behindGateway)
    throw new InvalidOperationException(
        "HrAuth:BehindGateway must be true outside Development.");
```

### Authority/Audience Missing Guard

If `Keycloak:Authority` is null or empty in production, JwtBearer will fail at metadata
discovery, preventing any authenticated request from succeeding. This is the desired
fail-closed behavior — services without proper authority configuration cannot serve
authenticated traffic.

### Recommended: Explicit Startup Validation

Add to each service's `Program.cs` in production:

```csharp
if (!builder.Environment.IsDevelopment())
{
    var authority = builder.Configuration["Keycloak:Authority"];
    if (string.IsNullOrWhiteSpace(authority) || !authority.StartsWith("https://"))
        throw new InvalidOperationException(
            "Keycloak:Authority must be a valid HTTPS URL in production.");
}
```

## Negative Test Cases

| Scenario | Expected Result |
|----------|----------------|
| Token from different issuer | 401 Unauthorized |
| Token with wrong audience | 401 Unauthorized |
| Expired token | 401 Unauthorized |
| Token with invalid signature | 401 Unauthorized |
| No Authorization header | Redirect to OIDC login (Gateway) / 401 (modules) |
| `BehindGateway=false` in Production | Startup crash |
| Empty `Authority` in Production | Metadata discovery failure → 401 for all requests |

## Key Rotation

1. Generate new signing key pair in Keycloak admin
2. Mark old key as passive (still verifies, no new signing)
3. Wait for `token lifespan + clock skew` (≤ 7 minutes)
4. Remove old key
5. Verify via `scripts/iam001-validate.ps1`

## Related Files

- `ServiceDefaults/KeycloakExtensions.cs` — JWT/OIDC configuration
- `ServiceDefaults/ModuleAuthenticationExtensions.cs` — module auth wiring
- `ServiceDefaults/KeycloakRoleMapping.cs` — realm role → ASP.NET role
- `Gateway/appsettings.Production.json` — production Keycloak config
- `docs/iam/realm-rebuild-checklist.md` — realm IaC rebuild
- `scripts/iam001-validate.ps1` — automated validation
- `Iam001JwtKeycloakTests.cs` — xUnit test coverage
