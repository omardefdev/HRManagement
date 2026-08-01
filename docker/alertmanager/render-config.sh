#!/usr/bin/env sh
# Optional: render Alertmanager config with ALERTMANAGER_WEBHOOK_URL override.
set -eu
TEMPLATE="${1:-/etc/alertmanager/alertmanager.yml.template}"
OUT="${2:-/tmp/alertmanager.yml}"
URL="${ALERTMANAGER_WEBHOOK_URL:-http://alert-sink:8089/webhook}"
sed "s|__WEBHOOK_URL__|${URL}|g" "${TEMPLATE}" > "${OUT}"
echo "Rendered Alertmanager config -> ${OUT} (webhook=${URL})"
