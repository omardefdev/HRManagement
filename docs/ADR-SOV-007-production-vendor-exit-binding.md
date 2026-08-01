# ADR-SOV-007 — Production vendor-exit binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-007 |
| Date | 2026-07-30 |

## Decision

`SovVendor` loads `vendor-exit-matrix.json` at startup. Staging/Production set `EnforceOnStartup=true` and `LiveVendorClaim=false`. Incomplete exit contracts, unowned rebuild, incomplete export, critical-without-alternate, unattested destruction, or `LiveVendorClaim=true` fail startup/CI.

## Consequences

- STR-006 ownership blocker `SOV-007` → Satisfied (platform vendor-exit binding; live exit drill = VEN-001).
- VEN-001 / NAT-001 / NAT-002 / SAN-001 / IOP-002 / SUP-004 stay open; `National24x7Claim` / `LivePenClaim` unchanged.
