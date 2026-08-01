# PROD-003 — Adoption minutes (production employee-selfservice binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | PROD-003 |
| Decision | Adopt lab employee-selfservice matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveEmployeeSelfServiceClaim=true` | PROD-003a + live-employee-portal Satisfied + IAM-002 Satisfied |
| Live manager portal | PROD-003b + live-manager-portal |
| Live usage / KPI measurement | PROD-003c + live-usage-telemetry |
| ESS-001 / MSS-001 | PROD-003d |
| PROD-004 / NAT-005 | PROD-003e |

## Cross-card sync

- PROD-002 agency matrix: PROD-003 → Satisfied
- STR-007 KPI matrix: PROD-003 → Satisfied (was Blocked)

## Notes

IAM-002 remains NotStarted inside the PROD-003 matrix. No live ESS/MSS portals are authorized by this binding.
