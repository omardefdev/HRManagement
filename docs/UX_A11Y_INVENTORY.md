# جرد الوصولية (UX-002)

مرجع: [ADR-UX-002-a11y.md](./ADR-UX-002-a11y.md).

## Baseline يدوي (مختصر)

| صفحة | ملاحظات ما قبل الإصلاح |
|---|---|
| Login / Gateway | toggler بلا اسم؛ لا skip؛ لا `<main>` |
| Employees Index | جداول؛ أزرار بـ`title` فقط أحياناً |
| CompensationTypes Index | أيقونات CRUD بلا `aria-label`؛ بلا `table-responsive` |

## فئات الفجوات

| فئة | قصة |
|---|---|
| Skip / landmarks / chrome names | B |
| Modals / spinner SR | B |
| Validation summary + field linking | C |
| Search label | C |
| Responsive tables + critical Indexes | D |
| axe + icon CI gate | E |

## AC → Story

| AC | Story |
|---|---|
| journeys بلا فأرة | B + D + E |
| zero serious/critical axe | E |
| كل icon button له اسم | B + D + E |
| جداول على الهاتف | D |
| أخطاء تربط الحقول ولا تكشف معلومات | C |

## بوابة التوظيف العامة وصفحة «طلبي» (HCM-006 / HCM-030، PLAN-30 K4)

الفحص الساكن `scripts/hcm006-a11y-scan.ps1` (الخروج 0 = لا ملاحظات حرجة) يشمل: `PublicCareers` (Index، Details مع نموذج التقديم والمرفقات والموافقات، Track، TrackResult، Submitted، Privacy)، `CandidatePortal` (Login، Code، Index مع نماذج العرض والانسحاب والاستئناف والمستندات) و`_PublicLayout`.

| فحص | التطبيق |
|---|---|
| `<label for>` ↔ `id` لكل حقل ظاهر | كل الحقول |
| رسالة خطأ الحقل لها `id` مرتبط بـ`aria-describedby`، والحقل يحمل `aria-invalid` | نمط `invalid-feedback` (بدل `ValidationMessage` بلا معرّف) |
| حقل الرمز: `autocomplete="one-time-code"` و`inputmode="numeric"` | `CandidatePortal/Code` |
| هدف تركيز ظاهر بعد الخطأ: ملخص `data-hr-error-summary` (`role="alert"`، `tabindex="-1"`) يركّز عليه `careers-portal.js`، بإطار تركيز في `_PublicLayout` | Details، Track، Login، Code، Index |
| CSP بالـnonce: لا `style=` ولا معالجات أحداث مضمّنة ولا `confirm()`؛ الأيقونات الزخرفية `aria-hidden` | كل الصفحات المفحوصة |

اختبار الوحدة `Hcm030PrivacyA11yScanTests.A11y_scan_passes` يشغّل الفحص. فحص axe الآلي (E) لا يغطي البوابة العامة بعد.
