# خطة التنفيذ التفصيلية — PLAN-10 (تجميد الدورة + الصرف الجزئي + حجز الاحتيال)

خطة تنفيذ قابلة للتطبيق لـ[PLAN-10 — Payroll Money](../PLAN-10-payroll-money.md) (AC-23, AC-24, AC-25). تعتمد على [PLAN-00](PLAN-00-EXECUTION.md) و[PLAN-06](PLAN-06-EXECUTION.md).

## المخرجات النهائية

- `GET /api/payroll/runs/{id}/freeze-status` — `{ resultsFrozen, validatedAtCalculate, configContentHash }`.
- `ExecutePartialDisbursementCommand` — batch + arrears + payment orders (amount overrides).
- `POST /api/payroll/runs/{id}/partial-disburse` + `POST .../funding-decisions`.
- `RunPayrollFraudScanCommand` + `POST /api/payroll/runs/{id}/fraud-scan` (auto قبل process).
- `POST /api/payroll/payment-holds/{id}/release` — four-eyes (`releasedBy` من JWT، `approverUserId` في body).
- اختبارات PG/SQLite + قبول حي `Ac23`..`Ac25`.

## W10.1 — AC-23

| خطوة | ملف | تحقق |
|------|-----|------|
| API | `PayrollApiController.GetFreezeStatus` | freeze DTO |
| Profile API | `PUT api/payroll/employees/{id}/pay-profile` | UpsertEmployeePayProfile |
| PG test | `Pay023FrozenRunIsolationTests` | A frozen, B reflects edit |
| Acceptance | `Ac23PayrollFreezeTests` | `acceptance-evidence/AC-23/` |

## W10.2 — AC-24

| خطوة | ملف | تحقق |
|------|-----|------|
| Orchestrator | `ExecutePartialDisbursementCommand` | invariant + idempotency |
| Orders | `PaymentOrderService.EnsureOrdersForBatchAsync(overrides)` | partial amounts |
| API | `partial-disburse`, `funding-decisions` | HTTP journey |
| Tests | `PartialDisbursementOrchestratorTests`, `Ac24PartialDisbursementTests` | replay ⇒ 409 |

## W10.3 — AC-25

| خطوة | ملف | تحقق |
|------|-----|------|
| Scan | `RunPayrollFraudScanCommand` + `FraudScanInputBuilder` | persist alerts/holds |
| Wire | `ProcessPaymentBatchHandler` pre-scan | gate blocks batch |
| Release API | `PaymentHoldsApiController` | four-eyes |
| Tests | `Frd001PaymentBlockIntegrationTests`, `Ac25FraudHoldTests` | block → release → pay |

## تعريف الإنجاز

- Run A lines identical after profile edit; Run B differs; recalc frozen run blocked.
- Partial: `disbursed + arrears = entitled`; orders match disbursed; no duplicate on replay.
- Fraud: pay blocked during hold; single user cannot release; two users release → pay allowed.
- `acceptance-evidence/AC-23/` .. `AC-25/` = Pass (or documented fallback to PG tests).
