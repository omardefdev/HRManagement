# ADR-MIG-005 — Migration reconciliation and evidence chain (gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-005 (card 70) |
| Date | 2026-07-30 |
| Dependencies | MIG-003 Satisfied; OBS-003 Satisfied |

## Context

MIG-003 provides lineage, quarantine/rejects, and pipeline-run ledger contracts. OBS-003 provides tamper-evident audit (hash chain / WORM). There is no signed reconciliation policy that forces 100% accounting (migrated/rejected/deferred+reason), zero unexplained financial variance, dual-control wave sign-off, and immutable evidence packages for migration waves.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-evidence/recon-matrix.json` |
| Depth | **Reconciliation/evidence gate pack** — contracts + harness; not EVD-003 vault or live agency SQL |
| Buckets | Migrated, Rejected, Deferred (reason required) |
| Financial | unexplained variance forbidden (lab tolerance 0) |
| Evidence | immutable package + contentHash; joins OBS-003 |
| Soft blockers | agency-extract-access; live-agency-recon; live-independent-sql |
| Closure | Done (reconciliation/evidence gate pack) |
| Out of scope | EVD-003 production vault; live agency SQL; MIG-006; Domain rewrite |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| MIG-005a | Recon job against live loads |
| MIG-005b | Evidence vault bind (EVD-003) |
| MIG-005c | Agency live sign-off |
| MIG-005d | Independent SQL pack |

## Blocker register

| Id | Status | Note |
|---|---|---|
| MIG-003 | Satisfied | Factory gate Done |
| OBS-003 | Satisfied | SIEM / hash / WORM Done |
| agency-extract-access | Blocked | Lab harness only |
| live-agency-recon | Blocked | Real agency recon later |
| live-independent-sql | Blocked | Independent SQL on agency later |

## Consequences

- MIG-006 cutover rehearsals consume accounted + evidence contracts from this pack.
- Validate fails on unexplained gaps, financial variance without reason, missing dual control, or tampered evidence hash.
