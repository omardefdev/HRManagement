# REL-002 — Migration rehearsal stub

REL-002 does **not** re-execute national migration. This checklist is satisfied only when MIG-006 is Done, or Waived for pilot with signed exception.

## Status

| Field | Value |
|---|---|
| blockedOn | MIG-006 |
| status | Lab contracts Satisfied via MIG-006 gate pack; **live** still Blocked |
| pilotAgency | PILOT-AGENCY-001 |
| acceptanceLink | [ADR-MIG-006](ADR-MIG-006-cutover-rehearsal-rollback.md), [MIG_006_VERIFICATION.md](MIG_006_VERIFICATION.md) |
| liveBlockers | agency-extract-access, live-cutover-perf, live-rehearsal-at-scale |

## Checklist (consumed from MIG-006)

| Item | Done? | Evidence |
|---|---|---|
| Timed rehearsal 1 | Lab Yes | testdata/mig006/rehearsals (R1) |
| Timed rehearsal 2 | Lab Yes | R2 |
| Timed rehearsal 3 | Lab Yes | R3 |
| Cutover runbook reviewed | Lab Yes | MIG_006_REHEARSAL_PLAYBOOK |
| Rollback dry-run | Lab Yes | surprise-rollback-pass (exercised) |

## Pilot exception path

If Pilot proceeds without MIG-006: record Waiver in EXCEPTION doc with program + migration + security signatures; keep status Waived in scope-matrix blockers.
