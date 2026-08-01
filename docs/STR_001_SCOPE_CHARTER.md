# STR-001 — Scope charter (state / agencies / service)

Adopted target state for the national HR platform envelope. Binding companion: [`str-scope/envelope-matrix.json`](str-scope/envelope-matrix.json).

| Field | Adopted value |
|---|---|
| Country | Syrian Arab Republic |
| Deployment geography | All 14 governorates (phased waves) |
| Tenant model | Independent agencies/tenants (see indicator `national.tenants`) |
| Person records | Active + historical employment records |
| Functional categories | Civil service / public payroll population in scope; see exclusions |
| Critical journeys | FUN-001..006 + payroll run/correctness |
| Currency | New Syrian pound (SYP); historical verify + 100:1 conversion note |
| Calendars / languages | Arabic primary, RTL; dated central holiday calendar |
| Connectivity profiles | Urban / rural / weak-intermittent (no silent drop/dupe) |
| Availability / RPO / RTO / p95 | Quantitative rows in envelope matrix |
| Five-year outlook | Growth 2x scenario document; CAP-001 proves capacity later |

## Non-claims

- This charter does **not** assert that live headcount equals envelope numbers.
- Official census remains **SYR-002**.
- Lab `scale_factor` in NFR_CAPACITY_MODEL is not national proof.
