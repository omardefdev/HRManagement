# HARD-001 — MinIO Hardening Baseline

**Control:** HARD-001 | **Asset:** MinIO (S3-compatible) | **Owner:** Platform Security

## Scope

Object storage for File.Storage service (`minio` / `deploy/k8s/data/base/minio/`).

## Baseline controls

| Control | Requirement | Implementation |
|---|---|---|
| Root credentials | Not injected into app services | IAM-004 — only `file-storage-web` gets app keys |
| Network | Internal only | `data` network; no host ports in production compose |
| Buckets | Least-privilege IAM policies | Separate access keys: `MINIO_FILESTORAGE_*`, backup keys |
| Encryption | SSE-S3 or KMS at rest | Enable server-side encryption in production overlay |
| TLS | TLS in K8s production | Internal mTLS or TLS via service mesh where deployed |
| Versioning | Enabled for compliance buckets | Bucket policy in init / IaC |
| Resource limits | Memory cap | Compose 512M; StatefulSet limits in K8s |
| Container hardening | Mild | `no-new-privileges`; persistent volume required — **no** read-only rootfs |

## Credential separation

| Credential | Used by |
|---|---|
| `MINIO_ROOT_USER/PASSWORD` | MinIO server bootstrap only |
| `MINIO_FILESTORAGE_ACCESS_KEY/SECRET_KEY` | `file-storage-web` only |
| `MINIO_BACKUP_*` | Backup sidecar / cron only |

## Exceptions

See [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md).

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
