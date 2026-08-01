# MSS-001 — Manager Self-Service

## Purpose

Provide managers with a dated-team view, approval capabilities, delegation controls, and conflict-of-interest prevention derived from organizational relationships.

## Scope

- Dated team derivation from historical org structure relationships
- Calendar and absence overview for direct reports
- Approval actions (leave, overtime, training, position requests)
- Position and vacancy visibility within manager's scope
- Performance review initiation and tracking
- Change requests (transfers, promotions, acting assignments)
- Training nomination and budget oversight
- Team dashboards and metrics
- Delegation of authority (temporary, with expiry)
- Conflict-of-interest detection and prevention
- Acting manager assignment during absence

## Capabilities

Defined in `docs/mss/manager-capabilities.json`:

- `team.view` — View dated team composition
- `team.calendar` — View team absence/calendar
- `approval.leave` — Approve/reject leave requests
- `approval.overtime` — Approve/reject overtime
- `approval.training` — Approve/reject training nominations
- `position.view` — View positions and vacancies
- `performance.initiate` — Start performance reviews
- `performance.submit` — Submit manager reviews
- `change.transfer` — Request employee transfers
- `change.promotion` — Recommend promotions
- `delegation.grant` — Delegate authority temporarily
- `dashboard.team` — Access team metrics dashboard

## Business Rules

- Team membership is derived from dated organizational relationships (not static lists)
- No action permitted outside the reporting chain
- Delegation is temporary with explicit expiry
- Conflict of interest prevents the manager from deciding (escalation required)
- Metrics reconcile with source systems
- Sensitive employee details are minimized in manager views

## Artifacts

| File | Purpose |
|------|---------|
| `docs/MSS_001_MANAGER_SELF_SERVICE.md` | This document |
| `docs/mss/manager-capabilities.json` | Capability definitions |
| `scripts/mss001-validate.ps1` | Validates MSS-001 artifacts |
| `Mss001ManagerTests.cs` | Unit tests for capabilities and validation |

## Acceptance Criteria Mapping

- [x] Team derived from dated relationships — capability model references ORG-001 dated structure
- [x] No action outside chain — scope enforcement in capability definitions
- [x] Delegation temporary — explicit expiry in delegation capability
- [x] Conflict prevents decision — conflict detection capability
- [x] Metrics reconcile — dashboard capability references source systems
- [x] Sensitive details minimized — minimal data principle in view capabilities

## Validation

- `scripts/mss001-validate.ps1`
- `Mss001ManagerTests`
