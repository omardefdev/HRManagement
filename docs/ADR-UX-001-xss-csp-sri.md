# ADR-UX-001 — XSS وCSP وCDN/SRI

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | UX-001 (البطاقة 45) |
| التاريخ | 2026-07-28 |

## السياق

واجهة Razor/AdminLTE عبر RCL مشترك كانت تعتمد CDN بلا SRI، و`innerHTML` لبيانات Lookup/إشعارات، ورؤوس أمنية على Gateway فقط بلا CSP.

## القرار

| الموضوع | الاختيار |
|---|---|
| أصول الواجهة | محلية تحت `HRManagement.Web.Shared/wwwroot/lib/` بإصدارات مثبتة؛ ممنوع CDN في `src/` |
| DOM | `escapeHtml` / `textContent` / `createElement`؛ لا `innerHTML` مع بيانات API/مستخدم |
| CSP | `UseHrSecurityHeaders` في ServiceDefaults؛ `Security:Csp:Mode=ReportOnly` افتراضياً ثم `Enforce` |
| Nonces | `HttpContext` nonce على `<script>`/`<style>` المضمنة المتبقية |
| رؤوس | nosniff، SAMEORIGIN، Referrer-Policy، Permissions-Policy، CSP |
| تحقق | سكربتات CI + corpus XSS؛ DAST يدوي (ZAP) موثّق |

## مسار Report-Only → Enforce

1. Staging/Dev: `ReportOnly` وجمع تقارير.
2. إصلاح مخالفات (inline/CDN).
3. تفعيل `Enforce` عبر إعداد دون كسر login/UI الأساسي.

## خارج النطاق

UX-002 (a11y)، SEC-003، SUP-001، SAN-001 (مرآة سيادية)، محررات WYSIWYG.

## العواقب

- حجم مستودع أكبر (vendored libs).
- Enforce صارم قد يكسر صفحات بـinline غير مُعلَّمة بـnonce حتى تُرحَّل.
