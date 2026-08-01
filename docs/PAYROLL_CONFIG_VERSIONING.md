# إصدار إعدادات الرواتب القانونية (PAY-002)

## دورة الحياة

```text
Draft → Approve → Publish → (edit) → new Draft (Supersedes prior) → …
Published prior versions become Superseded when a newer version is published for the same Code.
```

| الحالة | المعنى |
|---|---|
| `Draft` | قابلة للتعديل in-place |
| `Approved` | hash مثبت؛ لا تعديل جوهري — التعديل ينشئ إصداراً جديداً |
| `Published` | سارية للاستخدام في الحساب حسب `EffectiveFrom`/`EffectiveTo` |
| `Superseded` | أُلغيت لصالح إصدار أحدث |

`IsActive` = النسخة المنشورة الحالية لتشغيلات جديدة (واحدة لكل `Code` عادة).

## خوارزمية ContentHash

`PayrollConfigurationHashService` يبني JSON مرتباً يشمل الحقول القانونية + شرائح الضريبة (`SortOrder`) + التقريب/العملة، ثم SHA-256 hex (64).

يُحسب عند الاعتماد/النشر ويُنسخ إلى `PayrollRun.ConfigContentHash` عند الحساب.

## حل النسخة حسب تاريخ الفترة

`ResolvePublishedForPeriod(periodStart)`:

1. مرشحات: `PublicationStatus == Published`
2. `EffectiveFrom <= periodStart` و (`EffectiveTo` null أو `>= periodStart`)
3. أحدث `EffectiveFrom` ثم أعلى `VersionNumber`

إن لم تُوجد نسخة → فشل الحساب برسالة عربية (**لا fallback** إلى Law50 في الذاكرة).

## ربط التشغيل

عند `CalculatePayrollRun`:

- إن وُجد pin مسبق: التحقق من وجود النسخة ومطابقة الـhash.
- وإلا: حل منشور للفترة وتثبيت `PayrollConfigurationId` + `ConfigContentHash`.

## التصحيح الرجعي

يرث pin المصدر. تشغيل مرحّل بلا pin يُرفض (PAY-002). حد الأشهر من النسخة المثبتة.

## الاعتمادية PAY-001

مسارات تجاوز اعتماد التشغيل تبقى ملك PAY-001؛ هذه البطاقة تثبّت الأساس القانوني للحساب فقط.

## ملفات رئيسية

- `PayrollConfiguration` / `PayrollRun` (Domain)
- `PayrollConfigurationVersioning` / `PayrollConfigurationHashService`
- `PayrollRunHandlers` / `CorrectionRunHandlers`
- `PayrollConfigurationsController` (Edit→clone, Approve, Publish)
