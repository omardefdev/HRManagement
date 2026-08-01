# ADR-MIG-006 — Cutover rehearsal and rollback (hybrid gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-006 (card 71) |
| Date | 2026-07-30 |
| Dependencies | MIG-005 Satisfied; OPS-003 Satisfied; REL-001 Satisfied |

## Context

MIG-005 provides accounted buckets and immutable evidence contracts. OPS-003 provides backup/PITR restore drills. REL-001 provides production-like staging. REL-002 migration rehearsal stub remains blocked on MIG-006. There is no signed cutover playbook requiring ≥3 timed rehearsals, change freeze, delta capture without loss, exercised (not paper-only) rollback, Go/No-Go gate, legacy read-only securing, and post-cutover recon at T+0/T+1/T+30.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-cutover/cutover-matrix.json` |
| Depth | **Hybrid cutover/rehearsal gate pack** — contracts + timed harness; not live agency cutover |
| Rehearsals | ≥3 timed; window/volume ref MIG-003 budget |
| Rollback | mustBeExercised; joins OPS-003 PITR |
| Soft blockers | agency-extract-access; live-cutover-perf; live-agency-recon; live-rehearsal-at-scale |
| Closure | Done (cutover/rehearsal gate pack) |
| Out of scope | Live agency cutover; production DNS flip; MIG-007; Domain rewrite |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| MIG-006a | Live timed rehearsal at agency volume |
| MIG-006b | Production DNS / communications |
| MIG-006c | Surprise rollback on staging-at-scale |
| MIG-006d | T+30 live recon |

## Blocker register

| Id | Status | Note |
|---|---|---|
| MIG-005 | Satisfied | Evidence gate Done |
| OPS-003 | Satisfied | Backup/PITR Done |
| REL-001 | Satisfied | Staging Done |
| agency-extract-access | Blocked | Lab harness only |
| live-cutover-perf | Blocked | Real window + agency volumes later |
| live-agency-recon | Blocked | Live T+ recon later |
| live-rehearsal-at-scale | Blocked | Full agency-scale rehearsal later |

## Consequences

- MIG-007 onboarding and DEC-001 decommission consume cutover readiness from this pack.
- REL-002 migration rehearsal checklist can mark lab contracts Satisfied; live remains Blocked.
- Validate fails on <3 rehearsals, paper-only rollback, delta loss, missing Go/No-Go, or unsecured legacy.
