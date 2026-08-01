#!/usr/bin/env bash
# Export Keycloak realm (without users) for reproducible deploy.
# Usage: ./scripts/export-keycloak-realm.sh [container] [realm]
set -euo pipefail

CONTAINER=${1:-hrm-keycloak}
REALM=${2:-hrmanagement}
OUTPUT="docker/keycloak/realm-${REALM}-export.json"

mkdir -p "$(dirname "$OUTPUT")"

echo "Exporting realm '${REALM}' from container '${CONTAINER}'..."
docker exec "$CONTAINER" mkdir -p /tmp/export
docker exec "$CONTAINER" /opt/keycloak/bin/kc.sh export \
  --dir /tmp/export \
  --realm "$REALM" \
  --users skip

# Keycloak 24 names the file differently depending on version; try common patterns.
if docker exec "$CONTAINER" test -f "/tmp/export/${REALM}-realm.json"; then
  docker cp "$CONTAINER:/tmp/export/${REALM}-realm.json" "$OUTPUT"
elif docker exec "$CONTAINER" test -f "/tmp/export/${REALM}.json"; then
  docker cp "$CONTAINER:/tmp/export/${REALM}.json" "$OUTPUT"
else
  echo "Export finished but realm JSON not found under /tmp/export. Listing:"
  docker exec "$CONTAINER" ls -la /tmp/export || true
  exit 1
fi

echo "Exported to $OUTPUT"
echo "Do not commit client secrets. Review and redact before committing."
