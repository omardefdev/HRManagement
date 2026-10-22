# Service Trust Inventory (IAM-004)

Inventory of workload identities, API audiences, scopes, and least-privilege infrastructure accounts.
Secrets are never stored in this file — only environment variable names.

## Keycloak confidential clients (service accounts)

| Workload | ClientId | Env secret | Intended scopes |
|----------|----------|------------|-----------------|
| HR.Core | `svc-hr-core` | `ServiceIdentity__ClientSecret` (hr-core) | `file-storage.references`, `payroll.grades.read` |
| HR.Contracts | `svc-hr-contracts` | (hr-contracts) | `file-storage.references` |
| HR.Health | `svc-hr-health` | (hr-health) | — |
| HR.Movements | `svc-hr-movements` | (hr-movements) | `hr-core.lookup` |
| HR.Training | `svc-hr-training` | (hr-training) | `file-storage.references` |
| Payroll | `svc-payroll` | (payroll) | `hr-core.lookup` |
| IDCard | `svc-idcard` | (idcard) | `file-storage.references` |
| Records.Archive | `svc-records-archive` | (archive) | `file-storage.references` |
| Admin.Logistics | `svc-admin-logistics` | (admin) | `hr-core.lookup`, `hr-core.workflow`, `file-storage.references` |
| IT.Infra | `svc-it-infra` | (it-infra) | `directory.sync` |
| Dev.Analytics | `svc-dev-analytics` | (analytics) | — |
| Compliance | `svc-compliance` | (compliance) | — |
| Auth.Directory | `svc-auth-directory` | (auth-directory) | — |
| File.Storage | `svc-file-storage` | (file-storage) | — |
| Gateway (users) | `hr-gateway-client` | `Keycloak__ClientSecret` | interactive OIDC |

Gateway remains the only interactive OIDC client. Service clients use `client_credentials` only.

## API resource audiences

| Resource API | Audience | Valid callers |
|--------------|----------|---------------|
| Auth.Directory internal sync | `api-auth-directory` | `svc-it-infra`, `svc-hr-core` |
| HR.Core lookup | `api-hr-core` | `svc-payroll`, `svc-hr-movements`, `svc-admin-logistics`, interactive users via gateway |
| File.Storage references | `api-file-storage` | `svc-hr-core`, `svc-hr-contracts`, `svc-hr-training`, `svc-idcard`, `svc-records-archive` |
| Payroll pay grades (HCM-027) | `api-payroll` | `svc-hr-core` |

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

Any other service→service HTTP call is denied by default (no client / no scope).

## Rotation

Rotating `ServiceIdentity__ClientSecret` for one service invalidates only that client's tokens.
Other services continue uninterrupted. See `docs/SERVICE_SECRET_ROTATION.md`.
