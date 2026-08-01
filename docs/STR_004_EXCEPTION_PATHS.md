# مسارات الاستثناء — STR-004

| مسار | السلوك |
|---|---|
| فرع منتج باسم جهة / tenant-branch | مرفوض — REPO_NOFORK_RULE |
| if (agency==…) في src | فشل validate |
| جهتان بـ digest مختلف يُقدَّم كـ«نفس الإصدار» | فشل validate |
| ادعاء ترقية تحفظ overrides دون PROD-001 | مرفوض — UPGRADE stub Blocked |
| توقيع تشفيري لحزم إعداد دون ONB/PROD | Blocked — schema فقط |
| حذف PROD-001 من blockers مع بقاء dependency | فشل validate |

راجع [ADR-STR-004](ADR-STR-004-single-product-no-fork.md).
