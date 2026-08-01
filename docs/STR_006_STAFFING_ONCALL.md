# STR-006 — Staffing / on-call (stub)

Extends [`REL_003_ONCALL_ROSTER.md`](REL_003_ONCALL_ROSTER.md). **Does not** claim national 24×7 desk — that is **SRE-001** (`national24x7Claim: false`).

| tier | primary | backups |
|---|---|---|
| platform | platform-lead | platform-backup |
| payroll | payroll-lead | payroll-backup |
| sre | sre-a | sre-b |
| soc | soc-a | soc-b |

## Simulation

See `testdata/str006/scenarios/staffing-oncall.json` — unannounced P1 window stub; coverage gaps fail validate when present in matrix `gapPolicy: failValidate`.

## Soft blocker

SRE-001 = Blocked until live rosters, paging, fatigue rules, and holiday coverage exist.
