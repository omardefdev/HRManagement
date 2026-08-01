# SRE-001 — Verification

## Artifacts

| Artifact | Path |
|---|---|
| Roster | [ON_CALL_ROSTER.md](./ON_CALL_ROSTER.md) |
| Escalation | [SRE_001_ESCALATION_POLICY.md](./SRE_001_ESCALATION_POLICY.md) |
| Access | [SRE_001_ACCESS_MATRIX.md](./SRE_001_ACCESS_MATRIX.md) |
| Drills | [SRE_001_DRILL_PLAYBOOK.md](./SRE_001_DRILL_PLAYBOOK.md) |
| Validate script | `scripts/sre001-oncall-validate.ps1` |

## Drill log

| Date | Drill | Ack | IC | Result | Notes |
|---|---|---|---|---|---|
| 2026-07-31 | Template / dry validation | n/a | n/a | Script+docs OK | Staff names + live page drill before national ORR |

## Acceptance

- [x] Roster structure without scheduling gaps (slots defined 24x7)
- [x] ≥2 Tier-0 backup slots modeled
- [x] P1 ack/IC targets documented and drillable
- [x] Access matrix requires tools before shift
- [ ] Live unannounced page drill with staffed names (operational gate)
