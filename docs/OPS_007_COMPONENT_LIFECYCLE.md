# OPS-007 — Component Lifecycle, Patching, and Certificates

**Card:** OPS-007  
**Status:** Done (component inventory + patch SLA policy + certificate alert windows)

## Purpose

Maintain a complete inventory of all platform components with lifecycle status, enforce patch SLAs to eliminate EOL and unpatched software, and ensure certificate/secret expiry is tracked with proactive alerting.

## Component inventory

Machine-readable inventory: [`ops007/component-inventory.json`](ops007/component-inventory.json)

Every component in the platform must be tracked with:

- Name, version, and vendor/project
- Tier classification (Tier-0 / Tier-1 / Tier-2)
- EOL date (known or estimated)
- Current patch level
- Upgrade path (N → N+1)
- Owner responsible for lifecycle

## Patch SLA policy

Machine-readable policy: [`ops007/patch-sla-policy.json`](ops007/patch-sla-policy.json)

| Vulnerability class | SLA | Exception process |
|---------------------|-----|-------------------|
| Exploited Critical (CISA KEV / zero-day) | ≤ 24 hours or approved compensating control | CISO sign-off within 4 hours |
| Critical (CVSS ≥ 9.0) | ≤ 7 days | Risk acceptance with expiry |
| High (CVSS 7.0–8.9) | ≤ 30 days | Standard change |
| Medium (CVSS 4.0–6.9) | ≤ 90 days | Batched in maintenance window |
| Low (CVSS < 4.0) | Next scheduled upgrade | — |

## Certificate and secret expiry

Alert windows for certificate and secret expiry (per SOV-003):

| Alert window | Action |
|-------------|--------|
| 60 days | Informational — plan renewal |
| 30 days | Warning — initiate renewal |
| 15 days | Urgent — escalate if not renewed |
| 7 days | Critical — page on-call, emergency renewal |

## EOL policy

- Zero EOL components without a documented exception.
- Exception requires: risk assessment, compensating controls, time-bound plan, owner.
- Quarterly EOL sweep of the full inventory.
- Components approaching EOL (< 6 months) flagged in weekly ops review.

## Upgrade and rollback

- Every critical component must have a tested N-1 → N upgrade path.
- Rollback procedure documented and tested for each upgrade.
- Upgrades must not break SLO targets.
- Compatibility lab validates upgrades before production.

## Maintenance windows

- Patch deployments aligned with REL-006 freeze windows.
- Emergency patches follow the hotfix path (REL-006).
- All patches recorded in the change management system.

## Acceptance criteria

- Zero EOL components without approved exception
- Exploited Critical patched ≤ 24 h or compensating control approved
- Certificate alert windows at 60/30/15/7 days
- Every critical component upgrade tested without SLO break
- Inventory accurate and current

## Verification

- Emergency patch drill
- Expired certificate drill
- N-1 → N upgrade and rollback exercise

## Dependencies

- SUP-001
- DB-001
- REL-004 (Done)
