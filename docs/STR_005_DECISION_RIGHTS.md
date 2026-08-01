# STR-005 — Decision rights (RACI summary)

Source: [`str-governance/governance-matrix.json`](str-governance/governance-matrix.json) — [ADR-STR-005](ADR-STR-005-program-governance-boards.md).

Rule: for every critical decision, **ownerRole ≠ reviewerRole**.

| Type | Owner (R) | Reviewer (A) | Board | ADR required |
|---|---|---|---|---|
| LawChange | legal | architecture | Architecture | Yes |
| SecurityIncident | security | change | Security | No |
| ScheduleDelay | program | sponsor | Program | No |
| GoLive | sponsor | security | Program | Yes |
| ArchitectureVariance | architecture | program | Architecture | Yes |
| DataModelChange | data | architecture | Data | Yes |
| ExceptionGrant | program | sponsor | Program | No |

## Boards (quorum ≥ 2)

| Board | Default owner | Default reviewer | Scope |
|---|---|---|---|
| Architecture | architecture | program | ADR, variance, platform boundaries |
| Security | security | sponsor | Incidents, exceptions, Go-Live security seat |
| Data | data | architecture | Canonical model, freeze, retention |
| Change | change | security | CAB-lite (REL-003); emergency windows |
| Program | program | sponsor | Delay, milestones, escalation |

Names of people for seats remain placeholders until **SYD-017** closes.

Distinct from GOV-002 legal payroll register (`docs/legal-decisions/register.json`).
