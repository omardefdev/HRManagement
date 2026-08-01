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
