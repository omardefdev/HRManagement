# REL-005 — Progressive Delivery and Safe Rollback

**Card:** REL-005  
**Status:** Done (canary gates + rollback policy + progressive delivery framework)

## Purpose

All production deployments follow a progressive delivery model — canary or blue-green — with automated health and business gates. Rollback must complete within 15 minutes for any service. No deployment may corrupt schema, events, or payroll data.

## Deployment strategies

| Strategy | Use case | Gate type |
|----------|----------|-----------|
| Canary | Default for all services | Automated health + business |
| Blue-green | Database-coupled or stateful workloads | Manual approval + automated |
| Feature flags | Tenant/region-scoped rollout | Flag evaluation per cohort |

## Canary rollout stages

Machine-readable gates: [`rel/canary-gates.json`](rel/canary-gates.json)

| Stage | Traffic % | Duration | Gate |
|-------|-----------|----------|------|
| 1 — Smoke | 1 % | 5 min | Error rate < 0.1 %, latency p99 < 2× baseline |
| 2 — Canary | 5 % | 15 min | Error rate < 0.5 %, no payroll invariant violation |
| 3 — Expand | 25 % | 30 min | SLO within budget, no data anomaly |
| 4 — Majority | 75 % | 30 min | All gates green |
| 5 — Full | 100 % | — | Promoted |

## Automated gates

### Health gates

- Error rate (5xx) below threshold
- Latency p50 / p95 / p99 within SLO
- No container restarts or OOM events
- No increase in log error volume

### Business gates

- Payroll calculation invariant: `gross - deductions = net`
- No duplicate disbursement events
- Attendance record count stable (no drops)
- No schema migration failure

## Abort and rollback

| Trigger | Action | Target |
|---------|--------|--------|
| Any gate fails | Automatic rollback | Previous stable version |
| Manual abort | IC or release authority stops rollout | Previous stable version |
| Error budget exhausted | Block further rollouts until budget recovers | N/A |

### Rollback SLA

- Service rollback: ≤ 15 minutes
- Database rollback: within expand/migrate/contract window (REL-004)
- Event schema: additive-only within minor; no breaking removal

## Feature flags

- Tenant and region cohorts supported.
- Flags evaluated at request time, not deploy time.
- Kill switch for immediate disable.
- Flag lifecycle: create → canary → GA → sunset → remove.

## Cohort management

| Cohort | Description |
|--------|-------------|
| Internal | Platform team only |
| Pilot agencies | Early adopter agencies per REL-007 |
| Region | Geographic rollout group |
| Full | All tenants |

## Audit and approval

- Every rollout logged with: digest, cohort, gate results, approver.
- Promotion from canary to full requires automated gate pass.
- Manual approval required for blue-green database deployments.
- Audit trail retained per GOV-001.

## Acceptance criteria

- Rollout on stages; no big-bang deployment
- Error budget or business invariant breach halts rollout automatically
- Rollback ≤ 15 min for services
- No schema / event / payroll corruption
- Known digest for every cohort

## Verification

- Bad-release injection (error rate spike)
- Failed migration rollback
- Canary signal validation
- Rollback under load

## Dependencies

- DEP-001
- REL-004 (Done)
- OBS-002
