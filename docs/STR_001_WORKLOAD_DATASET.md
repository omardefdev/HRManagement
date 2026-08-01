# STR-001 — Workload and dataset description

This describes the **adopted shape** of national/syria envelopes. It is not CAP-001 proof. Lab scaling uses [NFR_CAPACITY_MODEL.md](NFR_CAPACITY_MODEL.md) `scale_factor` only.

## Dataset classes

| Class | Purpose | PII | Size reference |
|---|---|---|---|
| Synthetic engineering | Design & CI | Forbidden real PII | Lab: 10k persons at scale_factor 0.002 vs 5M |
| Staging synthetic (REL-001) | Parity/rehearsal | Synthetic only | Per REL-001 ceilings |
| Pilot limited | REL-002 agency | Legal decision | PILOT-AGENCY-001 ceilings |
| National proof | CAP-001 | Masked/synth per CAP | Envelope minima |

## Workload shape (adopted)

| Dimension | Value / note |
|---|---|
| Read:write (steady) | ~80:20 outside payroll window |
| Payroll window | Batch-dominant; messaging freshness critical (OBS J3/J4) |
| Peak HTTP | `national.peak_http_rps` excluding static assets |
| File sizes | Typical attachment ≤ 10 MB; CDR/scan path FILE-002 |
| Long-running | Payroll run, exports (PERF-004), migration rehearsals |

## Seasons (from NFR)

| Season | Pattern | Tied indicators |
|---|---|---|
| Morning login | Sessions + lists | concurrent_sessions, p95_read |
| Payroll window | Batch + MQ | payroll_* persons/hours |
| Report export | Async jobs | peak_http secondary |
| Org browse | Tree/scope | org_units |

## Explicit separation

| Layer | Proves |
|---|---|
| STR-001 | Adopted minima + shape |
| NFR-001 lab | Feasibility at reduced scale |
| CAP-001 | National load/soak against this envelope |
