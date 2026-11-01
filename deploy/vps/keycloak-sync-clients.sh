#!/usr/bin/env bash
# PLAN-37 (P-4): keeps the server's Keycloak realm in step with deploy/wiring/service-wiring.json, idempotently, without
# dropping or re-importing the realm (the demo users and data depend on it).
#
#   bash keycloak-sync-clients.sh [--check]     run from the release directory (remote-deploy.sh calls it after the
#                                               stack is healthy); --check changes nothing and exits 1 when a change is due
#
# For every client scope of the matrix: the scope exists with one audience mapper per audience. For every service
# client: the client exists (confidential, service account, no browser flow), its optional scopes other than Keycloak's
# built-in ones are exactly the matrix scopes, it has no non-built-in default scope, and its secret equals the value
# of its secretEnv variable. All kcadm calls run inside
# the Keycloak container, and the admin password and the client secrets are read there from the container's own
# environment (filled by compose from /opt/hrm/shared/.env), so no secret is passed on a command line on the host or
# printed. Output: client and scope ids with the action taken, and "changes=N"; a second run prints changes=0.
set -euo pipefail
export MSYS_NO_PATHCONV=1

HRM_PROJECT="${HRM_PROJECT:-hrm}"
REALM="${HRM_REALM:-hrmanagement}"
CHECK=0
[ "${1:-}" = "--check" ] && CHECK=1
RELEASE_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
MATRIX="$RELEASE_DIR/deploy/wiring/service-wiring.json"
KCADM=/opt/keycloak/bin/kcadm.sh
CHANGES=0

log() { printf '[keycloak-sync] %s\n' "$*"; }
die() { printf '[keycloak-sync] ERROR: %s\n' "$*" >&2; exit 1; }

[ -f "$MATRIX" ] || die "deploy/wiring/service-wiring.json not found in $RELEASE_DIR"
KC="$(docker ps -q --filter "label=com.docker.compose.project=$HRM_PROJECT" --filter "label=com.docker.compose.service=keycloak" | awk 'NR==1')"
[ -n "$KC" ] || die "keycloak container of project $HRM_PROJECT is not running"

kc() { docker exec "$KC" "$KCADM" "$@"; }
# Writes stdin to a file inside the container and runs kcadm with "-f <file>" appended; the file is removed after.
kc_file() { docker exec -i "$KC" sh -c 'f=$(mktemp); cat > "$f"; "$0" "$@" -f "$f"; rc=$?; rm -f "$f"; exit $rc' "$KCADM" "$@"; }
change() {  # description command...
  CHANGES=$((CHANGES + 1))
  if [ "$CHECK" = "1" ]; then log "would $1"; return 0; fi
  shift; "$@" > /dev/null; log "${DESCRIPTION:-done}"
}

# The admin password reaches kcadm's prompt on stdin inside the container (printf is a shell builtin), never on a
# command line. The pipe stays open a moment after the password: when EOF follows it at once, Keycloak 24's prompt
# sometimes never returns (seen on the server: 2 of 5 logins hung, 0 of 8 with the delay); bounded, three attempts.
docker exec -i "$KC" sh -c 'for attempt in 1 2 3; do
    { printf "%s\n" "$KEYCLOAK_ADMIN_PASSWORD"; sleep 2; } | timeout 60 "$0" config credentials --server http://localhost:8080 --realm master --user "$KEYCLOAK_ADMIN" && exit 0
  done; exit 1' "$KCADM" < /dev/null > /dev/null 2>&1 \
  || die "kcadm login failed (KEYCLOAK_ADMIN / KEYCLOAK_ADMIN_PASSWORD in the keycloak container)"

PLAN="$(perl -MJSON::PP -e 'local $/; open my $f, "<", $ARGV[0] or die "$!"; my $m = decode_json(<$f>);
  for my $n (sort keys %{$m->{scopes}}) { my $s = $m->{scopes}{$n}; print "scope\t$n\t" . join(" ", @{$s->{audiences}}) . "\t$s->{description}\n"; }
  for my $s (@{$m->{services}}) { next unless $s->{serviceIdentity}; print "client\t$s->{clientId}\t$s->{secretEnv}\t" . join(" ", @{$s->{scopes}}) . "\n"; }' "$MATRIX")"
# Keycloak built-in client scopes (no service permission); every other scope on a service client is managed here.
BUILTIN=" acr address basic email microprofile-jwt offline_access organization phone profile role_list roles saml_organization web-origins "

scope_id() { kc get client-scopes -r "$REALM" --fields id,name --format csv --noquotes | awk -F, -v n="$1" '$2==n && !found {print $1; found=1}'; }

# 1. Client scopes and their audience mappers.
while IFS="$(printf '\t')" read -r kind name audiences description; do
  [ "$kind" = "scope" ] || continue
  sid="$(scope_id "$name")"
  if [ -z "$sid" ]; then
    DESCRIPTION="scope $name: created" change "create scope $name" kc_file create client-scopes -r "$REALM" <<JSON
{"name":"$name","description":"$description","protocol":"openid-connect","attributes":{"include.in.token.scope":"true","display.on.consent.screen":"false"}}
JSON
    sid="$(scope_id "$name")"
  else
    log "scope $name: unchanged"
  fi
  [ -n "$sid" ] || { log "scope $name: (check mode) mappers not checked"; continue; }
  mappers=" $(kc get "client-scopes/$sid/protocol-mappers/models" -r "$REALM" --fields name --format csv --noquotes | tr '\n' ' ') "
  for audience in $audiences; do
    case "$mappers" in *" audience-$audience "*) continue;; esac
    DESCRIPTION="scope $name: audience mapper $audience added" change "add audience $audience to scope $name" \
      kc_file create "client-scopes/$sid/protocol-mappers/models" -r "$REALM" <<JSON
{"name":"audience-$audience","protocol":"openid-connect","protocolMapper":"oidc-audience-mapper","consentRequired":false,"config":{"included.client.audience":"$audience","id.token.claim":"false","access.token.claim":"true","introspection.token.claim":"true"}}
JSON
  done
done <<< "$PLAN"

# 2. Service clients: existence and flags, optional scopes, secret.
while IFS="$(printf '\t')" read -r kind client secret_env scopes; do
  [ "$kind" = "client" ] || continue
  cid="$(kc get clients -r "$REALM" -q "clientId=$client" --fields id --format csv --noquotes | awk 'NR==1')"
  if [ -z "$cid" ]; then
    DESCRIPTION="client $client: created" change "create client $client" kc_file create clients -r "$REALM" <<JSON
{"clientId":"$client","enabled":true,"publicClient":false,"standardFlowEnabled":false,"directAccessGrantsEnabled":false,"serviceAccountsEnabled":true,"protocol":"openid-connect","fullScopeAllowed":false}
JSON
    cid="$(kc get clients -r "$REALM" -q "clientId=$client" --fields id --format csv --noquotes | awk 'NR==1')"
    [ -n "$cid" ] || { log "client $client: (check mode) not created yet"; continue; }
  fi

  flags="$(kc get "clients/$cid" -r "$REALM" --fields enabled,publicClient,standardFlowEnabled,serviceAccountsEnabled --format csv --noquotes)"
  if [ "$flags" != "true,false,false,true" ]; then
    DESCRIPTION="client $client: enabled as a confidential service account (was $flags)" change "fix flags of $client ($flags)" \
      kc update "clients/$cid" -r "$REALM" -s enabled=true -s publicClient=false -s standardFlowEnabled=false -s serviceAccountsEnabled=true
  fi

  current=" $(kc get "clients/$cid/optional-client-scopes" -r "$REALM" --fields name --format csv --noquotes | tr '\n' ' ') "
  for scope in $scopes; do
    case "$current" in *" $scope "*) continue;; esac
    sid="$(scope_id "$scope")"
    DESCRIPTION="client $client: optional scope $scope linked" change "link $scope to $client" \
      kc update "clients/$cid/optional-client-scopes/${sid:-missing}" -r "$REALM"
  done
  # Any other optional scope is unlinked, also one that has left the matrix catalog (a scope granted without a call);
  # Keycloak's built-in scopes carry no service permission and are left alone.
  for scope in $current; do
    case "$BUILTIN" in *" $scope "*) continue;; esac
    case " $scopes " in *" $scope "*) continue;; esac
    sid="$(scope_id "$scope")"
    DESCRIPTION="client $client: optional scope $scope removed (not in the matrix)" change "unlink $scope from $client" \
      kc delete "clients/$cid/optional-client-scopes/$sid" -r "$REALM"
  done
  # A default scope is in every token of the client, requested or not: only built-in ones are allowed.
  defaults=" $(kc get "clients/$cid/default-client-scopes" -r "$REALM" --fields name --format csv --noquotes | tr '\n' ' ') "
  for scope in $defaults; do
    case "$BUILTIN" in *" $scope "*) continue;; esac
    sid="$(scope_id "$scope")"
    DESCRIPTION="client $client: default scope $scope removed (service scopes are optional, per the matrix)" change "unlink default $scope from $client" \
      kc delete "clients/$cid/default-client-scopes/$sid" -r "$REALM"
  done

  # The secret is compared and set inside the container, from its own environment variable; nothing is printed. It is
  # written by the shell builtin printf to a private temporary file read by "kcadm update -f" (a plain PUT of {"secret"};
  # "--merge" answers unknown_error on Keycloak 24), so it is never on a process command line.
  state="$(docker exec "$KC" sh -c 'v="$(printenv "$2")"; [ -n "$v" ] || { echo missing; exit 0; }
    cur="$("$0" get "clients/$1/client-secret" -r "$3" --fields value --format csv --noquotes)"
    [ "$cur" = "$v" ] && echo unchanged || echo differs' "$KCADM" "$cid" "$secret_env" "$REALM")"
  case "$state" in
    missing) die "$secret_env is not set in the keycloak container (add it to docker-compose.vps.yml and /opt/hrm/shared/.env)";;
    unchanged) log "client $client: unchanged";;
    *)
      DESCRIPTION="client $client: secret set from $secret_env" change "set the secret of $client from $secret_env" \
        docker exec "$KC" sh -c 'f="$(mktemp)"; v="$(printenv "$2")"; printf "{\"secret\":\"%s\"}" "$v" > "$f"; "$0" update "clients/$1" -r "$3" -f "$f"; rc=$?; rm -f "$f"; exit $rc' "$KCADM" "$cid" "$secret_env" "$REALM";;
  esac
done <<< "$PLAN"

log "changes=$CHANGES"
if [ "$CHECK" = "1" ] && [ "$CHANGES" -gt 0 ]; then exit 1; fi
exit 0
