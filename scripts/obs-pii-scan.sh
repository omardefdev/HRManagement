#!/usr/bin/env bash
# OBS-001: fail if sample contains raw PII values (not redacted placeholders).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURE="${ROOT}/policy/obs/testdata/bad-span-attributes.json"
GOOD="${ROOT}/policy/obs/testdata/ok-span-attributes.json"

# Raw values that must never appear exported
raw_pii='super-secret|"[0-9]{9,}"|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+'

echo "== bad fixture must contain raw PII (control) =="
if ! grep -Eq "${raw_pii}" "${FIXTURE}" && ! grep -q '12345678901' "${FIXTURE}"; then
  echo "bad fixture missing raw PII markers" >&2
  exit 2
fi

echo "== redacted sample must not contain raw PII values =="
if grep -Eq 'super-secret|12345678901|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+' "${GOOD}"; then
  echo "PII SCAN FAILED: good fixture still has raw PII values" >&2
  exit 1
fi
if ! grep -q '\[redacted\]' "${GOOD}"; then
  echo "PII SCAN FAILED: good fixture missing [redacted] markers" >&2
  exit 1
fi

if [[ -n "${TEMPO_URL:-}" ]]; then
  echo "== live Tempo sample (best-effort) =="
  sample="$(curl -fsS "${TEMPO_URL}/api/search?limit=5" || true)"
  if echo "${sample}" | grep -Eq 'super-secret|12345678901'; then
    echo "PII SCAN FAILED on live Tempo sample" >&2
    exit 1
  fi
fi

echo "PII scan OK (fixtures)"
