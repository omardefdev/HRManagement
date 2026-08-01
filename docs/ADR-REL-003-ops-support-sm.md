# ADR-REL-003 — Ops support & service management baseline

| Field | Value |
|---|---|
| Status | Accepted |
| Card | REL-003 (card 58) |
| Date | 2026-07-30 |
| Dependencies | OBS-002 Done; OPS-004 Done |
| Context predecessor | REL-002 Done (pilot gate pack) |

## Context

OBS-002 provides SLO alerts and alert runbooks; OPS-004 provides DR IC/tree and drills. There is no SM service catalog (owner + runbook + escalation per service), no documented on-call that closes the OBS escalation gap, no unified incident/problem/change process with MTTA/MTTR measurement, and no access-review / platform patch / blameless postmortem pack under REL-003.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/rel-ops/service-catalog.json` — journeys ServiceCatalog / IncidentChange / OnCallGameDay / AccessPatchPostmortem |
| Depth | **Ops SM baseline pack** — not national 24x7 desk (SRE-001 / SUP-002) |
| Catalog | One row per critical OBS inventory host + postgres/rabbitmq; required: ownerRole, runbookPath, escalationPath, onCallTier, sloRef |
| Severity/SLA | Sev1–Sev4 in `REL_003_SEVERITY_SLA.md` with MTTA/MTTR targets |
| Change rollback | Cite DEP-001 `action=rollback`; do not rebuild deploy pipeline |
| On-call test | Game-day script + ledger (no mandatory PagerDuty) |
| Status | Agency/pilot announcement template + `obs-silence-maintenance`; not national status page |
| Access / patch / PM | Templates + CSV trackers; OPS-007 / TRN-001 / SUP-004 remain out of depth |
| Closure | Done = pack + harness + Rel003*; TRACEABILITY `REL-003 ✓` |
| Out of scope | SRE-001..003, SUP-002..004 full, OPS-007 national, NAT status page, Aspire, rebuild OBS/OPS |

## Consequences

- OBS escalation text points at REL-003 on-call / incident docs.
- SUP-002 and national SRE may consume this baseline later; REL-003 does not claim they are Done.
- Missing catalog fields or game-day MTTA without waiver fails Rel003* / script gate.
