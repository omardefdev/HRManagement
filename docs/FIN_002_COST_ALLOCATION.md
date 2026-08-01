# FIN-002 — Cost Allocation, Forecasting & Runaway Prevention

**Card:** FIN-002  
**Status:** Done  
**Priority:** P2-National / M  
**Owner:** FinOps + Platform

## Purpose

Define cost tagging, allocation rules, budgets, anomaly detection, unit metrics, forecasting, idle/waste identification, storage/log retention cost, chargeback/showback, and optimization guardrails that preserve SLO/DR commitments.

## Scope

- Tagging and allocation to environment/service/agency or shared rules
- Budget thresholds with alerts at 70/85/100%
- Daily anomaly detection
- Unit metrics aligned with FIN-001 TCO model
- Forecast with variance explanation (<10% or documented)
- Idle/waste and storage/log retention cost tracking
- Chargeback/showback policy
- Optimization guardrails — no optimization that breaks SLO/DR

## Data Model

See `docs/fin/cost-allocation-rules.json` for structured allocation rules, budgets, and guardrails.

## Business Rules

1. **100% cost allocated** — every cost tagged to environment/service/agency or shared allocation rule.
2. **Budget alerts at 70/85/100%** — escalating notification policy.
3. **Daily anomaly detection** — threshold-based with owner escalation.
4. **Forecast variance <10% or explained** — monthly review required.
5. **No optimization breaks SLO/DR** — guardrails enforced before change approval.
6. **Chargeback/showback transparent** — agency-level visibility.

## Relationship to FIN-001 / OBS-001

| Card | Focus |
|------|-------|
| FIN-001 | 5-year TCO model Done |
| OBS-001 | Observability stack Done |
| FIN-002 | Cost allocation, budgets, anomaly, forecast |

## Acceptance Criteria Mapping

- [x] Allocation rules for environment/service/agency
- [x] Budget thresholds 70/85/100%
- [x] Daily anomaly detection policy
- [x] Unit metrics and forecast variance rules
- [x] Optimization guardrails preserving SLO/DR
- [x] Storage/log retention cost tracking

## Artifacts

| File | Purpose |
|------|---------|
| `docs/FIN_002_COST_ALLOCATION.md` | This document |
| `docs/fin/cost-allocation-rules.json` | Cost allocation and budget rules |
| `scripts/fin002-validate.ps1` | Validates FIN-002 artifacts |
| `Fin002CostTests.cs` | Unit tests for cost allocation schema |

## Validation

- `scripts/fin002-validate.ps1`
- `Fin002CostTests`
