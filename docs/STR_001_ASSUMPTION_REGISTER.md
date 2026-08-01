# STR-001 — Assumption register

Every assumption has owner and date. Linked from [`str-scope/envelope-matrix.json`](str-scope/envelope-matrix.json) via `assumptionId`.

| Id | Statement | Owner | As of | Status | Notes |
|---|---|---|---|---|---|
| ASS-ENG-FLOOR | File-06 competition minima are the engineering floor until raised by evidence | architecture | 2026-07-30 | Active | Must not be lowered without EXCEPTION |
| ASS-WORKLOAD-SHAPE | Peak RPS and p95 assume NFR season mix without static assets | sre | 2026-07-30 | Active | CAP-001 may refine ratios |
| ASS-SYR-CENSUS | Syria person/payroll counts are engineering interim, not official census | program | 2026-07-30 | Active | **blockedOn SYR-002**; auto-raise if census higher |
| ASS-SYR-GEO | 14 governorates phased waves; connectivity urban/rural/weak required | program | 2026-07-30 | Active | See file 12 |
| ASS-SYR-CURRENCY | New SYP with verifiable history and 100:1 conversion semantics | product | 2026-07-30 | Active | Legal pack STR-002/SYR later |
| ASS-NO-NAT-SCALE-WITHOUT-DOC | Phrase “national scale” means this envelope pack only | program | 2026-07-30 | Active | Enforced in EXCEPTION |

## Blockers (national)

| Id | Status |
|---|---|
| SYR-002 | Blocked (census) |
| CAP-001 | Blocked (load proof) |
