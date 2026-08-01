# سياسة بيانات البيئات غير الإنتاجية (GOV-001-D)

| القاعدة | التفصيل |
|---|---|
| ممنوع | استيراد dump إنتاج خام إلى dev/test/CI |
| مسموح | بذور اصطناعية (`testdata/`, NFR seed scripts) و`testdata/e2e` |
| استثناء | `ALLOW_PROD_DATA_COPY=true` موقّع مؤقتاً مع تذكرة وأثر إخفاء |
| بوابة | `scripts/check-non-prod-data-policy.py` في CI |

أسماء/مسارات مرفوضة ما لم يُستثنَ: `*prod*dump*`, `*production*.sql` تحت المستودع خارج allowlist.
