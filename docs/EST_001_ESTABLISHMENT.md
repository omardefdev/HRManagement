# EST-001 — Establishment Positions & Funded Position Table

**Card:** EST-001  
**Status:** Done  
**Priority:** P1-National / XL  
**Owner:** HRCore + Finance

## Overview

Implements the establishment (الملاك الوظيفي) domain — the official table of funded
positions that governs all staffing decisions. Every employee assignment must reference
a funded, non-frozen position. Position occupancy must never exceed FTE capacity.

## Domain Model

### JobCatalogEntry

Official job classification catalog. Each entry defines a job title, grade, and
occupational group.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `TenantId` | Guid | Agency/ministry boundary |
| `JobCode` | string(20) | Unique code within tenant |
| `TitleAr` | string(200) | Arabic job title |
| `TitleEn` | string(200) | English job title (optional) |
| `DescriptionAr` | string(2000) | Job description |
| `GradeCode` | string(20) | Grade/rank code |
| `OccupationalGroup` | string(100) | Functional classification |
| `IsActive` | bool | Whether the entry is current |

**Unique constraint:** `(TenantId, JobCode)`

### FundedPosition

A specific funded slot in the establishment table.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `TenantId` | Guid | Agency boundary |
| `PositionCode` | string(30) | Position identifier |
| `GradeCode` | string(20) | Grade/rank |
| `FTE` | decimal | Full-time equivalent (1.0 = full) |
| `BudgetYear` | int | Fiscal year |
| `Status` | enum | `Vacant`, `Occupied`, `Frozen` |
| `OrgUnitId` | Guid | Organizational unit (FK) |
| `JobCatalogEntryId` | Guid? | Job catalog reference (FK) |
| `OccupantEmployeeId` | Guid? | Current occupant (FK) |
| `CostCenter` | string(50) | Budget cost center |
| `FreezeReason` | string(500) | Why position is frozen |
| `FreezeDate` | DateTime? | When frozen |

**Unique constraint:** `(TenantId, PositionCode, BudgetYear)`

## Business Rules (EstablishmentService)

1. **No assignment without funded position** — `AssignEmployee` rejects if position is null, frozen, or already occupied.
2. **No over-capacity** — FTE must be > 0 for assignment.
3. **Freeze governance** — cannot freeze an occupied position; must vacate first.
4. **Unfreeze** — restores position to Vacant status.
5. **Vacate** — clears occupant, sets status to Vacant.
6. **HasFundedPosition** — validates that an employee's assignment is backed by a valid position.

## Migration

`20260731171000_EST001_Establishment.cs` — creates `JobCatalogEntries` and `FundedPositions` tables with indexes and foreign keys.

## Files

- `HR.Core.Domain/Entities/JobCatalogEntry.cs`
- `HR.Core.Domain/Entities/FundedPosition.cs`
- `HR.Core.Domain/Services/EstablishmentService.cs`
- `HR.Core.Infrastructure/Data/HRCoreDbContext.cs` (DbSets + configuration)
- `HR.Core.Infrastructure/Migrations/20260731171000_EST001_Establishment.cs`
- `HR.Core.Web/Controllers/JobCatalogController.cs` — CRUD for job catalog (central write)
- `HR.Core.Web/Controllers/FundedPositionsController.cs` — create/freeze/unfreeze funded positions
- `HR.Core.Web/Services/EstablishmentScopeService.cs` — shared branch scope filtering
- `GET /api/lookup/job-catalog` — searchable picker for catalog entries
- `HR.Core.Web/Controllers/EstablishmentChangesController.cs` — governed change requests
- `HR.Core.Infrastructure/Migrations/20260903215913_EST002_EstablishmentGovernance.cs`
- `scripts/est002-validate.ps1`
- `Est002EstablishmentGovernanceTests.cs`

## UI (PLAN-EST-A)

Operational screens under **الملاك والمناصب (EST-001)** in HR.Core:

| Screen | Central HR | Branch manager |
|--------|------------|----------------|
| Job catalog Index/Details/Create/Edit | Yes | Forbid |
| Funded positions Index/Details | Yes | Read-only (branch scope) |
| Create position / Freeze / Unfreeze | Yes | Forbid |

Vacant positions created via UI appear in **طلب شاغر جديد** (`GET /api/recruitment/vacant-positions`) only after an establishment change request reaches **Applied** status.

## Governance (PLAN-EST-B / EST-002)

Mutations from EST-A screens create **draft change requests** (`EstablishmentChangeRequest`) instead of writing live tables directly:

| Component | Purpose |
|-----------|---------|
| `EstablishmentChangeRequest` | Unified change request with `PayloadJson`, legal source, WF link |
| `EstablishmentChangeLog` | Append-only before/after snapshots on apply |
| `EstablishmentChangeLifecycleService` | Draft / submit / apply / cancel |
| `EstablishmentChangeApplier` | Applies approved payloads to catalog/positions |
| Policy `Core.Establishment` + WF `ESTABLISHMENT-CHANGE-APPROVAL` | Approval path when enforcement ≠ Off |
| `EstablishmentChangesController` | Index/Details + submit + **workflow approve/reject** (PLAN-24) |
| `EstablishmentChangeWorkflowUiService` | Workflow panel on Details (PLAN-24) |
| WorkInbox | Pending establishment changes with deep link to Details (PLAN-23/24) |

PLAN-25 (G-02): submit **always** starts `ESTABLISHMENT-CHANGE-APPROVAL` through `ApprovalStartCoordinator` — whatever `NationalScope:IsLabScale` or the policy mode (the policy may deny or start another workflow, never skip it). The request becomes `PendingApproval` only together with the instance; with no published definition the submit is refused («لا يوجد تعريف مسار اعتماد منشور لهذا الإجراء.») and nothing changes. The old «Off without IsLabScale → apply immediately» shortcut is removed. Completion (step 1 `hr-core`, step 2 `hr-management-approver`, enforced on the server; the creator never approves or rejects; central HR only) applies the change via the handler. A request left `PendingApproval` without an instance by an earlier build gets «إعادة بدء مسار الاعتماد» on its Details page or `scripts/hcm025-repair-stuck-approvals.ps1`.

## Temporal (PLAN-EST-C / EST-003)

Dated versions enable as-of queries without rewriting history:

| Component | Purpose |
|-----------|---------|
| `JobCatalogVersion` / `FundedPositionVersion` | ValidFrom/ValidTo snapshots (ORG-001 pattern) |
| `JobCatalogVersionService` / `FundedPositionVersionService` | Append on apply + occupancy |
| `EstablishmentAsOfService` | Catalog/position/unit as-of aggregation |
| `GET /api/establishment/*/as-of` | API for temporal reads |
| Org as-of fix | `OrgUnitVersionService` uses position versions (AC-14) |

## Planning (PLAN-EST-D / EST-004)

Workforce planning and establishment gap reports from funded positions:

| Component | Purpose |
|-----------|---------|
| `WorkforcePlanLine` | Structured plan targets (grade/catalog/count/FTE) |
| `EstablishmentMetricsService` / `EstablishmentGapAnalysisService` | Funded/occupied/vacant/frozen vs plan |
| `EstablishmentPlanningApiController` | `GET /api/establishment/gap-analysis` + plan gap |
| Gap report UI | WorkforcePlans/Details + GapReport |
| `FinanceBudgetImportBatch` / `FileFinanceBudgetPort` | Stub CSV import + cost-center reconciliation |

## Bulk import (PLAN-EST-F / EST-006)

Governed CSV import for job catalog entries:

| Component | Purpose |
|-----------|---------|
| `JobCatalogImportBatch` / `JobCatalogImportLine` | Parsed CSV audit trail |
| `FileJobCatalogPort` / `JobCatalogImportService` | Parse + MDM validate + draft change request |
| `EstablishmentChangeType.BulkCreateJobCatalog` | Single governance cycle for N catalog rows |
| `JobCatalog/Import` | Central HR upload UI + template download |

CSV columns: `TitleAr`, `TitleEn`, `DescriptionAr`, `GradeCode`, `JobClassificationCode`, `IsActive`, `LegacyJobCode` (Migration mode only).

Live external finance ERP integration remains deferred (CAP-EXT-01).

## MDM admin UI (PLAN-EST-G / EST-007)

Governed MVC screens for national reference codes used by job catalog pickers:

| Component | Purpose |
|-----------|---------|
| `ReferenceCatalogAdminService` | List/create/update/deactivate + impact analysis |
| `ReferenceCatalogController` + Views | Central HR UI for Grade + JobClassification |
| `ReferenceImpactAnalyzer` | JobCatalog grade/group counts before deactivate |
| Sidebar «المرجع الوطني (MDM)» | Governance navigation entry |

Platform MDM rows are read-only; agency overlays are created on the current tenant. See [PLAN-20](acceptance-plans/PLAN-20-establishment-mdm-admin-ui.md).
