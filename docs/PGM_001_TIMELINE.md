# PGM-001 — Program Timeline, Critical Path, Resource Model, and Scope Decomposition

## Purpose

Establish the program baseline timeline, critical path (separating engineering from sovereign external constraints), XL card decomposition, resource model, wave-one minimum slice, and evidence-based progress reporting — prerequisite for credible launch date and FIN-001 TCO.

## Scope

- Decompose every open XL card into approved stories
- Duration and effort estimate per card with owner
- Dependency network and critical path
- Separate sovereign-external blockers from engineering work
- Resource model by team from master plan
- Wave-one minimum slice with explicit deferrals
- Baseline timeline linked to FIN-001 and CAP-003
- Schedule risk register with legislation/hosting scenarios

## Critical Path

See `docs/pgm/critical-path.json` for the machine-readable model.

| ID | Path Segment | Constraint Type | Duration |
|----|--------------|-----------------|----------|
| CP-01 | Legislation binding | sovereign-external | 12 weeks |
| CP-02 | Hosting and keys | sovereign-external | 16 weeks |
| CP-03 | Payroll and treasury | mixed | 14 weeks |
| CP-04 | Migration and onboarding | engineering | 10 weeks |
| CP-05 | Wave rollout | engineering | 20 weeks |

## XL Decomposition Policy

- Zero XL cards open for execution without approved story split
- Current open XL without split: **0**
- Split examples documented for SYR-001, SYR-005, SYR-007, SYR-008, MIG-007, PRD-001

## Wave-One Minimum Slice

| Included | Deferred (explicit) |
|----------|---------------------|
| Core HR | Special regime full |
| Payroll Law 50 | Mobile offline full |
| Civil identity match | Advanced analytics |
| Branch RTL UX | |

## Acceptance Criteria Mapping

- [x] Zero XL without split — `openXlWithoutSplit: 0`
- [x] Every card has estimate, owner, dependency position — critical path entries
- [x] Critical path shows sovereign constraints — `constraintType` field
- [x] Wave-one slice names deferrals explicitly — `explicitlyDeferred`
- [x] Baseline linked to FIN-001/CAP-003 — baseline flags
- [x] No launch date without this baseline — documented policy
- [x] Re-plan on SYD/scope/wave change — schedule risks + progress rules

## Artifacts

| File | Purpose |
|------|---------|
| `docs/PGM_001_TIMELINE.md` | This document |
| `docs/pgm/critical-path.json` | Timeline and critical path model |
| `scripts/pgm001-validate.ps1` | Validates PGM-001 artifacts |
| `Pgm001TimelineTests.cs` | Unit tests |

## Validation

- `scripts/pgm001-validate.ps1`
- `Pgm001TimelineTests`
