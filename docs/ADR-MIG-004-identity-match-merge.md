# ADR-MIG-004 — Identity match, dedup, and reversible merge (gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-004 (card 69) |
| Date | 2026-07-30 |
| Dependencies | MIG-002 Satisfied; DATA-002 Satisfied; GOV-001 Satisfied |

## Context

MIG-002 provides signed code maps. DATA-002 provides legal employee lifecycle (no hard delete; Merged status). GOV-001 provides classification/privacy. GOV-003 offers an interim merge/unmerge runbook inside HR.Core. There is no signed matching policy for deterministic/probabilistic identity match, national-ID exceptions, high-risk auto-merge bans, labelled precision/recall, adversarial twin/name-variant handling, or cross-tenant review privacy for migration waves.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-match/match-matrix.json` |
| Depth | **Identity match/merge gate pack** — contracts + labelled harness + validate; not a live matcher service |
| Strategies | DeterministicNationalId, DeterministicComposite, ProbabilisticNameDob, ExceptionQueue |
| Auto-merge | Only at/below `autoMergeMaxRisk`; high risk requires human review + evidence |
| Merge audit | Required fields; reversible via unmerge (joins GOV-003 interim) |
| Soft blockers | agency-extract-access; L10N-002; live-labelled-metrics |
| Closure | Done (identity match/merge gate pack) |
| Out of scope | Production matcher; MDM-002 golden person; FRD-001 continuous fraud; Domain rewrite; MIG-005 |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| MIG-004a | Matcher worker consuming labelled rules |
| MIG-004b | Human review UI |
| MIG-004c | Cross-BC merge propagation |
| MIG-004d | Live labelled agency evaluation |

## Blocker register

| Id | Status | Note |
|---|---|---|
| MIG-002 | Satisfied | Code-map gate Done |
| DATA-002 | Satisfied | Legal lifecycle Done |
| GOV-001 | Satisfied | Privacy/classification Done |
| GOV-003-merge | Satisfied | Interim merge/unmerge runbook |
| agency-extract-access | Blocked | Lab labelled pairs only |
| L10N-002 | NotStarted | Arabic normalization deferred |
| live-labelled-metrics | Blocked | Precision/recall on agency extract → later |

## Consequences

- MDM-002 / FRD-001 / L10N-002 consume match thresholds and review privacy from this pack.
- Validate fails on unsigned thresholds, high-risk auto-merge, non-reversible merge stubs, or cross-tenant PII exposure in review fixtures.
