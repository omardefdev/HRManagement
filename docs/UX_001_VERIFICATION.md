# UX-001 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + جرد | [ADR-UX-001-xss-csp-sri.md](./ADR-UX-001-xss-csp-sri.md), [UX_XSS_INVENTORY.md](./UX_XSS_INVENTORY.md) | Ready |
| DOM XSS sinks أصلحت | `entity-pickers.js`, `shared-ui.js`, `live-notifications.js`, `file-upload.js` بلا `innerHTML` ديناميكي | OK |
| لا CDN خارجي في views | `scripts/check-no-external-cdn.sh` + libs تحت `wwwroot/lib/` | OK |
| CSP Report-Only + رؤوس | `UseHrSecurityHeaders` عبر `UseHrProductionMiddleware`؛ `Security:CspMode` | OK |
| Nonces | `CspNonceTagHelper` على script/style | OK |
| Corpus / بوابة ثابتة | `Ux001XssCorpusTests` + `check-xss-dom-sinks.sh` في CI | OK |
| DAST | ZAP baseline يدوي موصى به على staging بعد تفعيل Enforce | موثّق |

## تفعيل Enforce

في `appsettings`: `"Security": { "CspMode": "Enforce", "CspConnectSrcExtra": ["https://keycloak-host"] }` بعد استقرار تقارير Report-Only.

## خارج النطاق

UX-002، SEC-003، SAN-001، SUP-001.

## إغلاق

**UX-001 = Done** مع تحديث TRACEABILITY / CI_AND_TESTING.
