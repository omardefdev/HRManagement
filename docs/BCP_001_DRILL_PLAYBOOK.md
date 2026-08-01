# BCP-001 — 8-hour business continuity drill playbook

**Card:** BCP-001  
**Scenario:** Simulated total platform outage (Gateway + Keycloak unreachable) during active pay period.

## Objectives

- Execute Tier-0 manual procedures for ≥8 hours
- Capture paper backlog with traceable form IDs
- Re-enter and reconcile without duplication
- Measure RTO against ARC-001 Tier-0 targets

## Participants

| Role | Count |
|------|-------|
| Incident Commander | 1 |
| HR Finance lead | 1 |
| Branch manager (pilot site) | 1 |
| HR operations (data entry) | 2 |
| Internal audit observer | 1 |
| Comms liaison | 1 |

## Timeline (T0 = outage declared)

| Time | Activity |
|------|----------|
| T0 | IC declares drill; disable pilot branch VPN to platform (simulated) |
| T0+30m | Notify agency heads; distribute paper forms from branch safe |
| T0+2h | Process 5 synthetic Tier-0 leave requests (Form L-01) |
| T0+4h | Complete emergency payroll run sheet for 20 synthetic employees |
| T0+6h | Identity check: temp roster vs paper list |
| T0+8h | IC declares platform restored (simulated) |
| T0+8h–12h | Re-entry per [BCP_001_REENTRY_RECONCILIATION.md](BCP_001_REENTRY_RECONCILIATION.md) |
| T0+12h | Audit sample + hot wash |

## Inject cards (optional)

- **T0+3h:** Comms liaison unavailable — backup uses SMS tree
- **T0+5h:** Duplicate Form L-01 submitted — test duplicate detection
- **T0+10h:** Payroll total mismatch by 1 unit — test IC escalation

## Metrics

| Metric | Target |
|--------|--------|
| Tier-0 procedure start | ≤ 4h from T0 |
| Paper forms traceable | 100% numbered |
| Re-entry duplicates | 0 |
| Re-entry completion | ≤ 4h after restore |

## Evidence

- Signed attendance sheet for drill participants
- Scanned form bundle with SHA-256 manifest
- Reconciliation spreadsheet
- Hot-wash notes → risk register if gaps found

## Frequency

- Tabletop: quarterly
- Full 8h drill: annually per pilot branch; rotate branches each year

## Related

- [BCP_001_MANUAL_PROCEDURES.md](BCP_001_MANUAL_PROCEDURES.md)
- [docs/bcp/degraded-mode-triggers.json](bcp/degraded-mode-triggers.json)
- SRE-001 on-call escalation
