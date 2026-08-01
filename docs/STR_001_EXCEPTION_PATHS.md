# مسارات الاستثناء — STR-001

| مسار | السلوك |
|---|---|
| استخدام «national scale» دون حزمة STR-001 | مرفوض |
| تخفيض مؤشر تحت floor في required-indicators دون EXCEPTION موقّع | `str001-envelope-validate` يفشل |
| مؤشر بلا ownerRole / asOfDate / sourceTier | فشل validate / Str001* |
| syria-interim بلا ASS-SYR-CENSUS | فشل validate |
| حذف SYR-002 من blockers مع الإبقاء على interim كـ«نهائي» | مرفوض |
| ادعاء CAP-001 أو SYR-002 Done داخل STR-001 | مرفوض |
| خلط scale_factor المخبري بإثبات وطني | مرفوض — WORKLOAD_DATASET |

راجع [ADR-STR-001](ADR-STR-001-national-scope-envelope.md).
