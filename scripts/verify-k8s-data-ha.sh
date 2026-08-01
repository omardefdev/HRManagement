#!/usr/bin/env bash
# OPS-002 helper: inspect data-plane HA posture (read-only).
# Usage: NS=hrmanagement-data bash scripts/verify-k8s-data-ha.sh

set -euo pipefail

NS="${NS:-hrmanagement-data}"
APP_NS="${APP_NS:-hrmanagement}"
fail=0

need() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "ERROR: required command not found: $1" >&2
    exit 2
  fi
}

need kubectl

echo "== namespace: ${NS} =="
kubectl get ns "${NS}" >/dev/null || { echo "missing namespace ${NS}"; exit 1; }

echo ""
echo "== CloudNativePG Cluster =="
if kubectl get crd clusters.postgresql.cnpg.io >/dev/null 2>&1; then
  kubectl get cluster.postgresql.cnpg.io -n "${NS}" -o wide || fail=1
  kubectl get pooler.postgresql.cnpg.io -n "${NS}" -o wide || true
else
  echo "WARN: CNPG CRDs not installed — run scripts/apply-cnpg-operator.sh"
  fail=1
fi

echo ""
echo "== RabbitMQ StatefulSet / pods =="
kubectl get sts rabbitmq -n "${NS}" -o wide || fail=1
kubectl get pods -n "${NS}" -l app.kubernetes.io/name=rabbitmq -o wide || fail=1
kubectl get pdb rabbitmq -n "${NS}" || fail=1

echo ""
echo "== Keycloak =="
kubectl get deploy keycloak -n "${NS}" -o wide || fail=1
kubectl get pods -n "${NS}" -l app.kubernetes.io/name=keycloak -o wide || fail=1

echo ""
echo "== MinIO =="
kubectl get sts minio -n "${NS}" -o wide || fail=1
kubectl get pods -n "${NS}" -l app.kubernetes.io/name=minio -o wide || fail=1
kubectl get job minio-enable-versioning -n "${NS}" || true

echo ""
echo "== Services (expected DNS targets for apps) =="
kubectl get svc -n "${NS}" || fail=1

echo ""
echo "== App ConfigMap data endpoints (${APP_NS}) =="
if kubectl get configmap hr-app-config -n "${APP_NS}" >/dev/null 2>&1; then
  kubectl get configmap hr-app-config -n "${APP_NS}" -o jsonpath='{range $k,$v := .data}{$k}={$v}{"\n"}{end}' \
    | grep -E 'Postgres__|RabbitMQ__|Keycloak__|FileStorage__' || true
else
  echo "WARN: hr-app-config not found in ${APP_NS}"
fi

echo ""
echo "== Superuser leak check (app config should not reference postgres/minioadmin/guest) =="
if kubectl get configmap hr-app-config -n "${APP_NS}" -o yaml 2>/dev/null | grep -Ei 'minioadmin|postgres://postgres:|guest:guest'; then
  echo "ERROR: possible shared superuser reference in app ConfigMap" >&2
  fail=1
else
  echo "OK: no obvious shared superuser strings in hr-app-config"
fi

echo ""
if [ "${fail}" -ne 0 ]; then
  echo "verify-k8s-data-ha: FAILED"
  exit 1
fi
echo "verify-k8s-data-ha: OK (inspect against docs/OPS_DATA_HA_VERIFICATION.md)"
exit 0
