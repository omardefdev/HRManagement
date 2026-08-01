# STR-007 — Program adoption minutes (value production binding)

| Field | Value |
|---|---|
| Card | STR-007 (card 65) |
| Date | 2026-07-30 |
| Decision | Bind kpi-matrix into host startup and CI |
| Default Live dashboard | `LiveDashboardClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/str-value/kpi-matrix.json` (copied to `ValueKpi/`) |
| Negative fixtures | reconcile-fail, screen-count-banned, cross-tenant-leak |
| Positive fixtures | reconcile-pass, independent-measurement, quarterly-benefit-review |

Promotion to live value dashboard = close PROD-006c + live-bi-dashboard Satisfied (PROD-006 production semantic-layer binding Done does not enable live BI; keep privacy rules). `cost_per_employee` numeric SoT = FIN-001. Live ESS self-service = PROD-003.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Product | _pending wet-ink_ | |
| Audit | _pending wet-ink_ | |
| Program Board | _pending wet-ink_ | |

## Independent review

See [STR_007_INDEPENDENT_REVIEW_WAIVER.md](STR_007_INDEPENDENT_REVIEW_WAIVER.md).
