# MIG-006 — Rehearsal playbook (lab)

Minimum **3** timed rehearsals (R1–R3), each within the MIG-003 cutover window budget (8h lab) and volumeOk against synthetic budget.

## Sequence (lab)

1. Change freeze declared
2. Extract / load rehearsal under freeze
3. Delta capture between freeze and cut
4. Parallel read/run validation
5. User lock
6. Go/No-Go decision
7. Surprise rollback drill (at least once across R1–R3)
8. Legacy read-only plan verified
9. Post-cutover recon stubs T+0 / T+1 / T+30 (joins MIG-005)

Live agency-scale rehearsal remains **Blocked** (`live-rehearsal-at-scale`, `live-cutover-perf`).
