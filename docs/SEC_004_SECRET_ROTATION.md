# SEC-004 — Secret rotation and build-context hardening

**Card:** SEC-004 (P0)  
**Scope:** Production fail-closed secrets, MinIO least-privilege, `.dockerignore` tightening, rotation runbooks.

## Production fail-closed

`docker-compose.production.yml` uses `${VAR:?message}` for critical secrets so compose **fails at parse time** when a value is missing:

| Secret | Variable | Notes |
|--------|----------|-------|
| PostgreSQL | `POSTGRES_PASSWORD`, `POSTGRES_USER` | Keycloak DB binding |
| RabbitMQ | `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS` | Shared anchor + per-service overrides |
| MinIO root | `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD` | MinIO server only — not injected into apps |
| MinIO app | `MINIO_FILESTORAGE_ACCESS_KEY`, `MINIO_FILESTORAGE_SECRET_KEY` | `file-storage-web` only |
| Keycloak | `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`, `KEYCLOAK_DIRECTORY_SYNC_SECRET` | Gateway + auth-directory |
| Grafana | `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD` | Overrides observability defaults |
| Service identity | `SVC_*_CLIENT_SECRET` | Each module service |

Local development (`docker-compose.yml`) keeps convenience defaults (`hrminio`, etc.) — **never use those in production**.

## MinIO least privilege

- Only `file-storage-web` receives MinIO application credentials in production.
- No app service references `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` for `FileStorage__AccessKey`.
- Create dedicated MinIO users/policies for file-storage and backup; rotate independently of root.

## Build context

`.dockerignore` excludes:

- `.env`, `.env.*`, `.env.local`
- `*.pfx`, `*.pem`, `*.key`, `*.secret`
- `secrets/`, `credentials/`
- `scripts/`, `docs/`, `src/Tests/` (non-runtime)

Run `scripts/sec004-secret-scan.ps1` before release builds.

## Zero-downtime rotation runbook

### 1. PostgreSQL (per-app role)

1. Create new password in secret manager (`POSTGRES_<SERVICE>_APP_PASSWORD_NEW`).
2. `ALTER ROLE <app_user> PASSWORD '<new>';` on primary.
3. Rolling restart app pods/containers one service at a time with new connection string.
4. Verify `/health/ready` and smoke tests.
5. Revoke old password reference from secret manager after all instances report healthy ≥15 min.
6. Replicate password change to DR standby (streaming replication inherits role passwords).

### 2. RabbitMQ

1. Create/update per-service user with same vhost permissions (see IAM-004 matrix).
2. Update `${RABBITMQ_<SERVICE>_PASS}` in secret manager.
3. Rolling restart consumers/producers; MassTransit reconnects automatically.
4. Disable/delete old credential after queue depth stable.

### 3. Keycloak

1. Rotate client secrets in Keycloak admin (Gateway, directory sync, service accounts).
2. Update `${KEYCLOAK_*_CLIENT_SECRET}` env vars.
3. Rolling restart Gateway → auth-directory → module services.
4. Validate login, token refresh, and service-to-service calls.
5. For signing keys: add new key, wait token TTL + skew, remove old key (see IAM-001).

### 4. MinIO

1. Create new access/secret key for `file-storage` user via `mc admin user` or policy API.
2. Update `MINIO_FILESTORAGE_ACCESS_KEY` / `MINIO_FILESTORAGE_SECRET_KEY`.
3. Rolling restart `file-storage-web` only.
4. Run upload/download integration test.
5. Remove old key from MinIO; rotate root only during maintenance window if required.

### Emergency leak response

1. Assume compromise — rotate affected secret immediately.
2. Run `scripts/sec004-secret-scan.ps1` and Gitleaks (SUP-001).
3. Invalidate active sessions if Keycloak/client secret leaked.
4. Audit object storage and DB access logs for anomalous activity.
5. Document in security incident record.

## Validation

```powershell
powershell -File scripts/sec004-secret-scan.ps1
powershell -File scripts/sec004-validate.ps1
dotnet test --filter Sec004SecretRotationTests
```

## Related

- SEC-001 — network surface
- IAM-004 — RabbitMQ/MinIO per-service credentials
- SUP-001 — Gitleaks in CI
- HARD-001 — MinIO hardening baseline
