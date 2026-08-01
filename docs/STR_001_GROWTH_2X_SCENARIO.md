# STR-001 — Growth 2x scenario review

Purpose: stress-test whether envelope minima remain coherent if critical volume indicators double within ~5 years. **Not** a CAP-001 execution.

## Doubled indicators (illustrative)

| Indicator id | Adopted | 2x | Concern for CAP later |
|---|---:|---:|---|
| national.active_history_persons | 5,000,000 | 10,000,000 | Storage, MDM, search |
| national.tenants | 1,000 | 2,000 | Isolation, onboarding factory |
| national.concurrent_sessions | 100,000 | 200,000 | Gateway/Keycloak HPA |
| national.peak_http_rps | 10,000 | 20,000 | Edge + DB |
| national.payroll_window_persons | 5,000,000 | 10,000,000 | Window hours may need extend or more parallelism |
| syria.payroll_ordinary_persons | 1,250,000 | 2,500,000 | Ordinary window ≤4h under pressure |

## RTO / payroll window effects

1. If persons 2x and window hours fixed, required throughput ~2x — CAP-001 must show headroom or accept longer window via new STR revision.
2. RTO 60m / RPO 5m stay as **targets**; 2x data volume may need DR topology revision (OPS/SRE), not silent relaxation.
3. Syria interim rises automatically if SYR-002 > current; 2x is additional planning stress.

## Questions handed to CAP-001 / architecture

- Headroom % required above envelope at L4?
- Payroll parallelism limits vs PERF-001?
- Tenant blast-radius controls at 2x tenants?

## Review record

| Field | Value |
|---|---|
| Review date | |
| Facilitator | |
| Outcome | Accepted for planning / Needs STR revision |
| Link to notes | |
