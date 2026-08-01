#!/usr/bin/env bash
# Install CloudNativePG operator (pinned). Run once per cluster before data HA apply.
# OPS-002 — see docs/OPS_DATA_HA_TOPOLOGY.md
set -euo pipefail

CNPG_VERSION="${CNPG_VERSION:-1.25.1}"
MANIFEST_URL="https://raw.githubusercontent.com/cloudnative-pg/cloudnative-pg/release-${CNPG_VERSION}/releases/cnpg-${CNPG_VERSION}.yaml"

echo "Applying CloudNativePG operator ${CNPG_VERSION} ..."
kubectl apply --server-side -f "${MANIFEST_URL}"
kubectl -n cnpg-system wait --for=condition=Available deploy/cnpg-controller-manager --timeout=180s
echo "CNPG operator ready."
