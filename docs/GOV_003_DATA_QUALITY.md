# أبعاد جودة البيانات الرئيسية (GOV-003-D)

| البعد | التعريف | عتبة افتراضية | تنبيه |
|---|---|---|---|
| completeness | موظف بلا NationalId أو FullName | 0 | فشل التقرير إن >0 |
| uniqueness | تكرار NationalId ضمن tenant (بعد تجاهل ANON-) | 0 | فشل إن >0 |
| orphan_rate | EmployeeId في أقمار صناعية بلا صف HR.Core | 0 | فشل إن >0 عند توفر DB |
| open_duplicate_cases | حالات Open/UnderReview | تحذير إن >10 | لا يفشل CI افتراضياً |
| snapshot_freshness | تأخر نسخ OrgUnit (وثائقي) | تحذير | MDM/ANA لاحقاً |

**Steward:** مسؤول بيانات HR يراجع طابور التكرار + `artifacts/gov003-dq-report.json`.

سكربت: `scripts/gov003-data-quality-report.py` — بدون Postgres يقيّم fixture/عتبات منطقية فقط.
