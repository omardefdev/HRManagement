# REL-003 — تحقق الإغلاق

| تحقق | دليل |
|---|---|
| ADR + catalog + severity | `ADR-REL-003-ops-support-sm.md`, `rel-ops/service-catalog.json`, `REL_003_SEVERITY_SLA.md`, `REL_003_JOURNEY_MATRIX.md` |
| ITSM processes | INCIDENT / PROBLEM / CHANGE / STATUS_MAINTENANCE |
| On-call + game day | ONCALL_ROSTER, `rel003-incident-game-day.ps1`, `testdata/rel003/game-day/` |
| Access / patch / PM | ACCESS_REVIEW, PATCH_CADENCE, POSTMORTEM + CSVs |
| OBS links | OBS_ALERTING_TOPOLOGY, OBS_ALERT_RUNBOOKS |
| Tests | `Rel003*` |
| Backlog | REL-003 Done في `04-P3` |

**الحالة:** Done — Ops SM baseline pack (ليس SRE/SUP وطني).

**كتالوج المنتج (tiers / mandatory / agency contract / flag readiness):** [STR-003](./ADR-STR-003-service-module-catalog.md) / [`str-modules/module-catalog.json`](./str-modules/module-catalog.json) — REL-003 يبقى ops SM فقط.
