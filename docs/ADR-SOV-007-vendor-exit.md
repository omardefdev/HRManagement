# ADR-SOV-007 — Vendor-exit contracts (pack)

| Field | Value |
|---|---|
| Status | Accepted (pack) |
| Card | SOV-007 |
| Date | 2026-07-30 |

## Decision

Source of truth is `docs/sov-vendor/vendor-exit-matrix.json`. Lab contracts encode IP/source/build/IaC/SBOM/docs ownership, escrow/licenses/subcontractors, open export, critical alternates, clean-room rebuild from owned artifacts, knowledge transfer, exit assistance, and destruction attestation. Live clean-room / exit drill / full export-import / destruction remain Blocked; `LiveVendorClaim=false`.
