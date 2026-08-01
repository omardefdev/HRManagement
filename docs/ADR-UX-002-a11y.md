# ADR-UX-002 — الوصولية وWCAG 2.2 AA

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | UX-002 (البطاقة 46) |
| التاريخ | 2026-07-28 |

## السياق

UX-001 وفّر واجهة RTL محلية وآمنة من XSS بلا CSP يمنع تحسينات a11y. لا skip-link، ولا بوابة axe، وجداول/أزرار أيقونة غير مسمّاة على نطاق واسع.

## القرار

| الموضوع | الاختيار |
|---|---|
| المعيار | WCAG **2.2 AA** على رحلات ويب عربية RTL حرجة |
| جداول | CSS تمرير أفقي عالمي + `table-responsive` على Indexes حرجة + `_HrDataTable` |
| أخطاء | `_HrValidationSummary` + ربط `aria-describedby`/`aria-invalid` عبر JS خفيف |
| تحقق | `@axe-core/playwright`؛ فشل serious/critical؛ CI لأسماء أزرار الأيقونة |
| متصفحات | آخر إصدارين Chrome/Edge/Firefox + Safari حديث |

## خارج النطاق

L10N-001/002، MOB-001، NAT-005 (شهادة)، إعادة UX-001، FUN UAT كامل.

## العواقب

- كنس أزرار الأيقونة في كل `Index.cshtml`.
- axe قد يتطلب Skip عند عدم توفر بيئة UI محلية (نمط QA-004).
