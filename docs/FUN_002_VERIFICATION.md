# FUN-002 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + مصفوفة | [ADR-FUN-002](./ADR-FUN-002-separation-journey-closure.md), [JOURNEY_MATRIX](./FUN_002_JOURNEY_MATRIX.md) | Ready |
| Checklist Owner/SLA + hold + EOS evidence | SeparationChecklist fields؛ lifecycle gate؛ payroll eos:id | OK |
| Soft-cancel + compensate + reconcile | CancelSeparation؛ [RECONCILIATION](./FUN_002_RECONCILIATION_CHECKLIST.md) | OK |
| Exceptions | [EXCEPTION_PATHS](./FUN_002_EXCEPTION_PATHS.md) + Fun002* tests | OK |
| UAT pack | [FUN_002_UAT_PACK.md](./FUN_002_UAT_PACK.md) ReadyForSign-off | OK |

## خارج النطاق

FUN-003، FUN-005 عمق أصول/بطاقة، FUN-006، PAY-003 Reverse داخل saga.

## إغلاق

**FUN-002 = Done** مع تحديث TRACEABILITY / PROCESS_SAGAS.
