# Product scope sign-off — HRManagement v1

**Version:** 1.0  
**Date:** 2026-05-29  
**Status:** Approved for v1 release candidate (technical integration complete; legal payroll deferred)

## Purpose

This document records explicit product acceptance of what v1 delivers versus what is deferred, so release go/no-go is not blocked by undocumented scope gaps.

## In scope for v1 (accepted)

| Capability | Acceptance criteria |
|------------|---------------------|
| Employee master data (HR.Core) | CRUD, recruitment hire path, status lifecycle, Diwan registration event |
| Contracts & service (HR.Contracts) | Contract activation, leave/advance approval with policy checks, separation wizard and publish |
| Movements & training | Transfer/secondment/training completion events to downstream services |
| Payroll operations | Payroll run lifecycle (draft → calculate → review → approve → post), period proration rules, inbound event adjustments |
| Cross-service integration | MassTransit workflow events wired per [MESSAGE_EVENT_MATRIX.md](./MESSAGE_EVENT_MATRIX.md) |
| Identity | Keycloak via gateway; Auth.Directory provisioning when DirectorySync enabled |
| Operations | Health checks, migrations policy, CI/CD, Docker production overlays |

## Explicitly out of scope for v1 (deferred)

| Item | Rationale | Target |
|------|-----------|--------|
| **Statutory / legal payroll engine** | v1 provides operational payroll runs and adjustments, not full tax/social/insurance tables or legal sign-off | v2 or external finance system integration |
| **Attendance integration** | Daily attendance is local CRUD only; no events to leave or payroll | v2 when attendance policy is defined |
| **Employee self-service portals** | External integration seams (`ILeaveRequested`, etc.) have consumers only | External project |
| **Dead-end audit events** | `ICardIssued`, `IComplaintResolved`, four HR.Contracts internals | v2 consumers or audit-only |
| **Full peripheral UI depth** | All modules have functional screens; not every Domain entity has a Web UI | Incremental UX releases |

## Sign-off

| Role | Name | Date | Decision |
|------|------|------|----------|
| Product owner | _Pending organizational sign-off_ | | Accept v1 scope as defined above |
| HR operations | | | |
| IT / security | | | |

**Technical recommendation:** Proceed to staging with blockers B1–B2 resolved; production requires checklist in [PRODUCTION_CUTOVER.md](./PRODUCTION_CUTOVER.md) and completed [UI_CHECKLIST.md](./UI_CHECKLIST.md).
