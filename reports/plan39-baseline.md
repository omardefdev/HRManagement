# PLAN-39 baseline (P0)

Taken 2026-11-01T23:13Z–23:27Z on `feature/plan-39-ops-keycloak-dataprotection` (from `main` e31d8bb0, plus the PLAN-38
deferred commit 6287e41a). Long outputs are in the git-ignored `artifacts/plan39/` (`p0-*`).

## 0.2 Prerequisites

| Prerequisite | State |
|---|---|
| `docker-compose.vps.yml`, `dp_*` volumes (PLAN-36) | present: 14 `dp_*` + `gateway_dataprotection_keys` in `docker-compose.production.yml`, every module mounts its own at `/var/hrm/dpkeys` (`x-app-production-env` sets `DataProtection__KeyRingPath`) |
| `hrm-bootstrap`, `init-secrets` (PLAN-38) | present: `deploy/bootstrap/bootstrap.sh` (steps 0–8), `deploy/vps/init-secrets.sh` |

## 0.3 Current acceptance realm

`reports/plan39-realm-acceptance-export.json`: a partial export (`partial-export?exportClients=true&exportGroupsAndRoles=true`,
users not exported) of the realm the acceptance chain builds today: `prepare-keycloak-import.ps1 -Profile Acceptance`
(template + 61 personas) imported into a throwaway Keycloak 24, then `sync-keycloak-dev-realm.ps1` (202 s). Keycloak
masks every client secret as `**********`; checked: no gateway secret from `.env`, no lab secret, no persona password.

Realm settings today: `sslRequired=none`, `bruteForceProtected=false`, no `passwordPolicy`, gateway
`directAccessGrantsEnabled=true`.

## 0.4 Clients and scopes

Sources: `deploy/wiring/service-wiring.json` (matrix), `docker/keycloak/realm-hrmanagement-dev.template.json` (template),
compose (`ServiceIdentity__*`, `Keycloak__AdditionalValidAudiences`), `sync-keycloak-dev-realm.ps1`,
`deploy/vps/keycloak-sync-clients.sh`, the acceptance export.

### Service clients (13 in the matrix)

| clientId | matrix scopes | secretEnv | template | export flags (std/dag) |
|---|---|---|---|---|
| svc-hr-core | file-storage.references, payroll.grades.read, file-storage.service-upload | SVC_HR_CORE_CLIENT_SECRET | yes, no scopes | false/false |
| svc-hr-contracts | hr-core.workflow, hr-core.lookup, file-storage.references | SVC_HR_CONTRACTS_CLIENT_SECRET | yes, no scopes | false/false |
| svc-hr-movements | hr-core.lookup | SVC_HR_MOVEMENTS_CLIENT_SECRET | **no** | false/**true** |
| svc-payroll | hr-core.lookup | SVC_PAYROLL_CLIENT_SECRET | yes, no scopes | false/false |
| svc-idcard | hr-core.workflow, file-storage.references | SVC_IDCARD_CLIENT_SECRET | yes, no scopes | false/false |
| svc-records-archive | hr-core.workflow, file-storage.references | SVC_RECORDS_ARCHIVE_CLIENT_SECRET | yes, no scopes | false/false |
| svc-admin-logistics | hr-core.lookup, hr-core.workflow, file-storage.references | SVC_ADMIN_LOGISTICS_CLIENT_SECRET | yes, scopes linked | false/false |
| svc-it-infra | hr-core.workflow, directory.sync | SVC_IT_INFRA_CLIENT_SECRET | yes, no scopes | false/false |
| svc-compliance | hr-core.lookup, hr-core.workflow, dsar.subject.read, file-storage.references | SVC_COMPLIANCE_CLIENT_SECRET | **no** | false/**true** |
| svc-auth-directory | hr-core.lookup | SVC_AUTH_DIRECTORY_CLIENT_SECRET | yes, no scopes | false/false |
| svc-hr-health | (none) | SVC_HR_HEALTH_CLIENT_SECRET | **no** | false/**true** |
| svc-hr-training | file-storage.references | SVC_HR_TRAINING_CLIENT_SECRET | yes, no scopes | false/false |
| svc-dev-analytics | (none) | SVC_DEV_ANALYTICS_CLIENT_SECRET | **no** | false/**true** |

- `svc-file-storage`: in the template and the export, **not in the matrix** (File.Storage has `serviceIdentity:false`).
  The PLAN-36 VpsDemo render disables it; the manifest drops it (plan §1: it stays only if the matrix keeps it).
- The four clients the dev sync script creates get `directAccessGrantsEnabled=true` (Keycloak's default on create):
  rule 3 of plan §1 is broken in the acceptance realm today. On the server all 13 are `false/false` (PLAN-37 sync).
- Compose: every `ServiceIdentity__ClientId` and `ServiceIdentity__Scope` matches the matrix.
- Template secrets: one placeholder `__SVC_SECRET__` for every service client, replaced by one shared lab value.

### Client scopes (8 S2S in the matrix)

| scope | audience(s) | clients | resource server |
|---|---|---|---|
| hr-core.lookup | api-hr-core | hr-contracts, hr-movements, payroll, admin-logistics, compliance, auth-directory | HR.Core (`ServiceIdentity__ApiAudience: api-hr-core`) |
| hr-core.workflow | api-hr-core | hr-contracts, idcard, records-archive, admin-logistics, it-infra, compliance | HR.Core |
| file-storage.references | api-file-storage | hr-core, hr-contracts, idcard, records-archive, admin-logistics, compliance, hr-training | File.Storage (`api-file-storage`) |
| file-storage.service-upload | api-file-storage | hr-core | File.Storage |
| dsar.subject.read | api-hr-core, api-payroll, api-file-storage | compliance | HR.Core, Payroll (`api-payroll` from appsettings only), File.Storage |
| payroll.grades.read | api-payroll | hr-core | Payroll |
| directory.sync | api-auth-directory | it-infra | Auth.Directory (`api-auth-directory`) |
| evidence.ingest | hr-gateway-client | (none) | the gateway |

Not in the template: file-storage.service-upload, dsar.subject.read, payroll.grades.read, directory.sync,
evidence.ingest (added by the sync scripts only). The plan counts "seven"; the matrix has eight (evidence.ingest has no
client): the realm carries all eight.

Audience mappers use `included.client.audience` with `api-*` names that are not clients in the realm. On the server the
tokens carry them (PLAN-37 AcInt37 passed) — P2's Testcontainers test asserts `aud` per client.

### Other clients and realm content

- `hr-gateway-client`: confidential, code flow, `directAccessGrantsEnabled=true` (template), redirect URIs
  `localhost:5000/5200/6200`, default scopes openid/profile/email/roles, optional offline_access, mappers
  `tenant_id`, `scope_org_unit_id`, `employee_id` (`oidc-usermodel-attribute-mapper`, String, id/access/userinfo) and
  audience mappers `hr-gateway-client`, `account`.
- `directory-sync`: confidential service account (manage-users for Auth.Directory), secret `KEYCLOAK_DIRECTORY_SYNC_SECRET`;
  managed by no sync script.
- Built-in scopes: the template redefines profile, email, roles, web-origins but not openid/offline_access/acr; Keycloak
  creates no default scopes when a realm file lists `clientScopes`, so only the dev sync script repairs them.
- Realm roles: 22 in the template + 6 more from the personas (payroll-reverser, payroll-poster, hr-core-executive,
  hr-core-supervisory, hr-exam-marker, hr-health-clinical).
- User profile: none in the template; the dev sync script adds managed `tenant_id`, `scope_org_unit_id`,
  `acceptance_fixture` (admin edit only), not `employee_id`.
- `realm-hr-services.json` (stale: 10 service clients, 4 mapper-less scopes) and `realm-rel001-staging.json`
  (REL-001 lab): referenced by `docs/rel-staging/parity-matrix.json`, `Rel001StagingParityTests`,
  `testdata/rel001/*`, `scripts/export-keycloak-realm.ps1`, `scripts/rel001-reset-staging.ps1` and docs.

## 0.5 Data Protection

- 15 hosts call `builder.AddHrDataProtection("<name>")` (no direct `AddDataProtection`); names: `HRManagement.Gateway`,
  `HR.Core`, and `<Module>.Web` for the other 13. `AddHrDataProtection` persists only when `DataProtection:KeyRingPath`
  is set, with no validation, no certificate, no logging.
- `CreateProtector` in production code: `HRManagement.PiiField.v1` (PiiFieldEncryptionService, HR.Core only) and
  `HR.Core.CareersSession.v1` (cookie only, a failure means "no session").
- **Only HR.Core stores protected data:** `Employees.NationalId` (`public` schema, varchar(512) in the database), set by
  `EmployeePiiProtectionInterceptor`, prefix `CfDJ8`; a failed unprotect logs Critical and throws on every employee read.
  No index on `NationalId` (unique index on `(TenantId, NationalIdHash)`). Table under tenant RLS (TEN-002C): a role
  without `BYPASSRLS` sees no row unless `app.current_tenant` is set.
- Key-ring paths: production compose sets `/var/hrm/dpkeys` for every module (one named volume each), the gateway
  `/var/hrm/dataprotection-keys`; acceptance shares one volume `acceptance_data_protection` for all services
  (separated by application name); dev sets none; k8s and appsettings set none.
- Start-up order (every service): `Build` → `UseHrProductionMiddleware` → `TryMigrateAsync` → `SeedRunner` → `Run`
  (hosted services, among them `ProductionConfigurationGuard`, start only here). `TryMigrateAsync` is the one shared
  call every host makes before migrations and seeds: the early hook for P-3.

## Server before-picture (read-only, 2026-11-01T23:22Z, tag `7bd85bc3ac9d`)

| Item | State |
|---|---|
| Keycloak's database user | `hrm_admin` (1 connection to `keycloak_db`, `PostgreSQL JDBC Driver`); every application also on `hrm_admin` (demo posture) |
| `keycloak_db` owners | database `hrm_admin`; 92 tables + 193 indexes + 92 row types owned by `hrm_admin`; no sequences, views or functions; schema `public` owned by `pg_database_owner`; role `keycloak` absent; PostgreSQL 16.15; `hrm_admin` is the bootstrap superuser |
| Realm `hrmanagement` | `sslRequired=external`, `bruteForceProtected=true` (failureFactor 10, wait 60 s, max 900 s, permanentLockout false), **no passwordPolicy**; gateway `directAccessGrantsEnabled=false`; 13 matrix clients false/false; `svc-file-storage` disabled; 12 users; master realm `external` |
| Data Protection volumes | all 15 (`hrm_dp_*` ×14, `hrm_gateway_dataprotection_keys`) hold one `key-*.xml` |
| Protected data | 500 of 500 `Employees.NationalId` start with `CfDJ8` |
| **Canary, read-only** | **ok**: the first `CfDJ8…` value decrypts with `hrm_dp_hr_core` and application name `HR.Core` (a throwaway probe in the deployed `hr-core-web` image, network none, ring mounted read-only, automatic key generation disabled, prints ok/failed only). Negative control: the same value with `hrm_dp_payroll` → `failed CryptographicException` |
| Backups | `/opt/hrm/backups/pre-plan38-20261101T175541Z`; 109 GB free |
