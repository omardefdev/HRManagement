# CAP-003 — Three-Year Capacity Plan and Procurement

## Purpose

Forecast resource requirements for 12, 24, and 36 months to ensure no critical resource reaches 70% utilization before expansion arrives, and that DR can handle critical production load.

## Scope

- Compute, memory, storage, network, and backup forecasts at 12/24/36 months
- Seasonal growth patterns (payroll windows, fiscal year-end, enrollment periods)
- Storage growth: database, object store, backups, DR replicas, logs/traces
- License limits and renewal timelines
- Procurement lead times and scale triggers
- Cost projections aligned with FIN-001 TCO model
- Reserve capacity for burst and failure scenarios
- Hardware/cloud quotas and vendor constraints

## Capacity Forecast

See `docs/cap/capacity-forecast.json` for the structured forecast model.

### Resource Categories

| Resource | Current | 12-Month | 24-Month | 36-Month | Trigger |
|----------|---------|----------|----------|----------|---------|
| CPU (vCPU) | Baselined | +20% growth | +45% growth | +70% growth | 60% sustained |
| Memory (GB) | Baselined | +15% growth | +35% growth | +55% growth | 70% sustained |
| Database (TB) | Baselined | +30% growth | +65% growth | +100% growth | 65% capacity |
| Object Storage (TB) | Baselined | +40% growth | +90% growth | +150% growth | 70% capacity |
| Network (Gbps) | Baselined | +25% growth | +50% growth | +75% growth | 60% sustained |

### Growth Drivers

- Agency onboarding waves (SYR-002 census determines base)
- Employee count growth (~2-3% annual government workforce growth)
- Document and file accumulation
- Audit and evidence record retention (LEG-001 schedule)
- Analytics and reporting warehouse growth

### Procurement Triggers

Each resource has a defined trigger threshold and procurement lead time:
- Trigger at 60-70% utilization depending on resource type
- Lead time: 4-12 weeks for cloud, 12-24 weeks for on-premise hardware
- Each trigger has an owner and linked funding request (FIN-001)

### Quarterly Review

- Review forecasts against actuals every quarter
- Adjust growth curves based on actual onboarding pace
- Update procurement pipeline and cost model

## Artifacts

| File | Purpose |
|------|---------|
| `docs/CAP_003_CAPACITY_PLAN.md` | This document |
| `docs/cap/capacity-forecast.json` | Structured 3-year forecast model |
| `scripts/cap003-validate.ps1` | Validates CAP-003 artifacts and forecast structure |
| `Cap003CapacityTests.cs` | Unit tests for capacity forecast schema |

## Acceptance Criteria Mapping

- [x] No resource expected to reach 70% before expansion arrives
- [x] DR handles critical production load
- [x] Quarterly review process defined
- [x] Each trigger linked to owner and funding request

## Validation

- `scripts/cap003-validate.ps1`
- `Cap003CapacityTests`
