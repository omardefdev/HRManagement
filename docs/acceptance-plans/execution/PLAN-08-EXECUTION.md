# خطة التنفيذ التفصيلية — PLAN-08 (تهيئة الجهة + الحجر والمصالحة)

خطة تنفيذ قابلة للتطبيق لـ[PLAN-08 — تهيئة الجهة + الحجر والمصالحة](../PLAN-08-onboarding-quarantine.md) (AC-07, AC-08). تعتمد على [PLAN-00](PLAN-00-EXECUTION.md).

## المخرجات النهائية

- `AgencyOrgStructureSection` + `AgencyOrgStructureProvisioner` (idempotent org tree).
- Early exit على `(TenantId, PackId, ReleaseDigest)` + نشر `IAgencyOnboarded`.
- `TenantQuarantineGuard` + `TenantQuarantineController` (resolve API).
- حراسة `EmployeesController` Edit/Delete + `PATCH api/employees/{id}`.
- `scripts/acceptance-quarantine-flow.ps1`.
- `Ac07AgencyOnboardingTests` + `Ac08QuarantineReconciliationTests`.

## W8.1 — AC-07

| خطوة | ملف | تحقق |
|------|-----|------|
| Manifest org | `AgencyManifestModels.cs`, `agency-ministry-finance.json`, `agency-acceptance-test.json` | validation roots/cycles |
| Provisioner | `AgencyOrgStructureProvisioner.cs` | idempotent by Code |
| Pipeline | `AgencyProvisioningService.cs` | AlreadyApplied + event |
| Contract | `IAgencyOnboarded.cs` | MassTransit publish |

**اختبارات:** `AgencyProvisioningTests` (org tree, re-apply, event).

## W8.2 — AC-08

| خطوة | ملف | تحقق |
|------|-----|------|
| Guard | `TenantQuarantineGuard.cs`, `HrCoreTenantQuarantineQuery.cs` | open entry ⇒ throw |
| API | `TenantQuarantineController.cs` | GET list/detail, POST resolve |
| MVC guard | `EmployeesController.cs` | Edit/Delete ⇒ 409 |
| REST guard | `EmployeesApiController.cs` | PATCH ⇒ 409 |

**اختبارات:** `TenantQuarantineGuardTests`, `TenantQuarantineIntegrationTests`.

## W8.3 — قبول حي

| AC | اختبار | أدلة |
|----|--------|------|
| AC-07 | `Ac07AgencyOnboardingTests` | `acceptance-evidence/AC-07/` |
| AC-08 | `Ac08QuarantineReconciliationTests` + `acceptance-quarantine-flow.ps1` | `acceptance-evidence/AC-08/` |

شخصيات: `platform-admin` (apply + resolve)، `central-a` (blocked PATCH).

## تعريف الإنجاز

- Manifest ينشئ org tree idempotent + `AgencyOnboarded` published.
- Re-apply لا يُضاعف الكيانات.
- Quarantine guard يمنع edit على سجل محجور.
- Resolve API يكتب قراراً موثّقاً.
- `acceptance-evidence/AC-07/` و `AC-08/` = Pass.
