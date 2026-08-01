# STR-005 — تحقق الإغلاق

| تحقق | دليل |
|---|---|
| ADR + matrix + blockers | `ADR-STR-005-program-governance-boards.md`, `str-governance/governance-matrix.json` |
| Decision rights + journeys | DECISION_RIGHTS, JOURNEY_MATRIX |
| Decision + risk registers | decision-log.json, risk-register.json |
| Exception / escalation / milestone | EXCEPTION_PATHS, CONFLICT_ESCALATION, MILESTONE_EVIDENCE |
| Simulations law/incident/delay | SIMULATION_PACK + testdata/str005/scenarios |
| Go-Live multi-seat | GO_LIVE_GATE + eng-only FAIL / multi-seat PASS |
| CODEOWNERS | `.github/CODEOWNERS` paths for STR-005 |
| Harness | `str005-governance-validate.ps1` → PASS |
| EXCEPTION/RECON/readiness | STR_005_* |
| Tests | `Str005*` |
| Backlog | Done (governance gate pack) + soft blockers في `07-…` |

**الحالة:** Done (governance gate pack) — SYD-017 Blocked؛ PGM-001 NotStarted.
