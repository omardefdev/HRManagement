# MDM-001 — منصة البيانات المرجعية الوطنية

## Overview

MDM-001 implements a **single-table national reference data catalog** that replaces scattered free-text fields and hard-coded enums with a governed, versioned, multilingual code list.

## Architecture

### Entity: `ReferenceCatalogEntry`
- **Location:** `src/Shared/HRManagement.SharedKernel/MasterData/ReferenceCatalogEntry.cs`
- Extends `AuditableEntity` (tenant-aware, audited)
- Discriminated by `CatalogKind` enum
- Code is unique per `(TenantId, CatalogKind, Code)`

### CatalogKind enum
Covers 12 national dimensions:

| Kind | Arabic | Use Case |
|------|--------|----------|
| JobClassification | التصنيف الوظيفي | تصنيف المسمى الوظيفي |
| Grade | الدرجة | الدرجة الوظيفية |
| Geography | الجغرافيا | المحافظات والمناطق |
| Agency | الجهة | الوزارات والهيئات |
| Qualification | المؤهل | المؤهلات العلمية |
| Bank | المصرف | المصارف المعتمدة |
| Currency | العملة | العملات |
| SalaryComponent | مكون الراتب | بنود الراتب |
| DocumentType | نوع الوثيقة | أنواع المستندات |
| ContractType | نوع العقد | أنواع العقود |
| MovementType | نوع الحركة | أنواع الحركات الوظيفية |
| ServiceType | نوع الخدمة | أنواع الخدمة |

### Fields per entry
`Code`, `NameAr`, `NameEn`, `NameFr`, `ValidFrom`, `ValidTo`, `ReplacedByCode`, `SourceAuthority`, `StewardRole`, `IsActive`, `Version`

## Admin UI (PLAN-20 / FR-MDM-03)

Central HR (`HrCoreWrite` + unrestricted org scope) manages **Grade** and **JobClassification** via MVC:

| Route | Screen |
|-------|--------|
| `GET /ReferenceCatalog?kind=Grade` | Index — search/filter |
| `GET /ReferenceCatalog/Create?kind=Grade` | Create new code |
| `GET /ReferenceCatalog/Edit/{id}` | Update names/dates (agency-owned only) |
| `GET /ReferenceCatalog/Details/{id}` | Details + impact table |
| `POST /ReferenceCatalog/Deactivate/{id}` | Deactivate (blocked when JobCatalog impact > 0) |

Platform tenant entries (`TenantDefaults.DefaultTenantId`) are **read-only** for agency users; new codes are created on the current agency tenant. Pickers consume `/api/lookup/reference-data` — no JS change required after create.

Service: `ReferenceCatalogAdminService` in Infrastructure; sidebar under **الحوكمة → المرجع الوطني (MDM)**.

## API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/reference-data?kind=Grade` | List entries by kind |
| GET | `/api/reference-data/{id}` | Get entry by ID |
| GET | `/api/reference-data/by-code?kind=Grade&code=GR-01` | Get entry by code |
| GET | `/api/reference-data/effective?kind=Grade&code=GR-01&asOf=2026-01-01` | Resolve effective entry following replacement chain |
| POST | `/api/reference-data` | Create new entry |
| PUT | `/api/reference-data/{id}` | Update entry |
| DELETE | `/api/reference-data/{id}` | Soft-delete (deactivate) |
| GET | `/api/reference-data/{id}/impact` | Analyze downstream impact |

## Lifecycle

### Deactivation & Replacement
- Entries are never hard-deleted; `IsActive = false` + `ValidTo` set
- `ReplacedByCode` creates a chain for effective-date resolution
- `ReferenceDataLifecycleService` enforces lifecycle rules

### Impact Analysis
- `ReferenceImpactAnalyzer` counts dependent rows before deactivation:
  - **Grade:** `JobCatalogEntries.GradeCode`
  - **JobClassification:** `JobCatalogEntries.OccupationalGroup`, `EmploymentRecords`, `CareerPathSteps`
- Admin UI and API reject deactivation when any affected count > 0 (v1)

### Events
- `IReferenceDataChanged` (MassTransit) published on create/update/deactivate
- `ReferenceDataChangedConsumer` logs changes for reconciliation

## Free-text FK Migration

`EmploymentRecord.JobClassificationCode` and `CareerPathStep.JobClassificationCode` are nullable string FKs to `ReferenceCatalogEntry.Code` (CatalogKind = JobClassification).

Migration script: `scripts/mdm001-migrate-freetext-to-codes.sql`

## Seed Data

`docker/seed/mdm-reference-seed.sql` contains sample Syrian:
- 6 grades (GR-01 through GR-EX)
- 7 job classifications (JC-ADM through JC-FIN)
- 14 governorates (GEO-DMS through GEO-HSK)

## Validation

```powershell
./scripts/mdm001-reference-validate.ps1
```

## Tests

`src/Tests/HRManagement.UnitTests/Mdm001ReferenceDataTests.cs`
