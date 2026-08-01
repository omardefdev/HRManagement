# FUN-001 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + مصفوفة | [ADR-FUN-001](./ADR-FUN-001-hire-journey-closure.md), [JOURNEY_MATRIX](./FUN_001_JOURNEY_MATRIX.md) | Ready |
| Checklist evidence + Directory party | HireChecklist fields؛ Auth.Directory في MandatoryParties؛ `HirePartyAckConsumer` | OK |
| Timeout/fault/compensate/resume | CancelHire + Compensate؛ [RECONCILIATION](./FUN_001_RECONCILIATION_CHECKLIST.md) | OK |
| Exceptions | [EXCEPTION_PATHS](./FUN_001_EXCEPTION_PATHS.md) + unit/integration tests | OK |
| UAT pack | [FUN_001_UAT_PACK.md](./FUN_001_UAT_PACK.md) ReadyForSign-off | OK |

## خارج النطاق

FUN-002، ONB-001، HCM-001، FUN-006.

## إغلاق

**FUN-001 = Done** مع تحديث TRACEABILITY / PROCESS_SAGAS.
