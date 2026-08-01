# STR-005 — Program decision log

Machine source: [`str-governance/decision-log.json`](str-governance/decision-log.json).

**Not** the GOV-002 legal payroll register ([`legal-decisions/register.json`](legal-decisions/register.json)).

| decisionId | type | owner | reviewer | board | status | evidence |
|---|---|---|---|---|---|---|
| PG-LAW-2026-07-SIM | LawChange | legal | architecture | Architecture | ApprovedInterim | law-change scenario |
| PG-INC-2026-07-SIM | SecurityIncident | security | change | Security | Approved | incident scenario |
| PG-DELAY-2026-07-SIM | ScheduleDelay | program | sponsor | Program | Approved | delay scenario |
| PG-GOLIVE-GATE-POLICY | GoLive | sponsor | security | Program | PolicyAccepted | GO_LIVE_GATE |

Invariant: `ownerRole` ≠ `reviewerRole` on every row (enforced by validate harness).
