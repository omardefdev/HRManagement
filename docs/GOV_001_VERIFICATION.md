# GOV-001 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + inventory + DPIA | [ADR-GOV-001](./ADR-GOV-001-data-classification-privacy.md), [inventory](./DATA_CLASSIFICATION_INVENTORY.md), [DPIA](./GOV_001_DPIA.md) Approved | Ready |
| AuditRedact / قناع تصدير | `PiiMask` + Reports/ExportJobs؛ `Gov001PiiMaskAndRedactTests` | OK |
| جدول احتفاظ + dry-run | [RETENTION_SCHEDULE](./GOV_001_RETENTION_SCHEDULE.md), `gov001-retention-dry-run.py`, [RUNBOOK](./GOV_001_RETENTION_RUNBOOK.md) | OK |
| Non-prod | [NON_PROD_DATA_POLICY](./NON_PROD_DATA_POLICY.md) + `check-non-prod-data-policy.py` في CI | OK |
| DSAR | `DsarRequest` + `api/dsar` (Open→InProgress→Fulfilled/Rejected) | OK |
| Breach tabletop | [GOV_001_BREACH_TABLETOP.md](./GOV_001_BREACH_TABLETOP.md) | موثّق |

## DSAR v1 (تشغيل يدوي)

1. `POST /api/dsar` مع `RequestType=Access` و`SubjectEmployeeId` أو `SubjectNationalId`.
2. `POST /api/dsar/{id}/status` → `InProgress`.
3. تجميع حزمة اكتشاف (JSON) من Employee عبر runbook؛ إرفاق عبر body `DiscoveryPackage`.
4. `Fulfilled` أو `Rejected`؛ يُثبَّت `CompletedAt`.

## خارج النطاق

GOV-002/003، PRV-001..003، NAT-002، L10N.

## إغلاق

**GOV-001 = Done** مع تحديث TRACEABILITY / CI_AND_TESTING؛ TEN-001 يُعلَّم Done شكلياً عبر [ADR-001-tenant-model.md](./decisions/ADR-001-tenant-model.md).
