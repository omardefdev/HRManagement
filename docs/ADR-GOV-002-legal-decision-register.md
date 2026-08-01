# ADR-GOV-002 — سجل القرارات القانونية للرواتب والخدمة

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | GOV-002 (البطاقة 48) |
| التاريخ | 2026-07-28 |
| الاعتمادية | PAY-002 (Done) |

## السياق

إصدار الإعداد القانوني immutable موجود (PAY-002)، لكن القواعد المالية ما زالت موسومة سابقاً بـ LEGAL_TBD بلا سجل قرارات بمصدر/نفاذ/مالك، وبلا موافقة قانونية منفصلة عن اعتماد التشغيل، وبلا change notice أو حزمة ذهبية مربوطة بالقرار.

## القرار

| الموضوع | الاختيار |
|---|---|
| سجل | `docs/legal-decisions/register.json` + Markdown + كيان `LegalDecisionRecord` |
| حالة Law 50/2004 | **ApprovedInterim** (منصة) بمراجع كتالوج 12 — ليست فتوى ولا شهادة NAT/SYR |
| CIVIL_SERVICE_2026 | **PendingEnactment** حتى نفاذ مصدق |
| إزالة LEGAL_TBD | إشارات `decisionId` + بوابة CI |
| موافقة قانونية | `LegalAttestation` قبل Publish؛ تطابق `ContentHash` |
| Change notice | `PayrollConfigChangeNotice` عند Publish |
| خارج النطاق | SYR-001، NAT-003، PROD-001، GOV-003 |

## العواقب

- لا نشر إصدار قانوني دون attestation سارية لنفس البصمة.
- الاختبارات الذهبية تربط المدخلات/المخرجات بـ`decisionId`.
- تغيير القانون = إصدار جديد + notice؛ الماضي يبقى على pin التشغيل.
