# STR-002 — Legislative change watch

Cadence for tracking Syrian legal/regulatory changes that affect the control catalog. Extends GOV-002 change-notice (payroll config) without replacing it.

| Field | Value |
|---|---|
| Owner | Legal (primary); Privacy / CISO / Payroll as domain owners |
| Cadence | Quarterly minimum + ad-hoc on gazette / MoF / privacy authority notice |
| Inputs | Official gazette, MoF circulars, file 12 updates, SYD status changes |
| Outputs | Catalog row updates; new/expired exceptions; card tickets (SYR/GOV/STR) |

## Process

1. Collect candidate changes (source + date + confidence A–D).
2. Map impact to `requirementId` / domain in control-catalog.
3. If financial formula changes → open GOV-002 decision revision + golden replay (do not edit silently).
4. If state decision (SYD) closes → flip Blocked-Legal → Partial/Implemented with evidence.
5. Record watch log entry (table below).

## Watch log (template)

| Date | Source | Impact domains | Catalog change | Ticket |
|---|---|---|---|---|
| 2026-07-30 | STR-002 baseline seed | all | Initial catalog | STR-002 |

## Handoff

- Payroll publish still requires LegalAttest (GOV-002).
- National corpus certification remains **SYR-001**.
