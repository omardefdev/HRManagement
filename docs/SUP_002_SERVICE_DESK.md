# SUP-002 — Service Desk, L1–L3, and SLA/OLA Agreements

**Card:** SUP-002  
**Status:** Done (service desk framework + SLA/OLA matrix + knowledge base structure)

## Purpose

Establish a 24x7 service desk for critical intake with tiered support levels, measurable SLAs/OLAs, knowledge management, and continuous improvement through trend/problem analysis.

## Support tiers

| Tier | Scope | Staffing | Target |
|------|-------|----------|--------|
| L1 — Service Desk | Triage, categorize, known-error resolution, password resets, FAQ | 24x7 for critical | FCR ≥ 40 % |
| L2 — Application Support | Configuration, workflow, data correction, integration issues | Business hours + on-call | Resolve ≥ 70 % of escalated |
| L3 — Engineering | Code fixes, infrastructure, database, security incidents | On-call | Root cause + fix |

## Channels

| Channel | Availability | Use case |
|---------|-------------|----------|
| Web portal (ticketing) | 24x7 | All requests |
| Phone hotline | 24x7 critical, business hours general | Urgent / P1 |
| Email | 24x7 intake | Non-urgent |
| Chat / Teams | Business hours | Quick questions |

## SLA / OLA matrix

Machine-readable definition: [`sup/sla-ola-matrix.json`](sup/sla-ola-matrix.json)

| Priority | Response | Resolution | OLA (L2 pickup) | OLA (L3 pickup) |
|----------|----------|------------|------------------|------------------|
| P1 — Critical | ≤ 15 min | ≤ 4 hours | ≤ 10 min | ≤ 30 min |
| P2 — High | ≤ 30 min | ≤ 8 hours | ≤ 30 min | ≤ 2 hours |
| P3 — Medium | ≤ 4 hours | ≤ 3 business days | ≤ 4 hours | ≤ 1 business day |
| P4 — Low | ≤ 1 business day | ≤ 5 business days | ≤ 1 business day | ≤ 3 business days |

## Knowledge management

Structure: [`sup/knowledge-base-structure.md`](sup/knowledge-base-structure.md)

- Top 20 issues must have knowledge articles.
- Articles reviewed quarterly for accuracy.
- L1 resolution scripts linked to ticket categories.
- New known errors added within 5 business days of identification.

## Categorization and routing

| Category | Sub-category examples | Default tier |
|----------|----------------------|--------------|
| Access & Identity | Password reset, role request, MFA | L1 |
| Payroll | Calculation query, slip issue, deduction | L2 |
| Leave & Attendance | Balance discrepancy, approval stuck | L1 → L2 |
| Integration | Treasury, civil registry, federation | L2 → L3 |
| Platform | Performance, outage, error | L2 → L3 |
| Data correction | Employee record, transfer, promotion | L2 |

## Metrics and reporting

| Metric | Target | Frequency |
|--------|--------|-----------|
| SLA achievement | ≥ 95 % after stabilization | Weekly |
| First Contact Resolution (FCR) | ≥ 40 % | Monthly |
| Backlog age (P1/P2 open > SLA) | 0 | Daily |
| CSAT | ≥ 4.0 / 5.0 | Monthly |
| Ticket misroute rate | ≤ 5 % | Monthly |

## Privacy in support

- Support attachments scanned for PII before storage.
- Ticket access limited by agency and role.
- Audit trail on all ticket views and modifications.

## Acceptance criteria

- SLA ≥ 95 % after stabilization
- Top 20 issues have knowledge articles
- Tickets do not get lost between teams
- FCR / backlog / age / CSAT measured
- Privacy controls on support attachments

## Verification

- Ticket flood simulation
- After-hours P1 drill
- Misroute / escalation test
- Support data privacy audit

## Dependencies

- REL-003
- SRE-001 (Done)
