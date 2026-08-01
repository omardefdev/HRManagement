# Kubernetes data-plane HA (OPS-002) + backup/PITR (OPS-003)

Namespace **`hrmanagement-data`**: PostgreSQL (CloudNativePG + PgBouncer + ScheduledBackup), RabbitMQ (3-node), Keycloak (ISPN), MinIO (4-node erasure), backup CronJobs.

App HA remains under [`../`](../) (OPS-001). Topology: [`docs/OPS_DATA_HA_TOPOLOGY.md`](../../docs/OPS_DATA_HA_TOPOLOGY.md). Backup: [`docs/OPS_BACKUP_TOPOLOGY.md`](../../docs/OPS_BACKUP_TOPOLOGY.md).

## Layout

```text
deploy/k8s/data/
  base/                 # CNPG Cluster+Pooler+backup, RabbitMQ, Keycloak, MinIO, CronJobs
  overlays/
    staging-data-ha/
    production-data-ha/
```

## Apply order

```bash
bash scripts/apply-cnpg-operator.sh
# Replace Secrets (incl. cnpg-backup-store, hr-postgres-backup, minio-backup-reader)
kubectl kustomize deploy/k8s/data/overlays/production-data-ha | kubectl apply -f -
bash scripts/verify-k8s-data-ha.sh
bash scripts/verify-backup-catalog.sh   # after first backup writes catalog
```

## Notes

- Compose stays single-node for local/dev; `--profile backup` runs postgres + minio sidecars.
- Secondary-region DR drills = **OPS-004**.
- Apps must not use postgres/minioadmin/guest or backup-store credentials.
