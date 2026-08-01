#!/usr/bin/env bash
# OPS-003: CNPG point-in-time recovery drill (documented automation).
# Creates a bootstrap Cluster from object store backup into a drill namespace.
# Requires: kubectl, CNPG operator, valid cnpg-backup-store credentials.
#
# Usage:
#   RECOVERY_TARGET_TIME="2026-07-28 12:00:00+00" \
#   bash scripts/restore-pitr-drill.sh
set -euo pipefail

NS="${DRILL_NS:-hrmanagement-data-drill}"
SOURCE_CLUSTER="${SOURCE_CLUSTER:-hr-postgres}"
DRILL_CLUSTER="${DRILL_CLUSTER:-hr-postgres-pitr-drill}"
TARGET_TIME="${RECOVERY_TARGET_TIME:?RECOVERY_TARGET_TIME required (e.g. 2026-07-28 12:00:00+00)}"

echo "OPS-003 PITR drill"
echo "  namespace: ${NS}"
echo "  source:  ${SOURCE_CLUSTER}"
echo "  drill:   ${DRILL_CLUSTER}"
echo "  target:  ${TARGET_TIME}"

kubectl get ns "${NS}" >/dev/null 2>&1 || kubectl create ns "${NS}"

# Copy backup store secret into drill namespace if missing
if ! kubectl -n "${NS}" get secret cnpg-backup-store >/dev/null 2>&1; then
  kubectl -n hrmanagement-data get secret cnpg-backup-store -o yaml \
    | sed "s/namespace: hrmanagement-data/namespace: ${NS}/" \
    | kubectl apply -f -
fi

cat <<EOF | kubectl apply -f -
apiVersion: postgresql.cnpg.io/v1
kind: Cluster
metadata:
  name: ${DRILL_CLUSTER}
  namespace: ${NS}
  labels:
    app.kubernetes.io/part-of: hrmanagement
    app.kubernetes.io/component: pitr-drill
spec:
  instances: 1
  imageName: ghcr.io/cloudnative-pg/postgresql:16.6
  bootstrap:
    recovery:
      source: ${SOURCE_CLUSTER}
      recoveryTarget:
        targetTime: "${TARGET_TIME}"
  externalClusters:
    - name: ${SOURCE_CLUSTER}
      barmanObjectStore:
        destinationPath: s3://hrm-backups/cnpg
        endpointURL: http://minio.hrmanagement-data.svc.cluster.local:9000
        s3Credentials:
          accessKeyId:
            name: cnpg-backup-store
            key: ACCESS_KEY_ID
          secretAccessKey:
            name: cnpg-backup-store
            key: ACCESS_SECRET_KEY
  storage:
    size: 50Gi
EOF

echo "Waiting for drill cluster to become ready (timeout 30m) ..."
kubectl -n "${NS}" wait --for=condition=Ready "cluster/${DRILL_CLUSTER}" --timeout=1800s \
  || echo "WARN: wait timed out — inspect: kubectl -n ${NS} get cluster,pods"

echo "Record actual RTO in docs/OPS_BACKUP_VERIFICATION.md results table."
echo "Cleanup when done: kubectl delete ns ${NS}"
