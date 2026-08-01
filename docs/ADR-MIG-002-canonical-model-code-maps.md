# ADR-MIG-002 — Canonical model and legacy code maps

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-002 (card 67) |
| Date | 2026-07-30 |
| Dependencies | MIG-001 Satisfied; TEN-001 Satisfied; GOV-003 Satisfied |

## Context

MIG-001 inventories sources and field-level SoT. GOV-003 owns platform writers. There is no migration-facing canonical entity pack, no value-level Map/Transform/Quarantine dispositions, no ban on mass `Other`, no signed repeatable mapping version, and no policy that legacy EmpNo/NAT_ID are reference-only (not auth/tenant keys).

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-canonical/code-map-matrix.json` |
| Depth | **Canonical + code-map gate pack** — docs + fixtures + validate; not MIG-003 ETL |
| Dispositions | Every legacy value: Map \| Transform \| Quarantine |
| Other | Forbidden as mass dump; `otherBucketMaxRate` enforced |
| Legacy ids | Reference fields only; never auth/session/tenant key |
| Locale | Asia/Damascus; encoding transforms (e.g. windows-1256→utf-8) |
| Soft blockers | agency-extract-access, SYR-003, MDM-001 |
| Closure | Done (canonical/code-map gate pack) + documented blockers |
| Out of scope | MIG-003 pipeline; MIG-004 match/merge; DataImportController rewrite |

## Blocker register

| Id | Status | Note |
|---|---|---|
| MIG-001 | Satisfied | Inventory gate Done |
| TEN-001 | Satisfied | Tenant model Done |
| GOV-003 | Satisfied | Field ownership Done |
| agency-extract-access | Blocked | Lab value universes for coverage |
| SYR-003 | Blocked | Full legal_regime model later; interim regimes mapped |
| MDM-001 | Blocked | Position catalog; JOB_TITLE → free text + quarantine |

## Consequences

- MIG-003 consumes these maps for transform/quarantine; does not redefine codes.
- Validate fails on missing disposition, mass Other, unsigned maps, or legacy id as security key.
