# ADR-MIG-006 — Cutover production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-006 (card 71) |
| Date | 2026-07-30 |
| Supersedes | Cutover/rehearsal gate pack as insufficient for production |
| Dependencies | MIG-005 production evidence binding, OPS-003, REL-001 |

## Context

The prior pack published the cutover matrix, rehearsal/delta/rollback/Go-No-Go contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigCutover` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-cutover/cutover-matrix.json` |
| Runtime | `MigCutoverOptions` + `IMigCutoverProvider` + `IAgencyCutoverClaimGuard` |
| Rehearsals | ≥3 timed / withinWindow / volumeOk |
| Delta | `noLossRequired` |
| Rollback | `mustBeExercised` + `paperOnlyForbidden` |
| Go/No-Go | decisionRequired + ≥5 mandatory criteria |
| Legacy | readOnlyAfterCutover + securedRequired |
| DNS | `liveForbiddenInThisPack=true` in this binding |
| Live cutover | Default `LiveAgencyCutoverClaim=false`; forbidden until MIG-006a–d |
| Soft blockers | MIG-005/OPS-003/REL-001 Satisfied; agency-extract-access / live-cutover-perf / live-agency-recon / live-rehearsal-at-scale Blocked |
| Closure | **Done (production cutover binding)** |

## Consequences

- Staging/Production fail startup on incomplete cutover policy, dishonest blockers, paper-only rollback, or live agency-cutover claim.
- CI runs `mig006-cutover-runtime-validate.ps1`.
- Live agency cutover, production DNS, agency-scale rehearsal, and DEC-001 decommission remain out of scope.
