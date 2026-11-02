#!/usr/bin/env bash
# PLAN-39 (section 4): exports the Data Protection key rings of the single host (the volumes hrm_dp_* and
# hrm_gateway_dataprotection_keys, plus the protection certificate when there is one) into one encrypted archive, for a
# backup the owner keeps. Adding it to the scheduled backups is wave 2 (OPS-E).
#
#   bash export-dp-keys.sh --out /path/chosen/by/owner/dp-keys-<utc>.tar.gz.enc [--project hrm] [--cert /opt/hrm/secrets/dp-cert.pfx]
#
# The archive is encrypted with openssl enc (AES-256-CBC, PBKDF2, 600000 iterations) under a passphrase read from the
# environment variable DP_EXPORT_PASSPHRASE (never from argv; set it with read -rs in the shell that runs this). The
# output is mode 600 and its SHA-256 is written next to it. Volumes are read through a read-only mount; nothing is
# changed. To restore: openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -pass env:DP_EXPORT_PASSPHRASE -in X | tar -xz,
# then copy each directory back into its volume while the services are stopped (docs/OPS_VPS_SINGLE_HOST.md section 13).
set -euo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash on Windows: keep container paths as they are.
umask 077

PROJECT="hrm"
OUT=""
CERT="/opt/hrm/secrets/dp-cert.pfx"
while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2"; shift 2;;
    --project) PROJECT="$2"; shift 2;;
    --cert) CERT="$2"; shift 2;;
    *) echo "usage: export-dp-keys.sh --out file.tar.gz.enc [--project hrm] [--cert file.pfx]" >&2; exit 2;;
  esac
done

die() { printf '[export-dp-keys] ERROR: %s\n' "$*" >&2; exit 1; }
[ -n "$OUT" ] || die "--out is required (a path the owner chose, outside the release directories)"
[ ! -e "$OUT" ] || die "$OUT exists; never overwritten"
[ -n "${DP_EXPORT_PASSPHRASE:-}" ] || die "set DP_EXPORT_PASSPHRASE in the environment first (read -rs DP_EXPORT_PASSPHRASE; export DP_EXPORT_PASSPHRASE)"
[ "${#DP_EXPORT_PASSPHRASE}" -ge 16 ] || die "DP_EXPORT_PASSPHRASE must be at least 16 characters"
command -v openssl > /dev/null || die "openssl not found"

volumes="$(docker volume ls -q | grep -E "^${PROJECT}_(dp_.+|gateway_dataprotection_keys)$" || true)"
[ -n "$volumes" ] || die "no Data Protection volume of project $PROJECT"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/dp-keys"
count=0
for v in $volumes; do
  name="${v#"${PROJECT}_"}"
  mkdir -p "$work/dp-keys/$name"
  docker run --rm --network none -v "$v:/v:ro" alpine:3.20.3 tar -C /v -cf - . | tar -C "$work/dp-keys/$name" -xf -
  keys="$(find "$work/dp-keys/$name" -name 'key-*.xml' | wc -l)"
  printf '[export-dp-keys] %s: %s key file(s)\n' "$v" "$keys"
  count=$((count + 1))
done
if [ -f "$CERT" ]; then
  mkdir -p "$work/dp-keys/certificate"
  cp "$CERT" "$work/dp-keys/certificate/"
  printf '[export-dp-keys] certificate %s included\n' "$CERT"
fi

mkdir -p "$(dirname "$OUT")"
tar -C "$work" -czf - dp-keys \
  | openssl enc -aes-256-cbc -pbkdf2 -iter 600000 -salt -pass env:DP_EXPORT_PASSPHRASE -out "$OUT"
chmod 600 "$OUT"
sha256sum "$OUT" > "$OUT.sha256"
chmod 600 "$OUT.sha256"
printf '[export-dp-keys] %s volume(s) exported to %s (encrypted, mode 600); SHA-256 in %s.sha256\n' "$count" "$OUT" "$OUT"
