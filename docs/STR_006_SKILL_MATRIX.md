# STR-006 — Skill matrix

Machine source: `skills[]` in [`str-ownership/ownership-matrix.json`](str-ownership/ownership-matrix.json).

Rule: Tier-0 skills require `qualifiedRoles.length >= minQualified` and `minQualified >= 2`.

| skillId | tier | qualified roles | minQualified |
|---|---|---|---|
| SK-PAYROLL-CALC | 0 | payroll-lead, payroll-backup, platform-lead | 2 |
| SK-PLATFORM-DEPLOY | 0 | platform-lead, platform-backup, sre-a | 2 |
| SK-DBA-RESTORE | 0 | dba-primary, dba-backup | 2 |
| SK-SOC-TRIAGE | 0 | soc-a, soc-b, security | 2 |
| SK-AUTH-IDP | 1 | auth-lead, auth-backup | 2 |

Feeds bus-factor exercise and staffing simulation. Full competency program = SUP-003 later.
