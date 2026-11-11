#!/usr/bin/env bash
# PLAN-45 (P-13): adds one demo user of docker/keycloak/realm-manifest.json to a realm that already exists. Keycloak imports the realm only
# when it is new, and reconcile.sh never touches users, so a user added to the manifest reaches a live realm only this way. Idempotent:
# an existing user keeps its password and only gets the roles it lacks.
#
# Runs inside the Keycloak container, where KEYCLOAK_ADMIN, KEYCLOAK_ADMIN_PASSWORD and DEMO_USERS_PASSWORD already are:
#
#   ssh hrm-vps "docker exec -i hrm-keycloak bash -s -- [--employee-id <guid>] <username> <email> <lastName> <tenantId> <role> [role...]" < scripts/vps/add-demo-user.sh
#
# PLAN-50 (P-13): --employee-id also writes the employee_id attribute, which the realm's mapper puts in the token; an approval
# delegation is matched by it (D-50-2). Without it the user carries tenant_id only, as before.
#
# The live realm's user profile declares no tenant_id, and an admin write of an attribute it does not know is silently dropped; users of the
# first import have it because the import bypasses the profile. So the profile allows unmanaged attributes for this write only, and goes back
# as it was: the stored attribute stays, and the token mapper reads it as it does for the imported users.
#
# As in reconcile.sh, a secret reaches kcadm only through its prompt on stdin or a private file (mode 600), never a command line, and is
# never printed.
set -euo pipefail
umask 077

EMPLOYEE_ID=""
if [ "${1:-}" = "--employee-id" ]; then
    EMPLOYEE_ID="${2:?employee id required after --employee-id}"
    shift 2
    [[ "$EMPLOYEE_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
        || { echo "--employee-id must be a GUID" >&2; exit 2; }
fi

USERNAME="${1:?username required}"
EMAIL="${2:?email required}"
LAST_NAME="${3:?last name required}"
TENANT="${4:?tenant id required}"
shift 4
[ "$#" -gt 0 ] || { echo "at least one role is required" >&2; exit 2; }

: "${KEYCLOAK_ADMIN:?KEYCLOAK_ADMIN required}"
: "${KEYCLOAK_ADMIN_PASSWORD:?KEYCLOAK_ADMIN_PASSWORD required}"
: "${DEMO_USERS_PASSWORD:?DEMO_USERS_PASSWORD required}"

KCADM=/opt/keycloak/bin/kcadm.sh
REALM="${HRM_REALM:-hrmanagement}"
TMP="$(mktemp -d)"
RESTORE_PROFILE=0
restore() {
    if [ "$RESTORE_PROFILE" = "1" ]; then kc update users/profile -r "$REALM" -f "$TMP/profile.json" > /dev/null; fi
    rm -rf "$TMP"
}
trap restore EXIT
export HOME="$TMP"

kc() { "$KCADM" "$@" --config "$TMP/kcadm.config" < /dev/null; }
json_escape() { local v="$1"; v="${v//\\/\\\\}"; v="${v//\"/\\\"}"; printf '%s' "$v"; }

{ printf '%s\n' "$KEYCLOAK_ADMIN_PASSWORD"; sleep 2; } | timeout 60 "$KCADM" config credentials --config "$TMP/kcadm.config" \
    --server http://localhost:8080 --realm master --user "$KEYCLOAK_ADMIN" > /dev/null 2>&1

kc get users/profile -r "$REALM" > "$TMP/profile.json"
if ! grep -q unmanagedAttributePolicy "$TMP/profile.json"; then
    sed '0,/{/s//{ "unmanagedAttributePolicy": "ADMIN_EDIT",/' "$TMP/profile.json" > "$TMP/open.json"
    kc update users/profile -r "$REALM" -f "$TMP/open.json" > /dev/null
    RESTORE_PROFILE=1
fi

if kc get users -r "$REALM" -q "username=$USERNAME" -q exact=true --fields username | grep -q "\"$USERNAME\""; then
    echo "exists: $USERNAME"
else
    body="$TMP/user.json"
    printf '{"username":"%s","email":"%s","firstName":"E2E","lastName":"%s","enabled":true,"emailVerified":true,"attributes":{"tenant_id":["%s"]},"credentials":[{"type":"password","temporary":false,"value":"%s"}]}' \
        "$USERNAME" "$EMAIL" "$LAST_NAME" "$TENANT" "$(json_escape "$DEMO_USERS_PASSWORD")" > "$body"
    kc create users -r "$REALM" -f "$body" > /dev/null 2>&1
    rm -f "$body"
    echo "created: $USERNAME"
fi

id="$(kc get users -r "$REALM" -q "username=$USERNAME" -q exact=true --fields id | grep -o '"id" : "[^"]*"' | cut -d'"' -f4)"
[ -n "$id" ] || { echo "no id for $USERNAME" >&2; exit 1; }
kc update "users/$id" -r "$REALM" -s "attributes.tenant_id=[\"$TENANT\"]" > /dev/null
if [ -n "$EMPLOYEE_ID" ]; then
    kc update "users/$id" -r "$REALM" -s "attributes.employee_id=[\"$EMPLOYEE_ID\"]" > /dev/null
    echo "employee_id: $USERNAME"
fi

for role in "$@"; do
    kc add-roles -r "$REALM" --uusername "$USERNAME" --rolename "$role" > /dev/null
done
echo "roles: $USERNAME: $*"
