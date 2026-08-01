# PROD-004 — Adoption minutes (production remote-branch binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | PROD-004 |
| Decision | Adopt lab remote-branch matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveRemoteBranchClaim=true` | PROD-004a + live-remote-branch-ux Satisfied |
| Live PWA fleet | PROD-004b + live-pwa-fleet |
| Live field offline | PROD-004c + live-offline-field |
| L10N-001 / MOB-001 | PROD-004d |
| SYR-013 / NAT | PROD-004e |

## Cross-card sync

- PROD-003 employee-selfservice matrix: PROD-004 → Satisfied

## Notes

L10N-001 / MOB-001 / SYR-013 / PROD-005 remain NotStarted inside the PROD-004 matrix. No live remote-branch fleet is authorized by this binding.
