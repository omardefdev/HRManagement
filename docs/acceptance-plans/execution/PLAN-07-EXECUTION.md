# خطة التنفيذ التفصيلية — PLAN-07 (الرقم الوطني + Mass Assignment)

خطة تنفيذ قابلة للتطبيق لـ[PLAN-07 — الرقم الوطني + Mass Assignment](../PLAN-07-national-id-mass-assignment.md) (AC-26, AC-31). تعتمد على [PLAN-00](PLAN-00-EXECUTION.md).

## المخرجات النهائية

- بحث بالبصمة (`NationalIdHash`) في `EmployeesController`، `LookupApiController`، و`EmployeesApiController`.
- عرض full/masked عبر `EmployeeNationalIdPresentation` + سياسة `PiiViewNationalId`.
- `scripts/ac026-scan-pii.ps1` + ربط CI عند `RUN_ACCEPTANCE=true`.
- ViewModels للوجستيات (7 controllers) + `PayrollRunFormViewModel`.
- `MassAssignmentArchitectureTests` = 0 violations.
- `MassAssignmentHttpTests` + `Ac26NationalIdPrivacyTests` + `Ac31MassAssignmentTests`.

## W7.1 — AC-26: بحث + عرض

| خطوة | ملف | تحقق |
|------|-----|------|
| سياسة PII | `HrManagementPolicies.PiiViewNationalId` | Admin أو hr-core (لا branch-manager وحده) |
| Helper | `EmployeeNationalIdPresentation.cs` | branch-manager ⇒ masked حتى مع hr-core |
| Hash search | `NationalIdSearch.cs`, controllers | لا `NationalId.Contains` |
| API | `EmployeesApiController` | `by-national-id-hash`, `search?nationalId=` |
| Views | `Details.cshtml`, `Index.cshtml` | `DisplayNationalId` |

**اختبارات:** `NationalIdSearchTests`, `NationalIdDisplayPolicyTests`.

## W7.2 — AC-26: فحص DB/السجلات

```powershell
pwsh scripts/ac026-scan-pii.ps1
# => ac026-scan-report.json, exit 0
```

يفحص: عدم وجود `[0-9]{9,11}` في `Employees.NationalId`؛ `NationalIdHash` غير فارغ؛ بصمات البذرة؛ عدم ظهور معرفات البذرة في logs `hrm-hr-core-web`.

## W7.3 — AC-31: ViewModels اللوجستيات

`LogisticsMassAssignmentViewModels.cs` + POST actions على VMs في:

- `InventoryItemsController`
- `SupplyOrdersController`, `VendorsController`, `AssetAssignmentsController`
- `MaintenanceRecordsController`, `ReceiptRecordsController`, `AssetsController`

**اختبار معماري:**

```bash
dotnet test --filter MassAssignmentArchitectureTests
```

## W7.4 — AC-31: HTTP hidden-field tests

`MassAssignmentHttpTests.cs`:

- POST `/InventoryItems/Create` + `TenantId` evil ⇒ DB tenant = server
- `[Bind]` على `PayrollRunsController.AddSalaryLine` يستبعد `NetPay`/`TenantId`

## W7.5 — قبول حي

| AC | اختبار | أدلة |
|----|--------|------|
| AC-26 | `Ac26NationalIdPrivacyTests` | `acceptance-evidence/AC-26/` |
| AC-31 | `Ac31MassAssignmentTests` | `acceptance-evidence/AC-31/` |

شخصيات: `central-a` (full PII + admin-logistics لـ AC-31)، `br1-mgr` (masked).

## تعريف الإنجاز

- بحث بالبصمة (UI + API)
- عرض full/masked حسب الدور
- `ac026-scan-pii.ps1` Pass على حزمة القبول
- `MassAssignmentArchitectureTests` + `MassAssignmentHttpTests` Pass
- Logistics POST لا يربط entities
- `acceptance-evidence/AC-26/` و `AC-31/` = Pass
