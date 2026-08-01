# نتائج قائمة تحقق `/ui` (مراجعة شيفرة — 2026-05)

مرجع القائمة: [UI_CHECKLIST.md](./UI_CHECKLIST.md)

## إعدادات طُبّقت في الشيفرة

| البند | الإجراء | الملف |
|--------|---------|-------|
| كوكي التطوير (Lax، غير Secure) | `HrAuth:CookieSameSite=Lax`، `CookieSecureAlways=false` | [`appsettings.Development.json`](../src/Gateway/HRManagement.Gateway/appsettings.Development.json) |
| Keycloak dev بدون HTTPS metadata | `RequireHttpsMetadata=false` | نفس الملف |
| مسارات YARP `/ui/...` | 13 مسار + `PathRemovePrefix` | [`appsettings.json`](../src/Gateway/HRManagement.Gateway/appsettings.json) |
| فحص صحة مجمّع | `/health/upstreams/{service}` | نفس الملف |
| إنتاج: HTTPS + ForwardedHeaders | `UseForwardedHeaders`، `CookieSecureAlways` | [`appsettings.Production.json`](../src/Gateway/HRManagement.Gateway/appsettings.Production.json) |

## التحقق الآلي (2026-05-29)

| الأداة | الأمر | يغطي |
|--------|-------|------|
| فحص صحة التجميع | `pwsh scripts/verify-staging-stack.ps1` | `/health/live` + `/health/upstreams/*` (13 وحدة) |
| E2E | `pwsh scripts/run-staging-e2e.ps1` | Playwright: login، health، SSO، logout، upstreams |
| سلسلة أعمال | `EndToEndReleaseWorkflowSmokeIntegrationTests` | عقد → إجازة → ترحيل رواتب → فصل |

## ما يتطلب تشغيلاً يدوياً (بعد تشغيل المكدس الكامل)

- [ ] تسجيل دخول Keycloak من البوابة والتحقق من استمرار الجلسة بين `/ui/hr-core` و`/ui/payroll` — **أتمتة جزئية:** `GatewayAuthE2ETests.Sso_across_ui_paths_after_login` عند ضبط `E2E_KEYCLOAK_USER` / `E2E_KEYCLOAK_PASSWORD`
- [ ] اختبار 403 بدون دور الوحدة (كل مسار في UI_CHECKLIST) — يدوي
- [ ] `/health` بدون مصادقة — **أتمتة:** `GatewayE2ETests.Gateway_health_smoke_when_stack_running`
- [ ] Playwright E2E (`RUN_PLAYWRIGHT_E2E=true`) — `scripts/run-staging-e2e.ps1`

## ملاحظات

- كل الخدمات تستخدم `_HrSharedLayout` من RCL؛ الروابط النسبية يجب أن تعمل خلف البوابة عند استخدام `/ui/{module}/...`.
- في الإنتاج: اضبط `Cors:AllowedOrigins` و`Keycloak:Authority` في ملفات Production لكل خدمة والبوابة.
