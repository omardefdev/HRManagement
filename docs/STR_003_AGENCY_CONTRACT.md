# STR-003 — Agency contract (what you get / don’t get)

Walkthrough template for onboarding / Pilot agency. Machine source: [`str-modules/module-catalog.json`](str-modules/module-catalog.json).

| Field | Value |
|---|---|
| Agency id | (e.g. PILOT-AGENCY-001) |
| Contract date | |
| Product release digest | |

## Included (available)

Agency-visible modules with `readinessStatus=Ready` and `serviceTier` in core|extended.

| moduleId | availability | serviceTier | Notes |
|---|---|---|---|
| hr-core | mandatory | core | |
| hr-contracts | mandatory | core | |
| hr-movements | mandatory | core | |
| payroll | mandatory | core | |
| records-archive | mandatory | core | |
| auth-directory | mandatory | core | |
| file-storage | mandatory | core | |
| hr-health | optional | extended | |
| hr-training | optional | extended | |
| idcard | optional | extended | |
| admin-logistics | optional | extended | |
| it-infra | optional | extended | |
| dev-analytics | optional | extended | |
| compliance | optional | extended | |

Optional modules may be omitted from a specific agency entitlement list without forking the product (STR-004).

## Excluded / not agency UI

| Item | Reason |
|---|---|
| postgres / rabbitmq | Platform tier — operated by provider, not a tenant module tile |
| Commercial license marketplace | Out of depth / future |
| Per-agency source forks | Forbidden (STR-004) |
| National 24×7 desk | SUP-002 |
| ONB factory automation | ONB-001 |
| Any NotReady/Sunset module | Not available until Ready |

## Walkthrough checklist

1. Review Included table with agency sponsor.
2. Confirm optional picks for this agency.
3. Confirm Excluded understood (no silent expectations).
4. Confirm feature flags / entitlement will not expose Excluded or NotReady.
5. Sign UAT pack roles.
