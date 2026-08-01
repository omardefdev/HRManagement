# EXT-004 — Certificate validation and revocation

## Validation

| Check | Required |
|---|---|
| Expiry | `certValidation.checkExpiry` |
| Revocation | `certValidation.checkRevocation` |
| Unknown status | `failClosedOnUnknown` — treat as invalid |

## Revocation

`revocation.failClosed` = true. Lab sources: CRL, OCSP (mock).

Revoked cert treated as valid → FAIL (`revoked-cert-fail`).

Live national CA CRL/OCSP endpoints = `national-pki-live` (Blocked).
