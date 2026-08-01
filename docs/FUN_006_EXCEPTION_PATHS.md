# مسارات الاستثناء — FUN-006

| مسار | السلوك |
|---|---|
| إتلاف تحت Legal Hold | رفض Request/Approve |
| موافقة إتلاف بنفس طالب الطلب | رفض SoD |
| إتلاف بلا موافقة مسبقة | CanDispose=false |
| ملف رقمي بلا checksum | رفض Create |
| عنصر رقمي بلا تصنيف/file plan | رفض Create |
| دور فرع يطلب headcount بلا orgUnitId | Forbid أو إجبار RootOrgUnitId |
| دور فرع يطلب orgUnitId مختلف | Forbid |
| دور فرع يطلب إجمالي tenant | Forbid |
| Rebuild لنفس الأحداث | نفس fingerprint |
| ExportJob لمستخدم آخر | رفض التحميل |

راجع [ADR-FUN-006](ADR-FUN-006-archive-analytics-reporting-closure.md).
