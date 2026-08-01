# SOV-005 Adoption Minutes — Production threat-modeling binding

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-005 (91) |
| Decision | Adopt production threat-modeling binding with `LiveThreatModelClaim=false` |

## Claim gates (deferred)

| Claim / capability | Requires |
|---|---|
| `LiveThreatModelClaim=true` | SOV-005a + live-independent-tm-review Satisfied |
| Live attack-tree walkthrough | SOV-005b + live-attack-tree-walkthrough |
| Live misuse backlog sync | SOV-005c + live-misuse-backlog-sync |
| Live residual risk signoff | SOV-005d + live-residual-risk-signoff |
| SOV-006 pen/red join | Satisfied (production pen-red binding; live pen = SOV-006a) |

## Cross-card

SOV-004 blocker `SOV-005` is Satisfied (platform threat-modeling binding). SOV-006 is Satisfied as production pen-red binding (not live pen). This does **not** authorize live independent TM review, raise `LivePamClaim`, or close NAT-001 / FRD-001.
