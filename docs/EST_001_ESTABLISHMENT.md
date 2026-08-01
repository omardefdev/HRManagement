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
- `scripts/est001-validate.ps1`
- `Est001EstablishmentTests.cs`
