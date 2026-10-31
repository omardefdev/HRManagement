#!/usr/bin/env bash
# PLAN-36 (section 8): self-test of the single host, run on the server (or on the owner's machine for the local
# variant). Prints one JSON document (for EvidenceRecorder) on stdout; human-readable lines go to stderr.
#
#   bash smoke.sh [--target-ip 127.0.0.1] [--cacert <file>] [--allow-staging-ca] [--require-integration-wiring]
#                 [--user hradmin] [--no-login]
#
# Checks: every container healthy and none OOM-killed; a valid certificate for APP_DOMAIN and SSO_DOMAIN (publicly
# trusted, or Caddy's internal root with --cacert, or Let's Encrypt staging with --allow-staging-ca); / redirects to
# https://SSO_DOMAIN and no Location points to localhost; /health*, /metrics, /alive answer 404 and /admin,
# /realms/master 403 through the edge; sign-in with a demo user and 200 for /ui/<slug>/ of the 13 module UIs; only
# 22 and 443 listen on public addresses (server only); days left to nextDue in legislative-watch-stamp.json (warning
# under 30). The demo password is read from the server's .env into a variable, sent to curl on stdin, never printed.
set -uo pipefail
export MSYS_NO_PATHCONV=1

HRM_HOME="${HRM_HOME:-/opt/hrm}"
HRM_PROJECT="${HRM_PROJECT:-hrm}"
ENV_FILE="$HRM_HOME/shared/.env"
TARGET_IP="127.0.0.1"
CACERT=""
ALLOW_STAGING=0
REQUIRE_WIRING=0
DEMO_USER="hradmin"
LOGIN=1
MODULES="hr-core hr-contracts hr-health hr-movements hr-training payroll idcard records-archive admin-logistics it-infra dev-analytics compliance auth-directory"

while [ $# -gt 0 ]; do
  case "$1" in
    --target-ip) TARGET_IP="$2"; shift 2;;
    --cacert) CACERT="$2"; shift 2;;
    --allow-staging-ca) ALLOW_STAGING=1; shift;;
    --require-integration-wiring) REQUIRE_WIRING=1; shift;;
    --user) DEMO_USER="$2"; shift 2;;
    --no-login) LOGIN=0; shift;;
    *) echo "unknown argument $1" >&2; exit 2;;
  esac
done

env_value() {
  awk -v k="$1" -F= '$1==k { v=substr($0, length(k)+2) } END { gsub(/^"|"$/, "", v); printf "%s", v }' "$ENV_FILE"
}

APP="$(env_value APP_DOMAIN)"
SSO="$(env_value SSO_DOMAIN)"
[ -n "$APP" ] && [ -n "$SSO" ] || { echo "APP_DOMAIN/SSO_DOMAIN missing in $ENV_FILE" >&2; exit 2; }

WORK="$(mktemp -d)"
# Git Bash (local variant): curl is the Windows binary and MSYS_NO_PATHCONV keeps /tmp paths untranslated.
command -v cygpath >/dev/null 2>&1 && WORK="$(cygpath -m "$WORK")"
DEVNULL=/dev/null
command -v cygpath >/dev/null 2>&1 && DEVNULL=NUL
trap 'rm -rf "$WORK"' EXIT
RESULTS="$WORK/results"
: > "$RESULTS"
FAILS=0

json_escape() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\000-\037'; }
record() {  # id status detail
  printf '{"id":"%s","status":"%s","detail":"%s"}\n' "$1" "$2" "$(json_escape "$3")" >> "$RESULTS"
  printf '[smoke] %-28s %-18s %s\n' "$1" "$2" "$3" >&2
  [ "$2" = "Fail" ] && FAILS=$((FAILS + 1))
  return 0
}

CURL_TLS=()
# Windows curl (Schannel, local variant only) also checks revocation, which Caddy's internal CA cannot answer; the chain
# and the name are still verified.
curl -V 2>/dev/null | grep -qi schannel && CURL_TLS=(--ssl-no-revoke)
[ -n "$CACERT" ] && CURL_TLS+=(--cacert "$CACERT")
[ "$ALLOW_STAGING" = "1" ] && CURL_TLS=(-k)
curl_edge() {
  curl -sS --max-time 20 --resolve "$APP:443:$TARGET_IP" --resolve "$SSO:443:$TARGET_IP" "${CURL_TLS[@]}" "$@"
}

# 1. Containers.
bad=""; oom=""
for id in $(docker ps -aq --filter "label=com.docker.compose.project=$HRM_PROJECT"); do
  set -- $(docker inspect -f '{{index .Config.Labels "com.docker.compose.service"}} {{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} {{.State.ExitCode}} {{.State.OOMKilled}}' "$id")
  [ "$5" = "true" ] && oom="$oom $1"
  case "$2:$3" in
    running:healthy|running:none) ;;
    exited:none) { [ "$1" = "volume-init" ] && [ "$4" = "0" ]; } || bad="$bad $1($2/exit $4)";;
    *) bad="$bad $1($2/$3)";;
  esac
done
count="$(docker ps -q --filter "label=com.docker.compose.project=$HRM_PROJECT" | wc -l | tr -d ' ')"
if [ -z "$bad" ] && [ "$count" -ge 20 ]; then record containers-healthy Pass "$count running containers healthy"; else record containers-healthy Fail "running=$count not healthy:${bad:- none}"; fi
if [ -z "$oom" ]; then record no-oom-killed Pass "no container OOMKilled"; else record no-oom-killed Fail "OOMKilled:$oom"; fi

# 2. Certificates.
for host in "$APP" "$SSO"; do
  issuer="$(echo | openssl s_client -connect "$TARGET_IP:443" -servername "$host" 2>/dev/null | openssl x509 -noout -issuer -enddate 2>/dev/null | tr '\n' ' ')"
  if [ "$ALLOW_STAGING" = "1" ]; then
    case "$issuer" in
      *"(STAGING)"*) record "tls-$host" Pass "Let's Encrypt staging certificate (accepted with --allow-staging-ca): $issuer";;
      *) record "tls-$host" Fail "--allow-staging-ca accepts only a Let's Encrypt staging certificate; got: ${issuer:-no certificate}";;
    esac
  else
    verify="$(curl -sS --max-time 20 --resolve "$host:443:$TARGET_IP" "${CURL_TLS[@]}" -o "$DEVNULL" -w '%{ssl_verify_result}' "https://$host/" 2>&1)"
    if [ "$verify" = "0" ]; then record "tls-$host" Pass "trusted certificate: $issuer"; else record "tls-$host" Fail "certificate not trusted (verify=$verify): $issuer"; fi
  fi
done

# 3. Redirect to the identity provider, never to localhost.
loc="$(curl_edge -o "$DEVNULL" -w '%{redirect_url}' "https://$APP/")"
chain="$loc"; hops=0
while [ -n "$loc" ] && [ "$hops" -lt 6 ] && [[ "$loc" != https://$SSO/* ]]; do
  case "$loc" in /*) loc="https://$APP$loc";; esac
  loc="$(curl_edge -o "$DEVNULL" -w '%{redirect_url}' "$loc")"; chain="$chain $loc"; hops=$((hops + 1))
done
if [[ "$chain" == *localhost* ]]; then record redirect-to-sso Fail "a Location points to localhost"
elif [[ "$loc" == https://$SSO/realms/hrmanagement/* ]]; then record redirect-to-sso Pass "/ -> https://$SSO/realms/hrmanagement/... ($((hops + 1)) hop(s))"
else record redirect-to-sso Fail "no redirect to https://$SSO (last: ${loc:-none})"; fi

# 4. Operations paths closed at the edge.
for spec in "$APP/health/ready:404" "$APP/health/live:404" "$APP/metrics:404" "$APP/alive:404" "$SSO/admin/:403" "$SSO/realms/master/:403" "$SSO/health/ready:404"; do
  url="https://${spec%:*}"; want="${spec##*:}"
  got="$(curl_edge -o "$DEVNULL" -w '%{http_code}' "$url")"
  if [ "$got" = "$want" ]; then record "edge-${spec%:*}" Pass "$got"; else record "edge-${spec%:*}" Fail "expected $want, got $got"; fi
done

# 5. Sign in with a demo user and open every module UI.
if [ "$LOGIN" = "1" ]; then
  jar="$WORK/jar"; page="$WORK/page.html"
  curl_edge -c "$jar" -b "$jar" -L -o "$page" "https://$APP/ui/hr-core/" || true
  action="$(grep -o 'id="kc-form-login"[^>]*action="[^"]*"' "$page" | sed -e 's/.*action="//' -e 's/"$//' -e 's/&amp;/\&/g')"
  if [ -z "$action" ]; then action="$(grep -o '<form[^>]*action="[^"]*login-actions/authenticate[^"]*"' "$page" | sed -e 's/.*action="//' -e 's/"$//' -e 's/&amp;/\&/g')"; fi
  if [ -z "$action" ]; then
    record login Fail "no Keycloak login form after /ui/hr-core/"
  else
    env_value DEMO_USERS_PASSWORD | curl_edge -c "$jar" -b "$jar" -o "$page" --data-urlencode "username=$DEMO_USER" --data-urlencode "password@-" "$action" || true
    # The gateway uses response_mode=form_post: Keycloak answers with a self-submitting form to /signin-oidc.
    if grep -q "signin-oidc" "$page"; then
      # Keycloak writes this page in upper case on one line (<FORM ... ACTION=...><INPUT TYPE="HIDDEN" NAME=... VALUE=...>).
      post_to="$(grep -io '<form[^>]*action="[^"]*"' "$page" | head -1 | sed -E -e 's/.*[aA][cC][tT][iI][oO][nN]="//' -e 's/"$//' -e 's/&amp;/\&/g')"
      args=()
      while read -r name value; do args+=(--data-urlencode "$name=$value"); done < <(grep -io '<input type="hidden" name="[^"]*" value="[^"]*"' "$page" | sed -E -e 's/.*[nN][aA][mM][eE]="([^"]*)" [vV][aA][lL][uU][eE]="([^"]*)"/\1 \2/' -e 's/&amp;/\&/g')
      curl_edge -c "$jar" -b "$jar" -L -o "$page" "${args[@]}" "$post_to" || true
    fi
    # curl caps its own Cookie header at about 8 KB and silently drops the rest; the gateway's chunked session cookie
    # (tokens saved in the ticket) is larger, so send the jar's cookies for APP_DOMAIN as an explicit header.
    session="$(awk -F'\t' -v h="$APP" '($1 == h || $1 == "#HttpOnly_" h) && NF >= 7 { printf "%s=%s; ", $6, $7 }' "$jar")"
    code="$(curl_edge -H "Cookie: $session" -o "$page" -w '%{http_code}' "https://$APP/")"
    if [ "$code" = "200" ]; then record login Pass "signed in as $DEMO_USER; home 200"; else record login Fail "home after sign-in answered $code"; fi
    ok=0; failed=""
    for m in $MODULES; do
      c="$(curl_edge -H "Cookie: $session" -o "$DEVNULL" -w '%{http_code}' "https://$APP/ui/$m/")"
      if [ "$c" = "200" ]; then ok=$((ok + 1)); else failed="$failed $m=$c"; fi
    done
    if [ -z "$failed" ]; then record module-uis Pass "$ok/13 module UIs answered 200"; else record module-uis Fail "$ok/13 answered 200;$failed"; fi
  fi
fi

# 6. Listening sockets (server only; the local variant checks from Windows in smoke.ps1).
if command -v ss >/dev/null 2>&1 && [ "$(uname -s)" = "Linux" ]; then
  public="$(ss -Htln | awk '{print $4}' | grep -vE '^(127\.|\[::1\]|::1)' | sed -E 's/.*:([0-9]+)$/\1/' | sort -un | tr '\n' ' ')"
  extra=""
  for p in $public; do case "$p" in 22|443) ;; *) extra="$extra $p";; esac; done
  if [ -z "$extra" ]; then record listening-ports Pass "public listeners: ${public}"; else record listening-ports Fail "unexpected public listeners:$extra (all: $public)"; fi
else
  record listening-ports Deferred "checked from the owner's machine (smoke.ps1)"
fi

# 7. Legislative watch stamp (LegalControlGuard stops Production after nextDue).
gw="$(docker ps -q --filter "label=com.docker.compose.project=$HRM_PROJECT" --filter "label=com.docker.compose.service=hr-gateway" | head -1)"
next=""
[ -n "$gw" ] && next="$(docker exec "$gw" cat /app/LegalControl/legislative-watch-stamp.json 2>/dev/null | grep -o '"nextDue"[^,}]*' | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}' | head -1)"
if [ -n "$next" ]; then
  days=$(( ( $(date -u -d "$next" +%s) - $(date -u +%s) ) / 86400 ))
  if [ "$days" -lt 0 ]; then record legislative-watch Fail "nextDue $next has passed: the services stop in Production"
  elif [ "$days" -lt 30 ]; then record legislative-watch Warn "nextDue $next in $days days (renew the stamp: docs/OPS_VPS_SINGLE_HOST.md)"
  else record legislative-watch Pass "nextDue $next in $days days"; fi
else
  record legislative-watch Fail "nextDue not found in the gateway's legislative-watch-stamp.json"
fi

# 8. PLAN-37 integration wiring: reported, not required here.
if [ "$REQUIRE_WIRING" = "1" ]; then
  for item in "hr-core Integration__PayrollBaseUrl/FileStorageBaseUrl and scopes" "Integration__HrCoreBaseUrl for Contracts, IDCard, Archive, IT" "S2S token issue per service client" "candidate document upload to File.Storage (S2S)" "per-service SVC_* secrets for every client"; do
    record "wiring: $item" "Deferred(PLAN-37)" "not required by PLAN-36"
  done
fi

pass=$(grep -c '"status":"Pass"' "$RESULTS"); warn=$(grep -c '"status":"Warn"' "$RESULTS"); deferred=$(grep -c '"status":"Deferred' "$RESULTS")
printf '{"generatedAtUtc":"%s","appDomain":"%s","ssoDomain":"%s","staging":%s,"summary":{"pass":%s,"fail":%s,"warn":%s,"deferred":%s},"checks":[%s]}\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$APP" "$SSO" "$([ "$ALLOW_STAGING" = 1 ] && echo true || echo false)" \
  "$pass" "$FAILS" "$warn" "$deferred" "$(paste -sd, "$RESULTS")"
[ "$FAILS" -eq 0 ]
