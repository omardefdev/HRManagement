# SRE-002 — Operational Readiness Review and Actionable Runbooks

**Card:** SRE-002  
**Status:** Done (ORR checklist + Tier-0 runbook templates binding)

## Purpose

Every Tier-0/1 service must pass an Operational Readiness Review before receiving production traffic. Each alert must have a runbook that an on-call engineer — who did not build the service — can execute end to end.

## ORR scope

| Area | Requirement |
|------|-------------|
| Service owner | Named individual with pager rotation |
| SLO / dashboard / alerts | Defined, instrumented, and validated |
| Dependency map | All upstream/downstream documented |
| Capacity | Headroom ≥ 30 % per CAP-001 |
| Deploy / rollback | Tested ≤ 15 min rollback per REL-005 |
| Backup / restore | Validated per OPS-003/004 |
| Common failures | Runbook per Tier-0 failure mode |
| Security | Controls per SOV-001 |
| Maintenance windows | Documented, SLO-aligned |
| Support | Tier mapping and escalation per SUP-002 |
| Known errors | Documented with workarounds |

## ORR checklist

Machine-readable template: [`sre/orr-checklist-template.json`](sre/orr-checklist-template.json)

Each service must fill in the checklist and obtain sign-off from:

1. Service Owner
2. SRE Lead
3. Security Representative
4. Capacity / Performance Lead

A service **must not** receive production traffic until the ORR is signed.

## Runbook standards

Every runbook must include:

1. **Title and alert link** — the Alertmanager rule that triggers it
2. **Severity** — P1 / P2 / P3
3. **Impact** — user-facing and data-integrity consequences
4. **Diagnosis** — step-by-step triage commands
5. **Mitigation** — immediate actions to restore service
6. **Escalation** — who to page if mitigation fails
7. **Post-resolution** — cleanup, evidence, postmortem trigger

## Tier-0 failure runbooks

| Runbook | File | Trigger |
|---------|------|---------|
| Database connection failure | [`runbooks/db-connection-failure.md`](../runbooks/db-connection-failure.md) | `PostgresConnectionPoolExhausted` or `PostgresUnreachable` |
| Message queue backup | [`runbooks/queue-backup.md`](../runbooks/queue-backup.md) | `RabbitMQQueueDepthCritical` or `MassTransitConsumerLag` |
| Out of memory | [`runbooks/oom-kill.md`](../runbooks/oom-kill.md) | `ContainerOOMKilled` or `PodEvicted` |
| Certificate expiry | [`runbooks/cert-expiry.md`](../runbooks/cert-expiry.md) | `TLSCertExpiringSoon` (60/30/15/7 day windows) |

## Acceptance criteria

- 100 % Tier-0/1 services pass ORR
- On-call engineer who did not author the service executes each runbook successfully
- No alert exists without a linked runbook
- New services do not receive traffic before ORR sign-off

## Verification

- Shadow on-call drill
- Random runbook execution drill
- ORR audit (quarterly)

## Dependencies

- ARC-001 (Satisfied — production criticality binding)
- SRE-001 (Done)
- REL-003
