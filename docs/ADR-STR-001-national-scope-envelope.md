# ADR-STR-001 — National scope and size/service envelope

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-001 (card 59) |
| Date | 2026-07-30 |
| Dependencies | None declared |
| Inputs | 06-NATIONAL-READINESS-MODEL; 12-SYRIA-BASELINE; NFR_CAPACITY_MODEL (lab scale only) |

## Context

File 06 holds provisional national and Syria-interim envelope numbers and states they are replaced by STR-001 once scope is adopted. There is no machine-readable matrix with owner/date/source tier, no adopted workload/dataset charter, no assumption register, and no growth-2x / independent-review gate pack. Claims of “national scale” lack a binding document.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-scope/envelope-matrix.json` — journeys NationalScope / QuantitativeEnvelope / WorkloadDataset / AssumptionsExclusions / GrowthReviewSignOff |
| Depth | **Envelope gate pack** — not CAP-001 load proof, not SYR-002 census, not NAT-008 |
| Envelopes | `national-competition` + `syria-interim`; promote file-06 values as adopted minima |
| Indicator fields | id, value, unit, ownerRole, asOfDate, sourceTier (A\|B\|C), envelope, optional assumptionId |
| Lowering values | Forbidden without signed exception path |
| Country scope | Syrian Arab Republic; 14 governorates; public-sector HR journeys FUN-001..006 + payroll |
| Census final | `blockedOn: SYR-002` — engineering envelope only |
| Independent review | Slot or Program Board waiver (not hiring consultant as card work) |
| Closure | Done = pack + validate harness + Str001*; TRACEABILITY `STR-001 ✓ (envelope pack)` |
| Out of scope | CAP-001 soak, SYR-002 signed census, STR-002 legal catalog, Aspire |

## Consequences

- File 06 remains historical reference; STR-001 matrix is the adoption source.
- CAP-001 / MIG / STR-002+ consume these minima; they do not re-negotiate downward silently.
- Validate script fails if required indicators missing or syria-interim lacks SYR-002 assumption link.
