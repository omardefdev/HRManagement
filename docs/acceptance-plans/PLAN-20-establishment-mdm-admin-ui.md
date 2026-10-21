# PLAN-20 — Establishment MDM Admin UI (PLAN-EST-G)

**Scope:** FR-MDM-03 — واجهة MVC لإدارة رموز الدرجات والمجموعات المهنية  
**Status:** Implemented  
**Depends on:** PLAN-18 (EST-E) + PLAN-19 (EST-F)  
**Related cards:** EST-001, MDM-001, COD-001

## Goal

تمكين **الموارد البشرية المركزية** من عرض وإنشاء وتعديل وتعطيل رموز `Grade` و `JobClassification` في المرجع الوطني، مع تحليل أثر على `JobCatalogEntries` قبل التعطيل.

## In scope

- `ReferenceCatalogAdminService` + توسيع `ReferenceImpactAnalyzer` (JobCatalog)
- `ReferenceCatalogController` + ViewModels + Views (Index/Create/Edit/Details)
- Sidebar «الحوكمة» → «المرجع الوطني (MDM)»
- RBAC: قراءة/كتابة مركزية عبر `ValidateCentralWriteAsync`؛ سجلات platform للقراءة فقط
- AC-EST-18 + `est007-validate.ps1` + Est007 unit tests

## Out of scope

- باقي أنواع `CatalogKind` (Geography, Bank, …)
- workflow اعتماد لتغييرات MDM
- نشر `IReferenceDataChanged` من API (تحسين لاحق)
- ترحيل أكواد acceptance القديمة (`G5` → `GR-05`)

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-EST-18a | central-a ينشئ `GR-16` → يظهر في picker الدرجة | — |
| AC-EST-18b | central-a ينشئ `JC-HR` → يظهر في picker المجموعة المهنية | — |
| AC-EST-18c | — | branch manager يحصل Forbid على Create |
| AC-EST-18d | — | تعطيل درجة مستخدمة في JobCatalog → مرفوض + رسالة أثر |

## Verification

```powershell
powershell -File scripts/est007-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Est007
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcEst18
```
