# ADR-STR-006 — Ownership production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-006 (card 64) |
| Date | 2026-07-30 |
| Supersedes | Ownership/sustainment gate pack as insufficient for production |
| Dependencies | STR-005 production governance binding |

## Context

The prior pack published the ownership matrix, bus-factor/TCO/vendor fixtures, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ProgramOwnership` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/str-ownership/ownership-matrix.json` |
| Runtime | `OwnershipOptions` (`ProgramOwnership`) + `IOwnershipProvider` + `ISustainmentClaimGuard` |
| Bus-factor | Critical components require `busFactor >= criticalMinBusFactor` and backups |
| Budget | Must fund ops/security/backup; forbidDevOnly |
| Vendors | Required clauses RTO + IncidentDisclosure + Exit with exitAssistance |
| National 24×7 | Default `National24x7Claim=false`; true forbidden while SRE-001 Blocked |
| Soft blockers | STR-005 Satisfied; SOV-007 Satisfied (vendor-exit binding; live drill = VEN-001); SRE-001 Blocked; FIN-001 NotStarted |
| Closure | **Done (production ownership binding)** |

Raising `National24x7Claim=true` requires SRE-001. Full independent TCO → FIN-001. Clean-room exit → SOV-007.

## Consequences

- Staging/Production fail startup on orphan critical ownership, dev-only budget, vendor missing Exit, dishonest soft blockers, or national 24×7 claim while SRE-001 Blocked.
- CI runs `str006-ownership-runtime-validate.ps1`.
- Live SRE desk, finance workbook, and clean-room exit drills remain out of scope.
