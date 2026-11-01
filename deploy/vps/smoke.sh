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
# under 30); PLAN-37: the service wiring of deploy/wiring/service-wiring.json (section 8, always required). The demo
# password is read from the server's .env into a variable, sent to curl on stdin, never printed.
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
    # One-shot containers: volume-init and (PLAN-38) hrm-bootstrap must have exited 0.
    exited:none) { { [ "$1" = "volume-init" ] || [ "$1" = "hrm-bootstrap" ]; } && [ "$4" = "0" ]; } || bad="$bad $1($2/exit $4)";;
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
    # The header goes through a 0600 file (curl -H @file), never the command line, which other local users can read.
    session_header="$WORK/session-header"
    ( umask 077; awk -F'	' -v h="$APP" '($1 == h || $1 == "#HttpOnly_" h) && NF >= 7 { printf "%s=%s; ", $6, $7 }' "$jar" | sed 's/^/Cookie: /' > "$session_header" )
    code="$(curl_edge -H "@$session_header" -o "$page" -w '%{http_code}' "https://$APP/")"
    if [ "$code" = "200" ]; then record login Pass "signed in as $DEMO_USER; home 200"; else record login Fail "home after sign-in answered $code"; fi
    ok=0; failed=""
    for m in $MODULES; do
      c="$(curl_edge -H "@$session_header" -o "$DEVNULL" -w '%{http_code}' "https://$APP/ui/$m/")"
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

# 8. PLAN-37 service wiring, mandatory (a missing or wrong item fails the run). The list comes from
#    deploy/wiring/service-wiring.json in this release (the matrix the compose files and k8s are checked against):
#    every URL, client id and scope inside each container; the secret set in the container and in .env (lengths only);
#    a client_credentials token per service client with all its scopes (the token stays inside the container); one
#    real S2S call (HR.Core -> File.Storage service upload, signed caller proof); the legal pack in the three images with
#    the repository's SHA-256 (and the start-up line, while the log still holds it). --require-integration-wiring is
#    accepted for compatibility; the checks always run.
RELEASE_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
MATRIX="$RELEASE_DIR/deploy/wiring/service-wiring.json"
REGIMES="$RELEASE_DIR/docs/syr/service-regimes.json"
cid() { docker ps -q --filter "label=com.docker.compose.project=$HRM_PROJECT" --filter "label=com.docker.compose.service=$1" | head -1; }
env_length() { awk -v k="$1" -F= '$1==k { v=substr($0, length(k)+2) } END { gsub(/^"|"$/, "", v); print length(v) }' "$ENV_FILE"; }
# Inside a service container: request its own token; prints "<http code> <granted scopes>" (never the token). The
# secret goes to curl through a private temporary file (printf is a shell builtin), never on a process command line.
token_probe() {
  docker exec -i "$1" sh -s <<'PROBE'
url="${ServiceIdentity__TokenEndpoint:-${Keycloak__Authority%/}/protocol/openid-connect/token}"
s="$(mktemp)"; trap 'rm -f "$s"' EXIT
printf '%s' "$ServiceIdentity__ClientSecret" > "$s"
set -- --data-urlencode grant_type=client_credentials --data-urlencode "client_id=$ServiceIdentity__ClientId" --data-urlencode "client_secret@$s"
[ -n "$ServiceIdentity__Scope" ] && set -- "$@" --data-urlencode "scope=$ServiceIdentity__Scope"
r="$(curl -s -m 10 -w '\n%{http_code}' "$@" "$url")"
code="$(printf '%s' "$r" | tail -n 1)"
granted="$(printf '%s' "$r" | sed -n 's/.*"scope":"\([^"]*\)".*/\1/p' | head -n 1)"
printf '%s %s\n' "$code" "$granted"
PROBE
}
# Inside hr-core-web: one signed S2S call to File.Storage's service upload; prints the HTTP status only. The proof
# reaches the container on stdin (not "docker exec -e"), and the secret, token and proof reach curl through private
# temporary files, so none of them is on a process command line on the host.
upload_probe() {
  { printf "PROOF='%s'\nPROOF_TS='%s'\nPROOF_PATH='%s'\n" "$2" "$3" "$4"; cat <<'CALL'
url="${ServiceIdentity__TokenEndpoint:-${Keycloak__Authority%/}/protocol/openid-connect/token}"
s="$(mktemp)"; h="$(mktemp)"; trap 'rm -f "$s" "$h"' EXIT
printf '%s' "$ServiceIdentity__ClientSecret" > "$s"
tok="$(curl -s -m 10 --data-urlencode grant_type=client_credentials --data-urlencode "client_id=$ServiceIdentity__ClientId" --data-urlencode "client_secret@$s" --data-urlencode "scope=$ServiceIdentity__Scope" "$url" | sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p')"
printf 'Authorization: Bearer %s\nX-HR-Gateway-Proof: %s\n' "$tok" "$PROOF" > "$h"
curl -s -m 10 -o /dev/null -w '%{http_code}' -H "@$h" -H "X-Tenant-ID: 00000000-0000-4000-8000-000000000001" \
  -H "X-HR-Gateway-Proof-Ts: $PROOF_TS" -H "X-HR-Gateway-Proof-Path: $PROOF_PATH" \
  "${Integration__FileStorageBaseUrl%/}$PROOF_PATH"
CALL
  } | docker exec -i "$1" sh -s
}
WIRING_PERL='local $/; open my $f, "<", $ARGV[0] or die "$!"; my $m = decode_json(<$f>);
for my $s (@{$m->{services}}) { my $c = $s->{composeService};
  print "$c\turl\t$_\t$s->{urls}{$_}\n" for sort keys %{$s->{urls}};
  print "$c\tid\t$s->{clientId}\t$s->{secretEnv}\t" . join(" ", @{$s->{scopes}}) . "\n" if $s->{serviceIdentity};
  print "$c\tpack\t$_\t-\n" for @{$s->{legalPacks}}; }'
if [ ! -f "$MATRIX" ]; then
  record wiring-matrix Fail "deploy/wiring/service-wiring.json is missing from the release"
elif ! WIRING="$(perl -MJSON::PP -e "$WIRING_PERL" "$MATRIX")"; then
  record wiring-matrix Fail "deploy/wiring/service-wiring.json could not be read (perl JSON::PP)"
else
  record wiring-matrix Pass "$(printf '%s\n' "$WIRING" | cut -f1 | sort -u | wc -l | tr -d ' ') services in the matrix"
  TAB="$(printf '\t')"
  for svc in $(printf '%s\n' "$WIRING" | cut -f1 | sort -u); do
    c="$(cid "$svc")"
    if [ -z "$c" ]; then record "wiring-env $svc" Fail "no running container"; continue; fi
    bad=""
    while IFS="$TAB" read -r s kind a b scopes; do
      [ "$s" = "$svc" ] || continue
      case "$kind" in
        url)
          v="$(docker exec "$c" printenv "$a" 2>/dev/null)"
          [ "$v" = "$b" ] || bad="$bad $a='$v'";;
        id)
          v="$(docker exec "$c" printenv ServiceIdentity__ClientId 2>/dev/null)"
          [ "$v" = "$a" ] || bad="$bad ClientId='$v'"
          v="$(docker exec "$c" printenv ServiceIdentity__Scope 2>/dev/null)"
          [ "$(printf '%s\n' $v | sort | tr '\n' ' ')" = "$(printf '%s\n' $scopes | sort | tr '\n' ' ')" ] || bad="$bad Scope='$v'"
          n="$(docker exec "$c" sh -c 'printf %s "$ServiceIdentity__ClientSecret" | wc -c' 2>/dev/null | tr -d ' ')"
          [ "${n:-0}" -ge 16 ] || bad="$bad container-secret-length=${n:-0}"
          [ "$(env_length "$b")" -ge 16 ] || bad="$bad .env:$b-missing-or-short"
          set -- $(token_probe "$c" < /dev/null)
          code="${1:-none}"; [ $# -gt 0 ] && shift; granted=" $* "
          missing=""; for want in $scopes; do case "$granted" in *" $want "*) ;; *) missing="$missing $want";; esac; done
          if [ "$code" = "200" ] && [ -z "$missing" ]; then record "wiring-token $a" Pass "token issued with [${scopes:-no scope}]"
          else record "wiring-token $a" Fail "token HTTP $code; missing scopes:${missing:- none}"; fi;;
        pack)
          want="$(sha256sum "$REGIMES" 2>/dev/null | cut -d' ' -f1)"
          got="$(docker exec "$c" sha256sum /app/seed-packs/syr003-service-regimes-v1/regimes.json 2>/dev/null | cut -d' ' -f1)"
          line="$(docker logs "$c" 2>&1 | grep 'LegalPack loaded' | grep -o 'sha256=[0-9a-f]*' | tail -n 1 | cut -d= -f2)"
          if [ -z "$want" ]; then record "legal-pack $svc" Fail "docs/syr/service-regimes.json is missing from the release"
          elif [ "$got" != "$want" ]; then record "legal-pack $svc" Fail "image pack sha256=${got:-absent}, repository $want"
          elif [ -n "$line" ] && [ "$line" != "$want" ]; then record "legal-pack $svc" Fail "start-up line sha256=$line, repository $want"
          else record "legal-pack $svc" Pass "sha256=$want${line:+ (start-up line matches)}"; fi;;
      esac
    done <<< "$WIRING"
    if [ -z "$bad" ]; then record "wiring-env $svc" Pass "URLs, client id, scopes and secret as in the matrix"; else record "wiring-env $svc" Fail "differs from the matrix:$bad"; fi
  done

  # One real S2S call: HR.Core's candidate upload relay to File.Storage (scope file-storage.service-upload, caller proof).
  core="$(cid hr-core-web)"
  if [ -z "$core" ] || [ "$(env_length GATEWAY_CALLER_PROOF_SECRET)" -lt 32 ]; then
    record wiring-s2s-candidate-upload Fail "hr-core-web not running or GATEWAY_CALLER_PROOF_SECRET missing"
  else
    path="/api/files/service-upload/$(cat /proc/sys/kernel/random/uuid 2>/dev/null || echo 00000000-0000-4000-8000-0000000000ff)/content"
    ts="$(date +%s)"
    # The proof secret is read into this process's environment only; the HMAC is computed here and never printed.
    proof="$(K="$(awk -F= '$1=="GATEWAY_CALLER_PROOF_SECRET" { v=substr($0, 29) } END { gsub(/^"|"$/, "", v); printf "%s", v }' "$ENV_FILE")" \
      TS="$ts" P="$path" perl -MDigest::SHA=hmac_sha256_base64 -e 'my $h = hmac_sha256_base64("$ENV{TS}\nGET\n$ENV{P}", $ENV{K}); $h .= "=" while length($h) % 4; print $h')"
    code="$(upload_probe "$core" "$proof" "$ts" "$path" < /dev/null)"
    # 404: authenticated, authorized and through the caller-proof check; the random file does not exist.
    case "$code" in
      404) record wiring-s2s-candidate-upload Pass "svc-hr-core -> File.Storage service upload: 404 for an unknown file (authorized)";;
      *) record wiring-s2s-candidate-upload Fail "svc-hr-core -> File.Storage service upload answered HTTP ${code:-none} (expected 404 for an unknown file)";;
    esac
  fi
fi

pass=$(grep -c '"status":"Pass"' "$RESULTS"); warn=$(grep -c '"status":"Warn"' "$RESULTS"); deferred=$(grep -c '"status":"Deferred' "$RESULTS")
printf '{"generatedAtUtc":"%s","appDomain":"%s","ssoDomain":"%s","staging":%s,"summary":{"pass":%s,"fail":%s,"warn":%s,"deferred":%s},"checks":[%s]}\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$APP" "$SSO" "$([ "$ALLOW_STAGING" = 1 ] && echo true || echo false)" \
  "$pass" "$FAILS" "$warn" "$deferred" "$(paste -sd, "$RESULTS")"
[ "$FAILS" -eq 0 ]
