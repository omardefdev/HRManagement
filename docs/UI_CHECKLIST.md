# قائمة تحقق يدوية — مسارات `/ui` عبر البوابة

استخدمها بعد تشغيل البوابة والخدمات وKeycloak. لكل مسار: تسجيل دخول، تحميل الصفحة الرئيسية للوحدة، إجراء قراءة/كتابة بسيطة إن وُجد.

## إعداد Keycloak السريع للتطوير

1. أنشئ **أدوار Realm** بنفس الأسماء الواردة في [KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md) (قسم الأدوار).
2. اربط المستخدم التجريبي بدور **`hr-admin`** لتجاوز جميع سياسات الوحدات، أو اربط أدوار الوحدات التي تختبرها فقط.

## التحقق الأساسي (SSO)

- [ ] **تسجيل الدخول:** فتح `http://localhost:5000` يوجّه إلى Keycloak ثم يعود إلى لوحة البوابة مع اسم المستخدم.
- [ ] **SSO عبر `/ui/*`:** بعد تسجيل دخول واحد، `/ui/hr-core` و`/ui/payroll` (مع الأدوار) يعملان دون إعادة توجيه Keycloak.
- [ ] **تسجيل الخروج:** `/Account/Logout` ينهي الجلسة؛ إعادة فتح `/ui/hr-core` تطلب تسجيل دخول.
- [ ] **مستخدم معطّل:** بعد فصل/تعطيل في Keycloak، الطلبات المحمية تفشل (401/403).
- [ ] **JwtBearer:** الخدمات خلف البوابة (`HrAuth:BehindGateway=true`) لا تقبل Cookie OIDC مباشرة — الوصول عبر البوابة فقط.

## الجلسات والكوكيز

- [ ] بعد تسجيل الدخول من البوابة، فتح `/` يظهر لوحة دون إعادة تحديث تلقائي لخطأ المصادقة.
- [ ] الانتقال بين `/ui/hr-core` و`/ui/payroll` يحافظ على الجلسة (كوكي البوابة + Bearer للخدمات).
- [ ] خلف HTTPS في الإنتاج: اضبط `HrAuth:CookieSecureAlways` إلى `true` و`Keycloak:RequireHttpsMetadata` إلى `true`.

## مسارات الواجهة (من البوابة)

| المسار | الدور المطلوب (إضافة إلى `hr-admin`) |
|--------|--------------------------------------|
| `/ui/hr-core` | `hr-core` |
| `/ui/hr-contracts` | `hr-contracts` |
| `/ui/hr-health` | `hr-health` |
| `/ui/hr-movements` | `hr-movements` |
| `/ui/hr-training` | `hr-training` |
| `/ui/payroll` | `payroll` |
| `/ui/idcard` | `idcard` |
| `/ui/records-archive` | `records-archive` |
| `/ui/admin-logistics` | `admin-logistics` |
| `/ui/it-infra` | `it-infra` |
| `/ui/dev-analytics` | `dev-analytics` |
| `/ui/compliance` | `compliance` |
| `/ui/auth-directory` | `auth-directory` |

- [ ] بدون الدور المناسب: توقع **403** من البوابة أو من الخدمة المصدر.
- [ ] `/health` على البوابة يبقى متاحاً بدون مصادقة (فحص التشغيل).

## YARP والروابط الثابتة

- [ ] الروابط في `Views` تشير إلى مسارات نسبية متسقة مع البادئة `/ui/...` عند العرض عبر البوابة.
- [ ] فحص الصحة التجميعي `/health/upstreams/...` يعمل من المتصفح أو من لوحة البوابة.

## إنتاج خلف بروكسي

- [ ] `UseForwardedHeaders`: `true` في `appsettings.Production.json` عند وضع التطبيق خلف nginx/IIS/ingress.
- [ ] `Cors:AllowedOrigins`: مصفوفة أصول الواجهة الأمامية فقط عند الحاجة لـ API من متصفح.
