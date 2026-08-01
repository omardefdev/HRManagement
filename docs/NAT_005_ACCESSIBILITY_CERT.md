# NAT-005 — Accessibility, Usability & Field Acceptance Certification

**Card:** NAT-005  
**Status:** Done (certification harness + evidence template)  
**Priority:** National Gate / L  
**Owner:** Independent Accessibility + User Research

## Purpose

Define the accessibility, usability, and field acceptance certification pack: WCAG 2.2 AA, assistive technology, Arabic/RTL, low bandwidth, regional/role diversity, task success, error recovery, training burden, and satisfaction targets aligned with STR-007.

## Scope

- WCAG 2.2 AA for critical journeys (zero accessibility blockers in harness)
- Arabic RTL-first and low-bandwidth profiles
- Assistive technology (keyboard, screen reader, contrast)
- Moderated field studies with branch and disability representation (live = Blocked)
- Network lab certification for connection profiles
- STR-007 task success and time-on-task targets

## Data Model

See `docs/nat/nat005-checklist.json` for structured accessibility certification checklist.

## Business Rules

1. **Zero accessibility blockers** — harness baseline; live audit Blocked.
2. **Critical journeys AA** — employee self-service, manager approval, payroll inquiry, branch attendance.
3. **RTL-first Arabic** — primary language with official secondary language policy.
4. **Field representation** — regional/role diversity and disability inclusion required for live cert.
5. **Remediation/retest** — failed criteria require tracked closure before sign-off.

## Relationship to UX-002 / PROD-003..005 / STR-007 / SYR-013

| Card | Focus |
|------|-------|
| UX-002 | Accessibility/responsive baseline Done |
| PROD-003..005 | ESS/MSS/workbox production bindings |
| STR-007 | Value KPI baseline |
| SYR-013 | Syrian branch UX and governorate checklist Done |
| NAT-005 | Independent accessibility/field certification template |

## Acceptance Criteria Mapping

- [x] WCAG 2.2 AA checklist for critical journeys
- [x] RTL-first and low-bandwidth requirements
- [x] Field acceptance criteria and STR-007 target refs
- [x] Verification methods defined (automated, manual, field, network lab)
- [x] Live field studies and network lab = Blocked

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_005_ACCESSIBILITY_CERT.md` | This document |
| `docs/nat/nat005-checklist.json` | Accessibility certification checklist |
| `scripts/nat005-validate.ps1` | Validates NAT-005 artifacts |
| `Nat005AccessibilityCertTests.cs` | Unit tests for accessibility cert schema |

## Validation

- `scripts/nat005-validate.ps1`
- `Nat005AccessibilityCertTests`

## Remaining Live Blockers

- Moderated field studies with branch/disability representation pending
- Low-bandwidth network lab certification pending
