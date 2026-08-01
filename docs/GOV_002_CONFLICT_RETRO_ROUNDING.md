# تضارب القوانين والرجعية والتقريب (GOV-002-C)

| القرار | decisionId | القاعدة |
|---|---|---|
| نظام واحد لكل فترة | LD-SINGLE-REGIME | لا دمج LAW_50_2004 مع CIVIL_SERVICE_2026 في نفس التشغيل؛ `ResolvePublishedForPeriod` يختار منشوراً واحداً |
| الرجعية | LD-RETRO-MONTHS | Correction يرث pin المصدر (PAY-002)؛ رفض إن تجاوز `MaxRetroCorrectionMonths` |
| التقريب | LD-ROUNDING | `MidpointRounding.AwayFromZero` و`DecimalPlaces=2` ما لم يُنشر إصدار attested بخلاف ذلك |
| الماضي لا يُعاد كتابته | — | تغيير القانون = إصدار جديد + LegalAttest + Publish + ChangeNotice؛ التشغيلات القديمة تبقى على `ConfigContentHash` |

انظر [GOV_002_CHANGE_NOTICE_RUNBOOK.md](./GOV_002_CHANGE_NOTICE_RUNBOOK.md).
