#!/usr/bin/env bash
# OPS-001 helper: inspect HA posture on a live cluster (read-only).
# Usage: NS=hrmanagement bash scripts/verify-k8s-ha.sh

set -euo pipefail

NS="${NS:-hrmanagement}"
fail=0

need() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "ERROR: required command not found: $1" >&2
    exit 2
  fi
}

need kubectl

echo "== namespace: ${NS} =="
kubectl get ns "${NS}" >/dev/null

echo ""
echo "== Deployments (replicas ready) =="
kubectl get deploy -n "${NS}" -o wide || fail=1

echo ""
echo "== Pods (-o wide for node/zone hints) =="
kubectl get pods -n "${NS}" -o wide || fail=1

echo ""
echo "== Gateway pod distribution =="
kubectl get pods -n "${NS}" -l app.kubernetes.io/name=hr-gateway -o custom-columns=NAME:.metadata.name,NODE:.spec.nodeName,PHASE:.status.phase || fail=1

echo ""
echo "== PodDisruptionBudgets =="
kubectl get pdb -n "${NS}" || fail=1

echo ""
echo "== HorizontalPodAutoscalers =="
kubectl get hpa -n "${NS}" || fail=1

echo ""
echo "== Ingress (edge → gateway only expected) =="
kubectl get ingress -n "${NS}" -o wide || fail=1

echo ""
echo "== Services (should be ClusterIP for apps) =="
kubectl get svc -n "${NS}" || fail=1

echo ""
echo "== Migrate jobs (should not have HPA) =="
kubectl get job -n "${NS}" -l app.kubernetes.io/component=migrate 2>/dev/null || true
if kubectl get hpa -n "${NS}" hr-migrate >/dev/null 2>&1; then
  echo "ERROR: HPA found for migrate job — autoscaling must not target one-shot jobs" >&2
  fail=1
else
  echo "OK: no HPA named hr-migrate"
fi

echo ""
echo "== Probe / shutdown env sample (hr-gateway) =="
kubectl get deploy hr-gateway -n "${NS}" -o jsonpath='{range .spec.template.spec.containers[0].env[*]}{.name}={.value}{"\n"}{end}' 2>/dev/null | grep -E 'SHUTDOWN|ASPNETCORE' || true
kubectl get deploy hr-gateway -n "${NS}" -o jsonpath='startup={.spec.template.spec.containers[0].startupProbe.httpGet.path} live={.spec.template.spec.containers[0].livenessProbe.httpGet.path} ready={.spec.template.spec.containers[0].readinessProbe.httpGet.path} grace={.spec.template.spec.terminationGracePeriodSeconds}{"\n"}' || fail=1

echo ""
if [ "${fail}" -ne 0 ]; then
  echo "verify-k8s-ha: FAILED"
  exit 1
fi
echo "verify-k8s-ha: OK (inspect output against docs/OPS_HA_VERIFICATION.md)"
exit 0
