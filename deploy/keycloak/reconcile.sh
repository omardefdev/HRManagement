#!/usr/bin/env bash
# PLAN-39 (D-39-2, P-2): keeps an existing Keycloak realm in step with the realm rendered from
# docker/keycloak/realm-manifest.json and deploy/wiring/service-wiring.json (scripts/keycloak/render-realm.ps1), on every
# deploy and idempotently. It replaces PLAN-37's deploy/vps/keycloak-sync-clients.sh.
#
# Runs as the one-shot compose service keycloak-reconcile, from the Keycloak image itself (kcadm.sh is there; there is
# no jq, awk, perl or curl, so the realm comes as the flat plan reconcile-plan.tsv that render-realm.ps1 writes next to
# realm-hrmanagement.json, and kcadm's JSON is read line by line), after keycloak is healthy:
#
#   bash reconcile.sh [--check] [plan]     plan default: /opt/keycloak/data/import/reconcile-plan.tsv
#
# It covers: the named realm security fields; every client scope of the matrix with exactly its audience mappers; every
# client of the plan (the svc-* clients of the matrix, the gateway, directory-sync): existence, flags, optional scopes
# (exactly the plan's, Keycloak's built-in scopes left alone), no non-built-in default scope on a service client, the
# gateway's redirect URIs, web origins and post-logout URIs, and each client's secret from the environment variable the
# plan names. It never reads, creates or changes users, role mappings or groups, and deletes no client or scope.
#
# Secrets: the admin password and the client secrets are read from this container's environment (compose passes them
# from the server's .env). A secret reaches kcadm only through a private temporary file (mode 600) or its prompt on
# stdin, never a command line, and is never printed. Output: client and scope names with the action, then
# "reconcile: N changes" (a second run prints "reconcile: 0 changes"). --check changes nothing and exits 1 when a
# change is due. Any failure exits non-zero, so the applications that wait for this service do not start.
set -euo pipefail
umask 077

CHECK=0
if [ "${1:-}" = "--check" ]; then CHECK=1; shift; fi
PLAN="${1:-${HRM_RECONCILE_PLAN:-/opt/keycloak/data/import/reconcile-plan.tsv}}"
KCADM="${KCADM:-/opt/keycloak/bin/kcadm.sh}"
SERVER="${HRM_KEYCLOAK_URL:-http://keycloak:8080}"
WAIT_SECONDS="${HRM_RECONCILE_WAIT_SECONDS:-300}"
# Keycloak's built-in client scopes carry no service permission; reconcile neither links nor unlinks them on a client.
BUILTIN=" acr address basic email microprofile-jwt offline_access openid organization phone profile role_list roles saml_organization web-origins "
CHANGES=0
T0="$(date +%s)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
export HOME="$TMP"   # kcadm keeps its token in $HOME/.keycloak; the image's home may be read-only

log() { printf '[keycloak-reconcile] %s\n' "$*"; }
die() { printf '[keycloak-reconcile] ERROR: %s\n' "$*" >&2; exit 1; }
# stdin from /dev/null: kcadm would otherwise read the plan that the loops below feed on stdin.
kc() { "$KCADM" "$@" --config "$TMP/kcadm.config" < /dev/null; }

[ -f "$PLAN" ] || die "plan $PLAN not found (render it: scripts/keycloak/render-realm.ps1 writes reconcile-plan.tsv next to the realm)"
REALM="$(grep -E $'^realm\t' "$PLAN" | cut -f2 | head -1)"
[ -n "$REALM" ] || die "plan $PLAN has no realm line"

# change <description> <command...>: counts the change; runs it unless --check.
change() {
  local description="$1"; shift
  CHANGES=$((CHANGES + 1))
  if [ "$CHECK" = "1" ]; then log "would: $description"; return 0; fi
  if ! "$@" > /dev/null 2> "$TMP/kcadm.err"; then sed "s/^/[keycloak-reconcile]   /" "$TMP/kcadm.err" >&2; die "failed: $description"; fi
  log "$description"
}

# kcadm -f with a JSON body written to a private file (the body may hold a secret).
kc_body() { # <body> <kcadm args...>
  local body="$1" f; shift
  f="$(mktemp "$TMP/body.XXXXXX")"
  printf '%s' "$body" > "$f"
  kc "$@" -f "$f"
  rm -f "$f"
}

json_escape() { local v="$1"; v="${v//\\/\\\\}"; v="${v//\"/\\\"}"; printf '%s' "$v"; }

# "[ "a", "b" ]" -> "a b" sorted (Keycloak returns sets in any order).
array_words() { printf '%s' "$1" | { grep -o '"[^"]*"' || true; } | tr -d '"' | sort | tr '\n' ' ' | sed 's/ $//'; }
words_sorted() { [ "$1" = "-" ] && return 0; printf '%s\n' $1 | sort | tr '\n' ' ' | sed 's/ $//'; }
json_array() { [ "$1" = "-" ] && { printf '[]'; return 0; }; local out="" w; for w in $1; do out="$out,\"$(json_escape "$w")\""; done; printf '[%s]' "${out#,}"; }

# ---- 0. admin login (master realm), retried until Keycloak answers ----------------------------------------------------
: "${KEYCLOAK_ADMIN:?KEYCLOAK_ADMIN required}"
: "${KEYCLOAK_ADMIN_PASSWORD:?KEYCLOAK_ADMIN_PASSWORD required}"
deadline=$(( $(date +%s) + WAIT_SECONDS ))
# The password reaches kcadm's prompt on stdin (printf is a builtin); the pipe stays open a moment after it, because
# Keycloak 24's prompt sometimes never returns when EOF follows at once (PLAN-37). Bounded per attempt.
until { printf '%s\n' "$KEYCLOAK_ADMIN_PASSWORD"; sleep 2; } | timeout 60 "$KCADM" config credentials --config "$TMP/kcadm.config" \
        --server "$SERVER" --realm master --user "$KEYCLOAK_ADMIN" > /dev/null 2>&1; do
  [ "$(date +%s)" -lt "$deadline" ] || die "kcadm login to $SERVER failed for ${WAIT_SECONDS} s (KEYCLOAK_ADMIN / KEYCLOAK_ADMIN_PASSWORD)"
  sleep 5
done
log "logged in to $SERVER (realm $REALM, plan $(basename "$PLAN"), check=$CHECK)"
kc get "realms/$REALM" --fields realm > /dev/null 2>&1 || die "realm $REALM does not exist (first boot imports it with --import-realm)"

# ---- 1. named realm security fields -------------------------------------------------------------------------------------
fields="" ; expected="" ; sets=()
while IFS=$'\t' read -r kind name value; do
  [ "$kind" = "security" ] || continue
  fields="$fields,$name"; expected="$expected,$value"; sets+=(-s "$name=$value")
done < "$PLAN"
fields="${fields#,}"; expected="${expected#,}"
current="$(kc get "realms/$REALM" --fields "$fields" --format csv --noquotes)"
if [ "$current" = "$expected" ]; then
  log "realm $REALM: security fields unchanged"
else
  change "realm $REALM: security fields set ($fields)" kc update "realms/$REALM" "${sets[@]}"
fi

# ---- 2. client scopes and their audience mappers ------------------------------------------------------------------------
# One call for every scope with its mappers; the pretty JSON is read line by line (2 spaces: scope, 4: mapper, 6: config).
declare -A SCOPE_ID MAPPERS
read_scopes() {
  SCOPE_ID=(); MAPPERS=()
  local line name="" mid="" mname="" mkind="" maud=""
  # Read into a file first: a failed read must stop the run, not look like an empty realm.
  kc get client-scopes -r "$REALM" --fields 'id,name,protocolMappers(id,name,protocolMapper,config(included.client.audience))' > "$TMP/scopes.json" 2> "$TMP/kcadm.err" \
    || { cat "$TMP/kcadm.err" >&2; die "could not read the client scopes of $REALM"; }
  flush_mapper() {
    if [ -n "$mid" ] && [ "$mkind" = "oidc-audience-mapper" ]; then MAPPERS[$name]="${MAPPERS[$name]:-} $mid|$mname|$maud"; fi
    mid=""; mname=""; mkind=""; maud=""
  }
  while IFS= read -r line; do
    if [[ $line =~ ^\ \ \"id\"\ :\ \"([^\"]*)\" ]]; then flush_mapper; sid="${BASH_REMATCH[1]}"
    elif [[ $line =~ ^\ \ \"name\"\ :\ \"([^\"]*)\" ]]; then name="${BASH_REMATCH[1]}"; SCOPE_ID[$name]="$sid"
    elif [[ $line =~ ^\ \ \ \ \"id\"\ :\ \"([^\"]*)\" ]]; then flush_mapper; mid="${BASH_REMATCH[1]}"
    elif [[ $line =~ ^\ \ \ \ \"name\"\ :\ \"([^\"]*)\" ]]; then mname="${BASH_REMATCH[1]}"
    elif [[ $line =~ ^\ \ \ \ \"protocolMapper\"\ :\ \"([^\"]*)\" ]]; then mkind="${BASH_REMATCH[1]}"
    elif [[ $line =~ ^\ \ \ \ \ \ \"included\.client\.audience\"\ :\ \"([^\"]*)\" ]]; then maud="${BASH_REMATCH[1]}"
    elif [[ $line =~ ^\}, ]] || [[ $line =~ ^\}\ \] ]]; then flush_mapper
    fi
  done < "$TMP/scopes.json"
  flush_mapper
}
read_scopes
created_scope=0
while IFS=$'\t' read -r kind name audiences description; do
  [ "$kind" = "scope" ] || continue
  if [ -z "${SCOPE_ID[$name]:-}" ]; then
    change "scope $name: created" kc_body "{\"name\":\"$(json_escape "$name")\",\"description\":\"$(json_escape "$description")\",\"protocol\":\"openid-connect\",\"attributes\":{\"include.in.token.scope\":\"true\",\"display.on.consent.screen\":\"false\"}}" \
      create client-scopes -r "$REALM"
    created_scope=1
  fi
done < "$PLAN"
if [ "$created_scope" = "1" ] && [ "$CHECK" = "0" ]; then read_scopes; fi
while IFS=$'\t' read -r kind name audiences description; do
  [ "$kind" = "scope" ] || continue
  sid="${SCOPE_ID[$name]:-}"
  if [ -z "$sid" ]; then log "scope $name: (check) not created, mappers not compared"; continue; fi
  have=" "; scope_changed=0
  for entry in ${MAPPERS[$name]:-}; do
    IFS='|' read -r mid mname maud <<< "$entry"
    have="$have$maud "
    case " $audiences " in
      *" $maud "*) ;;
      *) change "scope $name: audience mapper $mname ($maud) removed (not in the matrix)" kc delete "client-scopes/$sid/protocol-mappers/models/$mid" -r "$REALM"; scope_changed=1;;
    esac
  done
  for audience in $audiences; do
    [ "$audience" = "-" ] && continue
    case "$have" in *" $audience "*) continue;; esac
    change "scope $name: audience mapper audience-$audience added" kc_body "{\"name\":\"audience-$audience\",\"protocol\":\"openid-connect\",\"protocolMapper\":\"oidc-audience-mapper\",\"consentRequired\":false,\"config\":{\"included.client.audience\":\"$audience\",\"id.token.claim\":\"false\",\"access.token.claim\":\"true\",\"introspection.token.claim\":\"true\"}}" \
      create "client-scopes/$sid/protocol-mappers/models" -r "$REALM"
    scope_changed=1
  done
  [ "$scope_changed" = "0" ] && log "scope $name: unchanged"
done < "$PLAN"

# ---- 3. clients ------------------------------------------------------------------------------------------------------------
declare -A CF   # CF[clientId|field] = raw JSON value of the field (strings unquoted)
read_clients() {
  CF=()
  local line id="" cid=""
  kc get clients -r "$REALM" --fields 'id,clientId,secret,enabled,publicClient,standardFlowEnabled,implicitFlowEnabled,directAccessGrantsEnabled,serviceAccountsEnabled,fullScopeAllowed,redirectUris,webOrigins,optionalClientScopes,defaultClientScopes,attributes(post.logout.redirect.uris)' \
    > "$TMP/clients.json" 2> "$TMP/kcadm.err" || { cat "$TMP/kcadm.err" >&2; die "could not read the clients of $REALM"; }
  while IFS= read -r line; do
    if [[ $line =~ ^\ \ \"id\"\ :\ \"([^\"]*)\" ]]; then id="${BASH_REMATCH[1]}"; cid=""
    elif [[ $line =~ ^\ \ \"clientId\"\ :\ \"([^\"]*)\" ]]; then cid="${BASH_REMATCH[1]}"; CF[$cid|id]="$id"
    elif [ -n "$cid" ] && [[ $line =~ ^\ \ \"([A-Za-z]+)\"\ :\ \"(.*)\",?$ ]]; then CF[$cid|${BASH_REMATCH[1]}]="${BASH_REMATCH[2]}"
    elif [ -n "$cid" ] && [[ $line =~ ^\ \ \"([A-Za-z]+)\"\ :\ (\[.*\]|true|false|[0-9]+),?$ ]]; then CF[$cid|${BASH_REMATCH[1]}]="${BASH_REMATCH[2]}"
    elif [ -n "$cid" ] && [[ $line =~ ^\ \ \ \ \"post\.logout\.redirect\.uris\"\ :\ \"(.*)\",?$ ]]; then CF[$cid|postLogout]="${BASH_REMATCH[1]}"
    fi
  done < "$TMP/clients.json"
}
read_clients

declare -A PLAN_FLAGS PLAN_URLS
while IFS=$'\t' read -r kind client a b c; do
  case "$kind" in
    flags) PLAN_FLAGS[$client]="$a";;
    urls) PLAN_URLS[$client]="$a"$'\t'"$b"$'\t'"$c";;
  esac
done < "$PLAN"

created_client=0
while IFS=$'\t' read -r kind client ckind secret_env optional defaults; do
  [ "$kind" = "client" ] || continue
  [ -n "${CF[$client|id]:-}" ] && continue
  body="{\"clientId\":\"$client\",\"protocol\":\"openid-connect\""
  for flag in ${PLAN_FLAGS[$client]:-}; do body="$body,\"${flag%%=*}\":${flag#*=}"; done
  change "client $client: created" kc_body "$body}" create clients -r "$REALM"
  created_client=1
done < "$PLAN"
if [ "$created_client" = "1" ] && [ "$CHECK" = "0" ]; then read_clients; fi

while IFS=$'\t' read -r kind client ckind secret_env optional defaults; do
  [ "$kind" = "client" ] || continue
  cid="${CF[$client|id]:-}"
  if [ -z "$cid" ]; then log "client $client: (check) not created yet"; continue; fi
  before=$CHANGES

  # Flags.
  sets=()
  for flag in ${PLAN_FLAGS[$client]:-}; do
    [ "${CF[$client|${flag%%=*}]:-}" = "${flag#*=}" ] || sets+=(-s "$flag")
  done
  if [ "${#sets[@]}" -gt 0 ]; then change "client $client: flags set (${sets[*]//-s /})" kc update "clients/$cid" -r "$REALM" "${sets[@]}"; fi

  # Gateway URLs (redirect URIs, web origins, post-logout URIs).
  if [ -n "${PLAN_URLS[$client]:-}" ]; then
    IFS=$'\t' read -r redirects origins postlogout <<< "${PLAN_URLS[$client]}"
    if [ "$(array_words "${CF[$client|redirectUris]:-[ ]}")" != "$(words_sorted "$redirects")" ] \
      || [ "$(array_words "${CF[$client|webOrigins]:-[ ]}")" != "$(words_sorted "$origins")" ] \
      || [ "${CF[$client|postLogout]:-}" != "$postlogout" ]; then
      change "client $client: redirect URIs, web origins and post-logout URIs set" kc_body \
        "{\"redirectUris\":$(json_array "$redirects"),\"webOrigins\":$(json_array "$origins"),\"attributes\":{\"post.logout.redirect.uris\":\"$(json_escape "$postlogout")\"}}" \
        update "clients/$cid" -r "$REALM"
    fi
  fi

  # Optional scopes: exactly the plan's (built-ins left alone). Default scopes: a service client has none but built-ins;
  # the gateway and directory-sync keep theirs, missing plan defaults are linked.
  current_optional=" $(array_words "${CF[$client|optionalClientScopes]:-[ ]}") "
  current_default=" $(array_words "${CF[$client|defaultClientScopes]:-[ ]}") "
  for scope in $([ "$optional" = "-" ] || echo "$optional"); do
    case "$current_optional" in *" $scope "*) continue;; esac
    sid="${SCOPE_ID[$scope]:-}"
    if [ -z "$sid" ]; then log "client $client: optional scope $scope does not exist (skipped)"; continue; fi
    change "client $client: optional scope $scope linked" kc update "clients/$cid/optional-client-scopes/$sid" -r "$REALM"
  done
  if [ "$ckind" = "service" ]; then
    for scope in $current_optional; do
      case "$BUILTIN" in *" $scope "*) continue;; esac
      case " $optional " in *" $scope "*) continue;; esac
      change "client $client: optional scope $scope removed (not in the matrix)" kc delete "clients/$cid/optional-client-scopes/${SCOPE_ID[$scope]}" -r "$REALM"
    done
    for scope in $current_default; do
      case "$BUILTIN" in *" $scope "*) continue;; esac
      change "client $client: default scope $scope removed (service scopes are optional)" kc delete "clients/$cid/default-client-scopes/${SCOPE_ID[$scope]}" -r "$REALM"
    done
  fi
  for scope in $([ "$defaults" = "-" ] || echo "$defaults"); do
    case "$current_default" in *" $scope "*) continue;; esac
    sid="${SCOPE_ID[$scope]:-}"
    if [ -z "$sid" ]; then log "client $client: default scope $scope does not exist (skipped)"; continue; fi
    change "client $client: default scope $scope linked" kc update "clients/$cid/default-client-scopes/$sid" -r "$REALM"
  done

  # Secret from the environment variable the plan names; compared and set in memory, never printed.
  wanted="${!secret_env:-}"
  if [ -z "$wanted" ]; then
    if [ "$ckind" = "directory-sync" ]; then
      log "client $client: secret left as is ($secret_env not set; directory sync disabled)"
    else
      die "$secret_env is not set in the keycloak-reconcile container (compose passes it from .env)"
    fi
  elif [ "${CF[$client|secret]:-}" != "$wanted" ]; then
    # The JSON value may be escaped; confirm with the raw value before writing.
    raw="$(kc get "clients/$cid/client-secret" -r "$REALM" --fields value --format csv --noquotes)"
    if [ "$raw" != "$wanted" ]; then
      change "client $client: secret set from $secret_env" kc_body "{\"secret\":\"$(json_escape "$wanted")\"}" update "clients/$cid" -r "$REALM"
    fi
  fi
  raw=""; wanted=""

  [ "$CHANGES" = "$before" ] && log "client $client: unchanged"
done < "$PLAN"

for key in "${!CF[@]}"; do
  case "$key" in *"|id") ;; *) continue;; esac
  client="${key%|id}"
  case "$client" in svc-*) grep -qE $'^client\t'"$client"$'\t' "$PLAN" || log "client $client: not in the plan, left as is";; esac
done
unset CF

log "reconcile: $CHANGES changes ($(( $(date +%s) - T0 )) s)"
if [ "$CHECK" = "1" ] && [ "$CHANGES" -gt 0 ]; then exit 1; fi
exit 0
