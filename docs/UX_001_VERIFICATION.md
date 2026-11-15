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

## PLAN-67 — Enforce في كل مكان، وسياسة واحدة لكل استجابة

- **أين يعمل Enforce:** كل `appsettings.Production.json` (الخدمات الأربع عشرة والبوابة)، وتطبيقات المكدّس الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml`)، ومكدّس E2E في CI (`docker-compose.ci-e2e.yml`). لا ملف إنتاج في `ReportOnly`.
- **السبب الذي أثبته P0:** كانت البوابة تكتب سياستها قبل `MapReverseProxy`، ثم يضيف YARP سياسة الخدمة، فتصل الصفحة المحوَّلة بسياستين؛ والمتصفح يطبّق الاثنتين معاً، ونونس كل واحدة لا يطابق الأخرى، فيُحجب كل ما ليس من الأصل نفسه. ومعهما سمات `style` ومعالجات `on…=` المضمّنة.
- **القاعدة (D-67-1):** `UseHrSecurityHeaders` يبني السياسة كما كانت، ويكتب كل رأس أمني في `Response.OnStarting`، وكلاً منها فقط إن لم تحمله الاستجابة (سياسة بأي من الوضعين تُحسب). فالصفحة المحوَّلة تبقى بسياسة الخدمة ونونسها، وصفحة البوابة، أو استجابة محوَّلة بلا رؤوس، تأخذ سياسة البوابة.
- **السياسة نفسها لم تتغير:** `script-src` و`style-src` من الأصل أو بنونس الاستجابة، و`object-src 'none'`، بلا `unsafe-*` ولا `*` ولا مصدر خارجي.
- **لا شيء مضمّن في العروض:** لا `on…=` ولا `style=` ولا `javascript:`. التأكيد عبر `data-hr-confirm` (UX-003)، وأشرطة التقدم عبر `data-hr-progress` يطبّقها `shared-ui.js` من CSSOM، والكتل المضمّنة الباقية تحمل النونس (`CspNonceTagHelper`).
- **الفحص:**
  - `scripts/ux067-validate.ps1` (و`-SelfTest`): الباني، وEnforce في كل ملف إنتاج وفي المكدّسين، ولا معالج ولا `style` في أي عرض، وقوائم الحارس فارغة.
  - `scripts/ux067-validate.ps1 -Headers <url>`: يعدّ سياسات استجابة واحدة بـ`curl.exe`، ويفشل إن لم تكن سياسة واحدة نافذة بلا Report-Only.
  - `InterfaceTextGuardTests` (معمارية) يمنع عودة المعالجات والسمات.
  - `AcUx67ReadableInterfaceTests` (AC-UX-67a، f): المتصفح يعدّ السياسات ويسجّل كل `securitypolicyviolation` على الصفحات الرئيسية، وصفحة مزروعة بمعالج مضمّن تثبت أن الفحص يفشل.

## خارج النطاق

UX-002، SEC-003، SAN-001، SUP-001.

## إغلاق

**UX-001 = Done** مع تحديث TRACEABILITY / CI_AND_TESTING.
