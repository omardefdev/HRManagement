# MIG-005 — Evidence package

## Required artifacts

`source-counts`, `bucket-breakdown`, `payroll-control`, `lineage-export`, `reject-register`, `checksum-manifest`, `sample-results`, `dual-control-signoff`.

## Immutability

- `immutable: true`
- `contentHash` (sha256) over package manifest
- WORM/tamper join: [ADR-OBS-003-siem-tamper-audit.md](ADR-OBS-003-siem-tamper-audit.md)

Hash mismatch or silent mutation → FAIL (`tamper-evidence-fail`).

This pack does **not** implement EVD-003 production vault (out of scope).
