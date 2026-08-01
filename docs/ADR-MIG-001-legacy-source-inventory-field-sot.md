# ADR-MIG-001 — Legacy source inventory and field source-of-truth

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-001 (card 66) |
| Date | 2026-07-30 |
| Dependencies | STR-001 Satisfied; GOV-003 Satisfied |

## Context

STR-007 names stub legacy systems for value baselines. GOV-003 owns platform field writers. There is no machine inventory of legacy databases/files/paper/APIs with measured volumes/quality, access risks, freeze constraints, or legacy→target field SoT covering 100% of sources (or signed exceptions). DataImportController remains a primitive CSV path — not a migration factory.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-inventory/source-inventory.json` |
| Depth | **Inventory gate pack** + sampling/counts/profiling fixtures; not MIG-003 pipeline |
| Coverage | Every known source Inventoried, ExceptionSigned, or Unsupported with signed exception |
| Field SoT | `targetFieldSot[]` joins GOV-003 critical fields to legacy source fields |
| Volume/quality | Measured (`estimateOnly: false`) or ExceptionSigned |
| Soft blockers | agency-extract-access=Blocked; SYR-002=Blocked (official census) |
| Closure | Done (inventory gate pack) + documented blockers |
| Out of scope | MIG-002 maps; MIG-003 ETL; live agency PII extract; DataImportController rewrite |

## Blocker register

| Id | Status | Note |
|---|---|---|
| STR-001 | Satisfied | Envelope Done |
| GOV-003 | Satisfied | Field ownership / DQ Done |
| agency-extract-access | Blocked | Real agency read paths not granted; lab counts used |
| SYR-002 | Blocked | Official census volumes later |

## Consequences

- MIG-002+ consume this inventory; they do not re-open source discovery.
- Validate fails on estimate-only volumes without exception, missing SoT, empty access risks, unsigned profiling, or incomplete coverage.
