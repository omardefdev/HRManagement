# ADR-MIG-004 — Match production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-004 (card 69) |
| Date | 2026-07-30 |
| Supersedes | Identity match/merge gate pack as insufficient for production |
| Dependencies | MIG-003 production factory binding |

## Context

The prior pack published the match matrix, thresholds, merge audit, review privacy, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigMatch` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-match/match-matrix.json` |
| Runtime | `MigMatchOptions` + `IMigMatchProvider` + `IIdentityMatchClaimGuard` |
| Strategies | DeterministicNationalId, DeterministicComposite, ProbabilisticNameDob, ExceptionQueue |
| Auto-merge | Only ≤ Low risk; high-risk AutoMerge forbidden; evidence required |
| Merge audit | Required fields; reversibleRequired |
| Review privacy | tenantScoped + forbidCrossAgencyPii |
| Live matcher | Default `LiveIdentityMatchClaim=false`; forbidden until MIG-004a–d |
| Soft blockers | MIG-002/DATA-002/GOV-001/GOV-003-merge Satisfied; agency-extract-access/live-labelled-metrics Blocked; L10N-002 NotStarted |
| Closure | **Done (production match binding)** |

## Consequences

- Staging/Production fail startup on incomplete match policy, dishonest blockers, or live identity-match claim.
- CI runs `mig004-match-runtime-validate.ps1`.
- Live matcher, MDM-002 golden person, FRD-001 continuous fraud, Domain rewrite, and live agency P/R remain out of scope.
