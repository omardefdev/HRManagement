# جرد XSS / CDN (UX-001)

مرجع: [ADR-UX-001-xss-csp-sri.md](./ADR-UX-001-xss-csp-sri.md).

## DOM sinks

| موقع | خطر | قصة |
|---|---|---|
| `entity-pickers.js` org tree `innerHTML` | stored/reflected عبر `code`/`nameAr` | B |
| `shared-ui.js` alert `innerHTML` | reflected من رسالة API | B |
| `_LiveNotifications` toast `innerHTML` | stored عبر SignalR | B |
| `file-upload.js` preview `innerHTML` | URL في HTML | B |
| `shared-ui.js` loader spinner | HTML ثابت | B (مسموح allowlist) |

## Html.Raw

| موقع | حالة | قصة |
|---|---|---|
| Dashboard Contracts/Payroll (`JsonSerializer`) | آمن كـJSON | توثيق |
| Reports / Gateway charts (`JsonSerializer`) | آمن كـJSON | توثيق |
| Documents/Contacts أيقونات ثابتة | ثابت | — |
| `_OrganizationBranding` Serialize | آمن | — |

## CDN → محلي

| أصل | نسخة | مسار محلي |
|---|---|---|
| jquery | 3.7.0 | `~/lib/jquery/jquery.min.js` |
| bootstrap RTL CSS/JS | 5.3.3 | `~/lib/bootstrap/...` |
| admin-lte RTL | 4.0.2 | `~/lib/admin-lte/...` |
| font-awesome | 6.5.0 | `~/lib/fontawesome/...` |
| chart.js | 4.4.1 | `~/lib/chartjs/chart.umd.min.js` |
| tom-select | 2.3.1 | `~/lib/tom-select/...` |
| signalr | 8.0.7 | `~/lib/signalr/signalr.min.js` |
| jquery-validation | 1.19.5 + unobtrusive 4.0.0 | `~/lib/jquery-validation/...` |

## AC → Story

| AC | Story |
|---|---|
| payloads لا تنفذ | B + E |
| CSP RO ثم Enforce | D |
| لا CDN غير مثبت | C |
| automated scan | C + E |
