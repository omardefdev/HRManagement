# CAP-002 — Load Protection and Fair Degradation

## Purpose

Prevent a single tenant from starving others under load. Admission control enforces per-tenant concurrency and rate limits; critical journeys (payroll, identity) get priority via separate quota tiers. 200% load must not cause cascading failure or OOM.

## Scope

- Per-tenant quotas (concurrency + rate)
- Tenant fairness / noisy-neighbor prevention
- Admission control and backpressure
- Load shedding and bounded queues
- Priority for payroll / identity journeys
- Retry budgets and cache stampede guard
- Overload UX (clear rejection with safe retry)

## Key Component

| Component | Location | Role |
|-----------|----------|------|
| `TenantAdmissionControl` | `ServiceDefaults/Capacity/` | In-memory quota check with sliding-window rate limiting |

## Configuration

- `docs/cap/tenant-quota-policy.json` — default and per-tier quota definitions

## Design Decisions

1. **In-memory first** — no external dependency for admission control; distributed cache can be added for multi-instance.
2. **Sliding window** — one-minute rate window prevents bursts without hard resets.
3. **Explicit release** — callers must release slots; timeout/circuit-breaker can force release.
4. **Tier-based quotas** — payroll/identity get higher limits; configurable per tenant.

## Acceptance Criteria

- One tenant does not starve others
- 200% load does not cause cascading failure or OOM
- Critical journeys remain within tier objective
- Recovery backlog clears within defined time
- Rejection is explicit and safely retryable

## Validation

- `scripts/cap002-validate.ps1`
- `Cap002LoadProtectionTests`
