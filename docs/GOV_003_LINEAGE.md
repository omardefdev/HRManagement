# نسب البيانات الرئيسية (GOV-003-E)

مرجع الآلة: [`master-data/lineage.json`](./master-data/lineage.json).

| المسار | الملخص |
|---|---|
| hire-to-payroll | Employee → IEmployeeCreated → PayProfile → تقرير راتب |
| orgunit-to-snapshot-report | OrgUnit → snapshot → تقرير النطاق |
| employee-to-auth | Employee → AuthUser |
| employee-to-analytics | Employee/Org → Dev.Analytics (فجوة خصائص الجهة تُغلق في FUN-006) |
| merge-lifecycle | DuplicateCase → merge → StatusChanged → runbook أقمار |

كل مسار يبدأ من SoR في [GOV_003_SOR_MATRIX.md](./GOV_003_SOR_MATRIX.md).
