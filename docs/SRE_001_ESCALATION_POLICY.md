# SRE-001 — Escalation policy

## Severity → SLA

| Severity | Meaning | Ack | Incident Commander |
|---|---|---|---|
| P1 | Service down / payroll blocked / auth outage | ≤ 5 minutes | ≤ 10 minutes |
| P2 | Degraded / single agency severely impacted | ≤ 15 minutes | ≤ 30 minutes |
| P3 | Warning / non-urgent | ≤ 1 hour | as needed |

## Path

```
Alertmanager page
  → Tier-0 ack + classify
    → Tier-1 engage (platform/db/security)
      → Incident Commander (P1/P2)
        → Domain Tier-2 + vendor contacts
          → Exec / comms (SRE-003)
```

## Alert mapping

Alert rules live in `deploy/prometheus/alert_rules.yml`. Each P1/P2 alert must name:

- runbook link
- tier to page
- backup contact

## Vendor contacts

| Vendor / component | Contact | Escalate when |
|---|---|---|
| PostgreSQL host | TBD | DB unavailable > 5m |
| IdP/Keycloak host | TBD | login failures nationwide |
| Object storage | TBD | file download outage |
| Network / edge | TBD | Gateway unreachable |
