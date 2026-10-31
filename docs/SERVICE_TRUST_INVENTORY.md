# Service Trust Inventory (IAM-004)

Inventory of workload identities, API audiences, scopes, and least-privilege infrastructure accounts.
Secrets are never stored in this file — only environment variable names.

## Keycloak confidential clients (service accounts)

**Moved (PLAN-37):** the client, secret variable, URL, scope and call of every service now live in
[`deploy/wiring/service-wiring.json`](../deploy/wiring/service-wiring.json), rendered as
[SERVICE_WIRING_MATRIX.md](SERVICE_WIRING_MATRIX.md). `ProductionWiringMatrixTests` keeps compose, k8s, Keycloak and
the server allow-lists in step with it. Edit the JSON, not a table here.

Gateway remains the only interactive OIDC client (`hr-gateway-client`, secret `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`).
Service clients use `client_credentials` only.

## API resource audiences

| Resource API | Audience | Valid callers |
|--------------|----------|---------------|
| Auth.Directory internal sync | `api-auth-directory` | `svc-it-infra`, `svc-hr-core` |
| HR.Core lookup | `api-hr-core` | `svc-payroll`, `svc-hr-movements`, `svc-admin-logistics`, interactive users via gateway |
| File.Storage references | `api-file-storage` | `svc-hr-core`, `svc-hr-contracts`, `svc-hr-training`, `svc-idcard`, `svc-records-archive` |
| Payroll pay grades (HCM-027) | `api-payroll` | `svc-hr-core` |
| File.Storage service upload (PLAN-30) | `api-file-storage` | `svc-hr-core` |

Configure on each resource server:

```json
"ServiceIdentity": {
  "ApiAudience": "api-hr-core"
}
```

and/or `Keycloak:AdditionalValidAudiences=api-hr-core`.

## Scopes

| Scope | Endpoint | Policy |
|-------|----------|--------|
| `directory.sync` | `POST /api/internal/directory/sync-employee` | `Service.DirectorySync` |
| `hr-core.lookup` | `/api/lookup/**` (S2S) | `HrCore.Lookup` / `Service.HrCoreLookup` |
| `hr-core.workflow` | `POST /api/workflow-instances` (S2S) | `HrCoreWrite` / service scope |
| `file-storage.references` | `POST/DELETE /api/files/{id}/references` | `Service.FileStorageReferences` |
| `payroll.grades.read` | `GET /api/internal/pay-grades/{gradeCode}?asOf=yyyy-MM-dd` (Payroll) | `Service.PayrollGradeLookup` — service account + scope + client `svc-hr-core` only |
| `file-storage.service-upload` | `POST /api/files/service-upload`, `POST /api/files/service-upload/metadata`, `GET /api/files/service-upload/{fileId}/content` (File.Storage) | `Service.FileStorageServiceUpload` — service account + scope + client `svc-hr-core` only |

### `payroll.grades.read` (HCM-027)

- **Purpose:** HR.Core validates an offer's salary against the grade bounds of the Payroll salary scale active at the proposed
  start date (`PayGradeLookupInternalController`).
- **Tenant:** HR.Core's `ForwardTenantHandler` sends the request tenant in `X-Tenant-ID`; Payroll's `TenantIdMiddleware`
  and EF tenant query filters (plus RLS) scope the read — no tenant bypass. Background callers set the record's tenant
  (`TenantIdFlow`) before calling.
- **Keycloak:** client scope `payroll.grades.read` with audience `api-payroll`, optional scope of `svc-hr-core`
  (`scripts/sync-keycloak-dev-realm.ps1`). HR.Core must request it: `ServiceIdentity__Scope=file-storage.references payroll.grades.read`.
- **Configuration:** `Integration__PayrollBaseUrl` on HR.Core (dev/acceptance compose only). When empty, HR.Core registers
  `UnavailablePayrollGradeLookup` and offers need a salary justification (unverified exception offers).
- **Failure mode:** 5 s timeout; any transport error or non-404 failure is treated as *unavailable*, never as *not found*.

### `file-storage.service-upload` (PLAN-30, FS-030)

- **Purpose:** anonymous candidates cannot call File.Storage, so HR.Core relays their documents with its service identity
  (`FilesServiceUploadController`). Only the `CandidateDocument` constraint profile is accepted (5 MiB; `.pdf`, `.jpg`,
  `.jpeg`, `.png`; magic bytes checked); the file goes through the normal quarantine bucket and scan queue.
- **Ownership:** the upload creates a grant `HolderType=Service`, `HolderId=<caller client id>`, `Module=hr-core`,
  `Visibility=Module`. Metadata and content are served only for files holding a non-revoked Service grant of the calling
  client in the request tenant (the grant's 24 h expiry is not applied); content only when `ScanStatus=Clean`; anything
  else is 404. The new scope grants nothing on `FilesApiController` (`ModuleFileStorage` still needs `file-storage.references`).
- **Tenant:** service tokens carry no `tenant_id`; the caller sends `X-Tenant-ID` (400 when missing).
- **Keycloak:** client scope `file-storage.service-upload` with audience `api-file-storage`, optional scope of `svc-hr-core`
  (`scripts/sync-keycloak-dev-realm.ps1`; not in the realm JSON, like `payroll.grades.read`). HR.Core requests it through
  `ServiceIdentity__Scope` from the wiring matrix (shared compose files and k8s, PLAN-37). Run the sync **before** HR.Core
  requests the scope: Keycloak refuses a token for an unlinked scope (`invalid_scope`), which breaks every HR.Core S2S call.
- **Gateway caller proof:** the route is not exempt, so outside Development the direct call must be signed with
  `GatewayCallerProof.Sign` (as `HrCoreLookupClient` does).

## PostgreSQL roles (per database)

| Service | Database | App role | Migrator role |
|---------|----------|----------|---------------|
| HR.Core | `hr_core_db` | `hr_core_app` | `hr_core_migrator` |
| HR.Contracts | `hr_contracts_db` | `hr_contracts_app` | `hr_contracts_migrator` |
| HR.Health | `hr_health_db` | `hr_health_app` | `hr_health_migrator` |
| HR.Movements | `hr_movements_db` | `hr_movements_app` | `hr_movements_migrator` |
| HR.Training | `hr_training_db` | `hr_training_app` | `hr_training_migrator` |
| Payroll | `payroll_db` | `payroll_app` | `payroll_migrator` |
| IDCard | `idcard_db` | `idcard_app` | `idcard_migrator` |
| Records.Archive | `archive_db` | `archive_app` | `archive_migrator` |
| Admin.Logistics | `admin_db` | `admin_app` | `admin_migrator` |
| IT.Infra | `it_infra_db` | `it_infra_app` | `it_infra_migrator` |
| Dev.Analytics | `analytics_db` | `analytics_app` | `analytics_migrator` |
| Compliance | `compliance_db` | `compliance_app` | `compliance_migrator` |
| Auth.Directory | `auth_directory_db` | `auth_directory_app` | `auth_directory_migrator` |
| File.Storage | `file_storage_db` | `file_storage_app` | `file_storage_migrator` |
| Gateway audit | `gateway_audit_db` | `gateway_audit_app` | `gateway_audit_migrator` |

Env pattern: `POSTGRES_HR_CORE_APP_PASSWORD`, etc. See `docker/init-service-roles.sql`.

## RabbitMQ users

| Service | Username | Configure/write/read |
|---------|----------|----------------------|
| Each service | `mq_<service>` (e.g. `mq_payroll`) | Own `^<service>.*` / MassTransit topology for that host |

Shared `RABBITMQ_DEFAULT_USER` is **admin-only** (bootstrap). Application services must use `RabbitMQ__Username` / `RabbitMQ__Password` per service.

## MinIO

| Principal | Access | Buckets |
|-----------|--------|---------|
| `MINIO_ROOT_*` | Bootstrap / console only | all |
| `MINIO_FILESTORAGE_ACCESS_KEY` | File.Storage app only | `hr-core`, `hr-contracts`, `idcard`, `records-archive`, `file-storage` |

Do **not** inject MinIO keys into unrelated services.

## Allowed S2S HTTP calls (allowlist)

| From | To | Path | Scope |
|------|-----|------|-------|
| IT.Infra | Auth.Directory | `/api/internal/directory/sync-employee` | `directory.sync` |
| Payroll | HR.Core | `/api/lookup/**` | `hr-core.lookup` |
| HR.Core / Contracts / Training / IDCard / Archive | File.Storage (via Gateway) | `/api/files/{id}/references` | `file-storage.references` |
| HR.Core | File.Storage (direct, caller-proof signed) | `/api/files/service-upload/**` | `file-storage.service-upload` |

Any other service→service HTTP call is denied by default (no client / no scope).

## Rotation

Rotating `ServiceIdentity__ClientSecret` for one service invalidates only that client's tokens.
Other services continue uninterrupted. See `docs/SERVICE_SECRET_ROTATION.md`.
