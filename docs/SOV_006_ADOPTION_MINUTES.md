# SOV-006 Adoption Minutes — Production pen-red binding

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-006 (92) |
| Decision | Adopt production pen-red binding with `LivePenClaim=false` |

## Claim gates (deferred)

| Claim / capability | Requires |
|---|---|
| `LivePenClaim=true` | SOV-006a + live-independent-pen-assessment Satisfied |
| Live red team exercise | SOV-006b + live-red-team-exercise |
| Live purple detection validation | SOV-006c + live-purple-detection-validation |
| Live remediation/retest closure | SOV-006d + live-remediation-retest-closure |
| NAT-002 national cert join | SOV-006e |

## Cross-card

SOV-005 blocker `SOV-006` is Satisfied (platform pen-red binding). This does **not** authorize live pen/red/purple, raise `LiveThreatModelClaim`, or close NAT-001 / NAT-002 / FRD-001. SOV-007 Done separately as production vendor-exit binding (not live exit drill).
