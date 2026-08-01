# OBS-003 — نموذج تهديد التدقيق

## مهاجمون

| جهة | قدرة | ماذا يمنع / يكتشف |
|---|---|---|
| مستخدم تطبيق عادي | استدعاء API | أحداث Gateway + قواعد bulk/roles |
| مشغّل بتطبيق EF | محاولة Update/Delete | `RejectAuditMutations` |
| حساب DB بصلاحية واسعة | SQL UPDATE/DELETE | كسر `EventHash`/`PrevHash` → `chain-verify` يفشل؛ WORM يبقى |
| مشغّل تخزين | حذف ملفات JSONL محلية | دفعة WORM Object Lock (prod) + catalog |
| مهاجم ساعة | انحراف الساعة | NTP موثّق؛ تحقق يحذّر لفجوات زمنية شاذة |

## حدود

- من يملك مفاتيح Object Lock admin يمكنه كسر WORM خارج نطاق هذه البطاقة (فصل صلاحيات سحابة = تشغيلي).
- سلسلة الهاش تحمي سلامة السجل لا سرّية المحتوى (PII مُنقّح في QueryString).
