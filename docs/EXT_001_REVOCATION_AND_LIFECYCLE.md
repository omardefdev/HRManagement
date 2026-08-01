# EXT-001 — Revocation and lifecycle

## Lifecycle states

`provision` → `active` → `disable` → `revoke` (reason required).

## Revocation SLA

| Field | Value |
|---|---|
| slaMinutes | 15 |
| channel | federation-revoke-bus |

Revocation completed within SLA → PASS. Breach without escalation → FAIL.
