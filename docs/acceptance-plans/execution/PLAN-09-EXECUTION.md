# خطة التنفيذ التفصيلية — PLAN-09 (المصالحة + عزل قواعد البيانات)

خطة تنفيذ قابلة للتطبيق لـ[PLAN-09 — المصالحة + عزل قواعد البيانات](../PLAN-09-reconciliation-db-isolation.md) (AC-19, AC-20). تعتمد على [PLAN-00](PLAN-00-EXECUTION.md).

## المخرجات النهائية

- `ReadModelReconciliationService` — detect + correct + report (dry-run supported).
- `AnalyticsReconciliationApiController` — POST reconcile, GET status.
- `scripts/acceptance-analytics-reconcile.ps1` + `Ac19AnalyticsReconciliationTests`.
- `scripts/apply-postgres-service-roles.ps1` + `acceptance-verify-db-isolation.ps1`.
- Tenant scripts (`assign-tenants`, `reconcile-tenants`) تستخدم أسماء `*_db`.
- `acceptance-up.ps1` + CI يشغّلان roles + verify.

## W9.1 — AC-19

| خطوة | ملف | تحقق |
|------|-----|------|
| Service | `ReadModelReconciliationService.cs` | fingerprint/schema drift |
| API | `AnalyticsReconciliationApiController.cs` | dryRun + live |
| Rebuild DRY | `ReadModelsController.Rebuild` | delegates to service |
| Script | `acceptance-analytics-reconcile.ps1` | before/after/report JSON |

**اختبارات:** `ReadModelReconciliationServiceTests`.

## W9.2 — AC-20

| خطوة | ملف | تحقق |
|------|-----|------|
| Roles re-apply | `apply-postgres-service-roles.ps1` | idempotent docker exec |
| Verify | `acceptance-verify-db-isolation.ps1` | wraps bash verify script |
| DB names | `assign-tenants.ps1`, `reconcile-tenants.ps1` | `hr_core_db`, `analytics_db`, … |
| Boot wire | `acceptance-up.ps1`, `ci.yml` | roles + verify after stack up |

**اختبارات:** `Ac20DatabaseIsolationTests`.

## W9.3 — قبول حي

| AC | اختبار | أدلة |
|----|--------|------|
| AC-19 | `Ac19AnalyticsReconciliationTests` | `acceptance-evidence/AC-19/` |
| AC-20 | `Ac20DatabaseIsolationTests` | `acceptance-evidence/AC-20/` |

## تعريف الإنجاز

- Drift injected ⇒ detected ⇒ corrected with report; second reconcile clean.
- `verify-postgres-service-roles.sh` Pass in acceptance boot + CI.
- Tenant scripts aligned with `init-databases.sql`.
- `acceptance-evidence/AC-19/` و `AC-20/` = Pass.
