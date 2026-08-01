# مسارات الاستثناء — STR-007

| مسار | السلوك |
|---|---|
| KPI بلا تعريف/مصدر/مالك/مراجع/هدف/anti-gaming | فشل validate |
| ownerRole == auditorRole | فشل validate |
| baselineRequired بلا prePilotFreeze | فشل validate |
| قياس منفعة بـ screenCount/pageCount/widgetCount | فشل validate |
| لوحة/تجميع عبر tenants بلا waiver | فشل validate |
| ادعاء FIN-001 أو LiveDashboardClaim / PROD-006c Done داخل الحزمة | مرفوض — soft blockers |
| تعديل KPI.cs كبديل للحزمة | خارج النطاق |
| حذف STR-001/003/GOV-003 من Satisfied | فشل validate |

راجع [ADR-STR-007](ADR-STR-007-value-competition-baseline.md).
