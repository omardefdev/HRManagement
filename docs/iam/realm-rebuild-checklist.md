# Keycloak Realm Rebuild Checklist

**Card:** IAM-001  
**Purpose:** Reproduce the `hrmanagement` realm from scratch with identical roles, clients, and policies.

## Prerequisites

- Keycloak 24+ running with admin access
- `scripts/export-keycloak-realm.ps1` or `.sh` available
- TLS certificates installed on Keycloak host

## Rebuild Steps

### 1. Create Realm

- Name: `hrmanagement`
- Display name: `HR Management System`
- Enabled: `true`
- Login settings:
  - User registration: `false`
  - Forgot password: `true`
  - Remember me: `true`
  - Login with email: `true`
  - Brute force protection: `true` (max 5 failures, 30s wait, 5m lockout)

### 2. Realm Signing Keys

- Algorithm: `RS256`
- Key size: 2048-bit minimum (4096 recommended for new deployments)
- Priority: 100 (active key)
- Rotation: export old key as passive before removing

### 3. Create Clients

#### Gateway Client (`hr-gateway-client`)

| Setting | Value |
|---------|-------|
| Client Protocol | openid-connect |
| Access Type | confidential |
| Standard Flow | enabled |
| Direct Access Grants | disabled |
| Service Accounts | disabled |
| Valid Redirect URIs | `https://<PORTAL_HOST>/*` |
| Web Origins | `https://<PORTAL_HOST>` |
| Post-Logout Redirect | `https://<PORTAL_HOST>/` |

#### Service Clients (per module)

| Setting | Value |
|---------|-------|
| Client Protocol | openid-connect |
| Access Type | bearer-only |
| Standard Flow | disabled |
| Service Accounts | enabled (for S2S) |

Service client names: `hr-core-service`, `hr-contracts-service`, `payroll-service`,
`hr-movements-service`, `hr-training-service`, `hr-health-service`, `compliance-service`,
`records-archive-service`, `idcard-service`, `admin-logistics-service`,
`it-infra-service`, `dev-analytics-service`, `auth-directory-service`, `file-storage-service`

### 4. Audience Mappers

For each service client, add a protocol mapper:
- Type: `Audience`
- Included Client Audience: the service client ID
- Add to access token: `true`

### 5. Realm Roles

Create roles matching `HrManagementRoles` constants:
- `hr-admin`
- `hr-officer`
- `hr-manager`
- `payroll-admin`
- `payroll-officer`
- `compliance-officer`
- `it-admin`
- `records-officer`
- `training-coordinator`
- `health-officer`
- `id-card-officer`
- `logistics-officer`
- `analytics-viewer`
- `ess-user` (employee self-service)
- `mss-user` (manager self-service)

### 6. Token Settings

| Setting | Value |
|---------|-------|
| Access Token Lifespan | 300 seconds (5 minutes) |
| Client Login Timeout | 300 seconds |
| Refresh Token Max Reuse | 0 (single use) |
| SSO Session Idle | 1800 seconds (30 minutes) |
| SSO Session Max | 36000 seconds (10 hours) |
| Offline Session Idle | 2592000 seconds (30 days) |

### 7. Scopes

Default client scopes include: `openid`, `profile`, `email`, `offline_access`

### 8. Verification

After rebuild:

```powershell
# Export realm and diff against known-good baseline
scripts/export-keycloak-realm.ps1 -RealmName hrmanagement -OutputPath realm-export.json

# Run validation
scripts/iam001-validate.ps1
```

### 9. Checklist Summary

- [ ] Realm created with correct name and settings
- [ ] Brute force protection enabled
- [ ] RS256 signing key configured
- [ ] Gateway client: confidential, standard flow, correct URIs
- [ ] All 14 service clients: bearer-only with audience mappers
- [ ] All realm roles created
- [ ] Token lifespans configured
- [ ] Default scopes set
- [ ] Export matches baseline
- [ ] `iam001-validate.ps1` passes
