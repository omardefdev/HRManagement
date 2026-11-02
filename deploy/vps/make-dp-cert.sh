#!/usr/bin/env bash
# PLAN-39 (section 4, D-39-3): makes the optional certificate that encrypts the Data Protection key rings at rest
# (DataProtection:CertificatePath). NOT enabled on the demo server (D-39-3); recommended for a pilot, where the
# certificate and its password must also be kept off the server (losing the certificate loses every key ring).
#
#   bash make-dp-cert.sh [--out /opt/hrm/secrets/dp-cert.pfx] [--env /opt/hrm/shared/.env] [--days 3650]
#
# RSA 3072, valid 10 years, written as a PFX with mode 600. The PFX password is generated with openssl rand and appended
# to the .env as DATAPROTECTION_CERTIFICATE_PASSWORD (never printed; the key must not be there yet). Refuses to overwrite
# an existing certificate: rotate by making a new one under another name, moving the old path to
# DataProtection__PreviousCertificatePaths, then pointing DataProtection__CertificatePath at the new one.
# To enable: mount the PFX read-only into every application and set DataProtection__CertificatePath and
# DataProtection__CertificatePassword (${DATAPROTECTION_CERTIFICATE_PASSWORD}) in a compose override.
set -euo pipefail
umask 077

OUT="/opt/hrm/secrets/dp-cert.pfx"
ENV_FILE="/opt/hrm/shared/.env"
DAYS=3650
while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2"; shift 2;;
    --env) ENV_FILE="$2"; shift 2;;
    --days) DAYS="$2"; shift 2;;
    *) echo "usage: make-dp-cert.sh [--out file.pfx] [--env .env] [--days N]" >&2; exit 2;;
  esac
done

die() { printf '[make-dp-cert] ERROR: %s\n' "$*" >&2; exit 1; }
command -v openssl > /dev/null || die "openssl not found"
[ ! -e "$OUT" ] || die "$OUT exists; never overwritten (rotate with a new file name, see the header)"
[ -f "$ENV_FILE" ] || die "$ENV_FILE not found"
if grep -qE '^DATAPROTECTION_CERTIFICATE_PASSWORD=.+' "$ENV_FILE"; then
  die "DATAPROTECTION_CERTIFICATE_PASSWORD is already set in $ENV_FILE; refusing to replace it"
fi

mkdir -p "$(dirname "$OUT")"
chmod 700 "$(dirname "$OUT")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The password goes to openssl through an environment variable of this process only (pass:env:), never argv.
DP_CERT_PASSWORD="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n')"
export DP_CERT_PASSWORD
openssl req -x509 -newkey rsa:3072 -sha256 -days "$DAYS" -nodes \
  -subj "/CN=hrm-dataprotection-$(date -u +%Y%m%d)" \
  -keyout "$work/key.pem" -out "$work/cert.pem" > /dev/null 2>&1 || die "openssl req failed"
openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -out "$OUT" -passout env:DP_CERT_PASSWORD > /dev/null 2>&1 \
  || die "openssl pkcs12 failed"
chmod 600 "$OUT"
printf 'DATAPROTECTION_CERTIFICATE_PASSWORD=%s\n' "$DP_CERT_PASSWORD" >> "$ENV_FILE"
unset DP_CERT_PASSWORD
chmod 600 "$ENV_FILE"

printf '[make-dp-cert] wrote %s (RSA 3072, %s days, mode 600); DATAPROTECTION_CERTIFICATE_PASSWORD added to %s (value not shown)\n' \
  "$OUT" "$DAYS" "$ENV_FILE"
printf '[make-dp-cert] SHA-256 fingerprint: %s\n' "$(openssl x509 -in "$work/cert.pem" -noout -fingerprint -sha256 | cut -d= -f2)"
printf '[make-dp-cert] keep a copy of the PFX and its password OFF this server: without them every key ring is lost\n'
