# SRE-001 — On-call roster (24x7)

> Fill real names/phones before national go-live. Template slots below must remain gap-free.

## Tier model

| Tier | Role | Scope |
|---|---|---|
| Tier-0 | NOC / first responder | Monitoring ack, triage, page Tier-1 |
| Tier-1 | Platform / DB / Security on-call | Restore service, escalate IC |
| Tier-2 | Domain owners | HRCore, Payroll, Contracts, Gateway, IdP |

## Current rotation (placeholder — replace with staffed names)

| Week starting (UTC) | Tier-0 primary | Tier-0 backup | Tier-1 platform | Tier-1 security | Incident Commander pool |
|---|---|---|---|---|---|
| 2026-08-04 | NOC-A (TBD) | NOC-B (TBD) | SRE-A (TBD) | SOC-A (TBD) | SRE-A / Service Owner |
| 2026-08-11 | NOC-B (TBD) | NOC-C (TBD) | SRE-B (TBD) | SOC-B (TBD) | SRE-B / Service Owner |
| 2026-08-18 | NOC-C (TBD) | NOC-A (TBD) | SRE-A (TBD) | SOC-A (TBD) | SRE-A / Service Owner |

**Rule:** every Tier-0 skill has ≥2 qualified backups. Holidays covered by explicit swap.

## Handover

- Overlap 30 minutes at shift change.
- Open incidents + silenced alerts + change calendar reviewed.
- Access verified per [SRE_001_ACCESS_MATRIX.md](./SRE_001_ACCESS_MATRIX.md).

## Fatigue / compensation

- Max consecutive Tier-0 nights: 7.
- Comp time / allowance per org HR policy (link when approved).
