# Keycloak والأسرار

## ابدأ من هنا: الـ realm كشيفرة (PLAN-39)

لا يُنشأ realm ولا عميل ولا نطاق يدوياً في لوحة الإدارة. الـ realm `hrmanagement` يُبنى من مصدرين لا يتكرران (D-39-1):

| المصدر | ما فيه |
|--------|--------|
| [`docker/keycloak/realm-manifest.json`](../docker/keycloak/realm-manifest.json) | الأدوار، عميل البوابة ومُعيّنات المطالبات الثلاث (`tenant_id`، `employee_id`، `scope_org_unit_id`)، `directory-sync`، النطاقات المدمجة، ملف المستخدم (السمات الثلاث للمدير وحده)، إعدادات الأمان (D-39-6)، ومستخدمو كل ملف تعريف. بلا أي سر |
| [`deploy/wiring/service-wiring.json`](../deploy/wiring/service-wiring.json) | عملاء `svc-*` ونطاقاتهم وجمهور كل نطاق واسم متغير السر (PLAN-37) |

1. **التوليد:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/keycloak/render-realm.ps1 -Profile Production|Demo|Acceptance [-EnvFile .env] [-AppDomain <host>]`
   يكتب `artifacts/keycloak/<profile>/realm-hrmanagement.json` و`reconcile-plan.tsv` (خارج git).
   - **Production:** مدير bootstrap واحد (`HRM_BOOTSTRAP_ADMIN_USER`، كلمة مرور مؤقتة، `UPDATE_PASSWORD` عند أول دخول، D-39-5).
   - **Demo:** المستخدمون التجريبيون بـ `DEMO_USERS_PASSWORD`.
   - **Acceptance:** كل شخصيات `testdata/acceptance/personas.json` بأسرار المختبر، مع password grant للبوابة (أداة القبول).
   في Production وDemo كل سر نائب `${NAME}` يحلّه Keycloak من بيئته عند الاستيراد؛ المولّد لا يقرأ سراً.
   الملفات الثلاثة لا تختلف إلا في المستخدمين وعناوين التحويل واستثناءات القبول (`Ops039RenderRealmTests`).
2. **الاستيراد الأول:** `keycloak` يعمل بـ `start --import-realm` من volume `keycloak_import` الذي يملؤه `remote-deploy.sh`
   (realm جديد فقط؛ لا يكتب فوق realm قائم).
3. **المطابقة في كل نشر:** الخدمة لمرة واحدة `keycloak-reconcile` ([deploy/keycloak/reconcile.sh](../deploy/keycloak/reconcile.sh))
   بعد أن يصبح Keycloak سليماً وقبل أي تطبيق: العملاء وأعلامهم، النطاقات وجمهورها، الأسرار من `.env`، عناوين التحويل،
   وحقول الأمان المسماة. **لا تمس المستخدمين ولا ربط الأدوار أبداً.** التشغيل الثاني يطبع `reconcile: 0 changes`.
4. **محلياً:** `scripts/prepare-keycloak-import.ps1` غلاف: `-Profile Local` (الافتراضي، قالب التطوير
   `realm-hrmanagement-dev.template.json` ثم `scripts/sync-keycloak-dev-realm.ps1`)، و`-Profile Acceptance` لمكدّس القبول.

**تجاهل في البداية:** قسم «تزامن Auth.Directory» أدناه — اختياري وليس شرطاً للدخول العادي.

**مهم:** `Authority` واسم الـ Realm يجب أن يطابقا حرفياً في **البوابة وجميع الخدمات** التي تستخدم نفس Keycloak.

---

## تزامن Auth.Directory مع Keycloak (اختياري)

عند `Keycloak:DirectorySync:Enabled` = `true`، يحاول خدمة الدليل بعد إنشاء `AuthUser` المحلي استدعاء **Admin API** لإنشاء مستخدم في Realm (عميل خدمة بـ client credentials وصلاحيات `manage-users` على نفس الـ realm).

- `Keycloak:DirectorySync:AdminClientId` / `AdminClientSecret` — عميل سري في نفس الـ realm (أو سر عبر `Keycloak__DirectorySync__AdminClientSecret`).
- يجب أن يطابق `Keycloak:Authority` نفس الـ realm المستهدف (`…/realms/{realm}`).

## Client secret

لا تُخزَّن أسرار العميل في المستودع للإنتاج. استخدم أحد الخيارات:

- متغير بيئة: `Keycloak__ClientSecret` (يستبدل `Keycloak:ClientSecret` في `appsettings`).
- Azure Key Vault / Docker secrets / مدير أسرار آخر يحقن القيمة في وقت التشغيل.

البوابة فقط تستخدم `Keycloak:ClientId` و`Keycloak__ClientSecret`. جميع الخدمات تحتاج `Keycloak:Authority` (issuer) للتحقق من JwtBearer.

| متغير البيئة | الاستخدام |
|--------------|-----------|
| `Keycloak__Authority` | Issuer (البوابة + جميع الخدمات) |
| `Keycloak__ClientSecret` | البوابة فقط (`hr-gateway-client`) |
| `Keycloak__DirectorySync__AdminClientSecret` | Auth.Directory (Admin API) |
| `HrAuth__BehindGateway` | `true` للخدمات في Docker/الإنتاج |

## HTTPS metadata

للإنتاج خلف TLS حقيقي، اضبط `Keycloak:RequireHttpsMetadata` إلى `true` في الإعدادات.
**الافتراضي في الشيفرة أصبح `true`**؛ عيّن `false` صراحةً فقط في التطوير المحلي.

## Audience validation (JWT)

JwtBearer يتحقق من `aud` الآن (`ValidateAudience = true`). القيم المقبولة:

- `Keycloak:ClientId` (مثلاً `hr-gateway-client`) — للتوكنات التفاعلية عبر البوابة
- `ServiceIdentity:ApiAudience` (مثلاً `api-hr-core`, `api-file-storage`, `api-auth-directory`) — موارد S2S (IAM-004)
- `Keycloak:AdditionalValidAudiences` — قائمة مفصولة بـ`;` عند الحاجة
- `account` — يُضاف تلقائياً لأن Keycloak يضعه غالباً في access token

token صادر لعميل/audience غير مسموح يُرفض.

## هوية الخدمة (IAM-004)

كل workload يملك عميل Keycloak confidential بـ `client_credentials` (`svc-*`). التفاصيل والجداول في:

- [SERVICE_TRUST_INVENTORY.md](./SERVICE_TRUST_INVENTORY.md)
- [SERVICE_SECRET_ROTATION.md](./SERVICE_SECRET_ROTATION.md)
- قالب العملاء: `docker/keycloak/realm-hr-services.json` (أسرار عبر `${ENV}` فقط — لا تُلتزم بأسرار حقيقية)

| متغير البيئة | الاستخدام |
|--------------|-----------|
| `ServiceIdentity__ClientId` | عميل الخدمة (مثل `svc-payroll`) |
| `ServiceIdentity__ClientSecret` / `SVC_*_CLIENT_SECRET` | سر العميل — لكل خدمة بشكل مستقل |
| `ServiceIdentity__Scope` | scopes المطلوبة للنداءات الصادرة |
| `ServiceIdentity__ApiAudience` | audience الذي تقبله هذه الخدمة كمورد |

`Authorization: Bearer <service_token>` يُحقن عبر `ServiceAuthHandler`؛ لا تعتمد مسارات S2S على توكن المستخدم بعد الآن.

### العملاء والنطاقات من مصفوفة التوصيل (PLAN-37)

مصدرها الوحيد [`deploy/wiring/service-wiring.json`](../deploy/wiring/service-wiring.json) (جدولها المقروء:
[SERVICE_WIRING_MATRIX.md](SERVICE_WIRING_MATRIX.md)). منها تُقرأ:

- `scripts/sync-keycloak-dev-realm.ps1`: كل نطاق (`scopes` مع مُعيّن audience لكل جمهور) وكل عميل خدمة بنطاقاته
  الاختيارية. شغّله بعد أي تغيير في المصفوفة على realm التطوير والقبول (`acceptance-up.ps1` يشغّله)، **قبل** أن تطلب
  الخدمات النطاق الجديد: Keycloak يرفض توكن نطاق غير مربوط (`invalid_scope`).
- `scripts/keycloak/render-realm.ps1` (PLAN-39؛ `prepare-keycloak-import.ps1 -Profile Demo|VpsDemo|Production` غلافه):
  realm الخادم عند أول استيراد، كل عميل بسرّه `${SVC_X_CLIENT_SECRET}`.
- `deploy/keycloak/reconcile.sh` (الخدمة `keycloak-reconcile`، PLAN-39): في كل نشر، بلا حذف realm ولا إعادة استيراد؛
  حلّ محل `deploy/vps/keycloak-sync-clients.sh` (PLAN-37، حُذف).

`ServiceIdentity__Scope` يُكتب صراحةً لكل خدمة في `docker-compose.yml` / `docker-compose.services.yml` وفي
`deploy/k8s/base/<svc>/wiring-patch.yaml` (فارغ لمن لا نطاق له)، ولا يأتي من `appsettings.json`. السر الفارغ في
`appsettings.json` لا يحجب `ServiceIdentity__ClientSecret` من البيئة بعد الآن.

## أسرار الإنتاج (OPS-005)

لا تُلتزم قيم حقيقية في git. المسارات:

| بيئة | آلية |
|------|------|
| محلي / kind | `deploy/k8s/**/secret*-placeholder.yaml` (**dev-only**) |
| staging / production | `ExternalSecret` → AWS Secrets Manager تحت `/hrmanagement/{env}/…` |

جسر المشغّل: `deploy/k8s/platform/external-secrets/` + مخرجات Terraform `eso_role_arn`. مفاتيح IAM-004 / `hrm_backup` موثّقة في [OPS_IAC_TOPOLOGY.md](./OPS_IAC_TOPOLOGY.md) و[SERVICE_SECRET_ROTATION.md](./SERVICE_SECRET_ROTATION.md).

## Realm Export / Import

لا تعتمد على إعداد يدوي غير قابل لإعادة الإنتاج. صدّر الـrealm بعد اكتمال الإعداد:

```bash
# Linux/macOS
./scripts/export-keycloak-realm.sh hrm-keycloak hrmanagement

# Windows PowerShell
.\scripts\export-keycloak-realm.ps1 -Container hrm-keycloak -Realm hrmanagement
```

الناتج: `docker/keycloak/realm-hrmanagement-export.json`.

**قبل الالتزام بالمستودع:** راجع الملف وأزل أي `secret` أو كلمات مرور. للاستيراد في بيئة جديدة:

```bash
# مثال — Keycloak 24 مع مجلد استيراد
docker exec -it hrm-keycloak /opt/keycloak/bin/kc.sh import \
  --file /opt/keycloak/data/import/realm-hrmanagement-export.json
```

أو اربط المجلد عبر volume في compose عند الإقلاع (`--import-realm` في أمر `start`).

## أدوار Realm (صلاحيات الوحدات)

أنشئ في Keycloak → **Realm roles** الأسماء التالية (بدون مسافات، حرفياً كما هي). ربطها بالمستخدمين أو بمجموعات. الدور **`hr-admin`** يمنح كل الوحدات في التطبيق.

| الدور | الوحدة |
|--------|--------|
| `hr-admin` | كل الوحدات |
| `hr-core` | HR.Core |
| `hr-contracts` | HR.Contracts |
| `hr-health` | HR.Health |
| `hr-movements` | HR.Movements |
| `hr-training` | HR.Training |
| `payroll` | Payroll |
| `idcard` | IDCard |
| `records-archive` | Records.Archive |
| `admin-logistics` | Admin.Logistics |
| `it-infra` | IT.Infra |
| `dev-analytics` | Dev.Analytics |
| `compliance` | Compliance |
| `auth-directory` | Auth.Directory |
| `platform-admin` | مشغّل المنصة — عبور الجهات مع تدقيق (TEN-001)؛ واجهات OBS-003 legal hold / chain-verify |
| `branch-manager` | مدير فرع (نطاق وحدة عبر `scope_org_unit_id`) |
| `hr-employee` | بوابة الخدمة الذاتية |

**أدوار PostgreSQL لتدقيق البوابة (OBS-003):** `hrm_audit_writer` (INSERT) و`hrm_audit_reader` (SELECT) — انظر `scripts/sql/obs003-audit-db-roles.sql`. حساب التطبيق لا يملك UPDATE/DELETE على `gateway_audit_entries`.

**سمات المستخدم الموصى بها (User attributes → token claims):**

| السمة | Claim في Access Token | الاستخدام |
|--------|----------------------|-----------|
| `employeeId` | `employee_id` / `employeeId` | ربط ESS بالموظف |
| `scope_org_unit_id` | `scope_org_unit_id` | جذر نطاق الفرع (IAM-002) |
| `tenant_id` | `tenant_id` | هوية الجهة (وزارة/هيئة) — TEN-001 |

أضف User Attribute Mapper لكل سمة في عميل البوابة (أو dedicated scope) بحيث تظهر في **access token**. التطبيق يرفض اختيار الجهة من العميل؛ المصدر الوحيد الموثوق هو الـ JWT.

**Mapper موصى به:** في العميل (Clients) → **Client scopes** / **Mappers**، أضف mapper لـ **realm roles** داخل **access token** بحيث يظهر claim `realm_access` (الافتراضي في Keycloak غالباً كافٍ). التطبيق يقرأ الأدوار من **access token** بعد تسجيل الدخول.

## Keycloak خلف الحافة (PLAN-36)

على الخادم الواحد يقف Keycloak خلف Caddy ([OPS_VPS_SINGLE_HOST.md](OPS_VPS_SINGLE_HOST.md)):

- `KC_PROXY_HEADERS=xforwarded` بدل `KC_PROXY=edge` المهجور في 24؛ Caddy وحده يصل إليه من خارج شبكة `data`، ولا يمرّر
  `X-Forwarded-For` الذي يرسله العميل.
- `KC_HOSTNAME=${SSO_DOMAIN}` و`KC_HOSTNAME_STRICT_HTTPS=true`، و`KC_HOSTNAME_PORT=-1` (الملف الأساسي يضبط 8088
  للتطوير؛ بدونه حمل التحويل `:8088`)، و`KC_HOSTNAME_STRICT_BACKCHANNEL=false` لأن الخدمات تصل إلى
  `http://keycloak:8080` على الشبكة الداخلية. لذلك `Keycloak:RequireHttpsMetadata=false` للخدمات (السلطة داخلية)،
  و`Keycloak:AdditionalValidIssuers=https://${SSO_DOMAIN}/realms/hrmanagement`.
- `/admin*` و`/realms/master*` يردّان 403 من الإنترنت إلا من `ADMIN_ALLOWED_CIDRS`؛ والبديل نفق SSH.
- realm العرض (`prepare-keycloak-import.ps1 -Profile VpsDemo -AppDomain ...`) يحمل الأسرار عناصر نائبة `${NAME}`
  يحلّها Keycloak من بيئته عند الاستيراد الأول، فلا تخرج كلمة مرور العرض ولا أسرار العملاء من الخادم.
- الحاوية تستورد الـ realm في أول إقلاع فقط؛ تغيير الاسم لاحقاً: [OPS_VPS_SINGLE_HOST.md §8](OPS_VPS_SINGLE_HOST.md).

## إعدادات الكوكي والإنتاج

- `HrAuth:CookieSameSite` — القيمة الافتراضية `Lax`؛ غيّر عند الحاجة (مثلاً `None` يتطلب `Secure`).
- `HrAuth:CookieSecureAlways` — `true` في الإنتاج مع HTTPS فقط.
- `UseForwardedHeaders` — `true` خلف بروكسي عكسي (انظر `appsettings.Production.json` في البوابة).
- `Cors:AllowedOrigins` — مصفوفة نطاقات مسموحة عند استدعاء API من متصفح.

## مسارات الواجهة `/ui/...`

تُعرَّف في البوابة (YARP) مع `PathRemovePrefix`. بعد تسجيل الدخول عبر البوابة، جرّب الروابط من لوحة البوابة الرئيسية. قائمة تحقق يدوية: [UI_CHECKLIST.md](./UI_CHECKLIST.md).
