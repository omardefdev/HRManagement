# PLAN-39 — Keycloak الإنتاجي كشيفرة ومفاتيح Data Protection المستمرة (OPS-C)

**Scope:** OPS — [دراسة الفجوات](../PRG_002_OPERATIONS_GAP_STUDY.md): G-OPS-03، G-OPS-04  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-36 (الحافة وCaddy وvolumes حلقات المفاتيح)، PLAN-37 (`deploy/wiring/service-wiring.json` مصدر عملاء الخدمات ونطاقاتها، والملف الشبيه بالإنتاج)، PLAN-38 (حاوية التهيئة لإنشاء دور Keycloak في PostgreSQL، و`init-secrets`)  
**Related cards:** IAM-001، SEC-004، PRV-001 (تشفير البيانات الشخصية)، IAM-004 (عملاء S2S في Keycloak)

## Goal

- **Realm كشيفرة.**
  - يُبنى realm `hrmanagement` من مصادر مُودعة في المستودع بلا تكرار (القائمة هنا، وعملاء الخدمات من مصفوفة PLAN-37)، فيه البوابة، و`directory-sync`، وكل عميل خدمة `svc-*` في المصفوفة (14 بحسب القالب الحالي)، ونطاقات S2S السبعة، وmappers الجمهور والجهة.
  - يُستورد تلقائياً على خادم جديد، وتطابق كل نشر أسرار العملاء مع `.env`.
  - المصدر نفسه يخدم ملفات التعريف `Production` و`Demo` و`Acceptance`، ولا تختلف إلا في المستخدمين والعناوين. فمكدّس القبول يختبر إعداد الإنتاج نفسه.
- **Keycloak بلا امتيازات زائدة.** يتصل بقاعدته بدور خاص لا بالحساب الإداري. والإعدادات الحساسة في الإنتاج مضبوطة: `sslRequired=external`، والحماية من التخمين، وسياسة كلمات المرور، وإيقاف `directAccessGrants` لعميل البوابة.
- **المفاتيح لا تضيع.** حلقة مفاتيح Data Protection لكل خدمة مستمرة. وفقدانها لا يمر بصمت: HR.Core يرفض الإقلاع إن لم يستطع فك رقم وطني مخزّن، بدل أن يولّد حلقة جديدة ويكسر شاشات الموظفين.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-39-1 مصدر الـ realm | مصدران لا يتكرران: عملاء الخدمات `svc-*` ونطاقاتهم ومتغير سرّهم من `deploy/wiring/service-wiring.json` (PLAN-37)، وكل ما عداها (الأدوار، عميل البوابة، `directory-sync`، جمهور كل نطاق، mappers، إعدادات الأمان) في `docker/keycloak/realm-manifest.json`. `render-realm.ps1` يدمجهما لكل ملف تعريف. وسكربت التطوير `sync-keycloak-dev-realm.ps1` يقرأ المصدرين نفسيهما | §1 |
| D-39-2 الاستيراد والمطابقة | استيراد أول بـ `--import-realm` (لا يكتب فوق realm قائم)، ثم مطابقة قابلة للإعادة بـ `kcadm.sh` في كل نشر، **للعملاء والنطاقات والأسرار وعناوين التحويل فقط**، ولا تمس المستخدمين أبداً | §2 |
| D-OPS-2 أسماء المضيفين | من PLAN-36: `APP_DOMAIN` و`SSO_DOMAIN` | §1 |
| D-39-3 مكان حلقات المفاتيح وحمايتها | volume مسمّى لكل خدمة (وصله PLAN-36). التشفير بشهادة `DataProtection:CertificatePath` اختياري: معطل في العرض، وموصى به للتجربة | §4 |
| D-39-4 فقدان المفاتيح | HR.Core يرفض الإقلاع. مفتاح طوارئ `DataProtection:AllowKeyRingReset=true` يسمح به مع `Critical` ومقياس | §4 |
| D-39-5 أول مدير للجهة | مستخدم bootstrap من `.env` (`HRM_BOOTSTRAP_ADMIN_USER`، كلمة مرور مؤقتة مولّدة) بدور `hr-admin`، وإجراء إلزامي `UPDATE_PASSWORD` عند أول دخول. لا مستخدمين آخرين في ملف Production | §1 |
| D-39-6 سياسة كلمات المرور والتخمين | `length(12) and notUsername and passwordHistory(3)`؛ `bruteForceProtected=true` بعد 5 محاولات وقفل 15 دقيقة | §1 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-39-ops-keycloak-dataprotection` | — |
| 0.2 | التحقق من المتطلبات | `docker-compose.vps.yml` وvolumes `dp_*` (PLAN-36)؛ `hrm-bootstrap` و`init-secrets` (PLAN-38) | موجودة؛ وإلا توقف وأبلغ |
| 0.3 | تصدير الـ realm الحالي للقبول | `scripts/export-keycloak-realm.ps1` على مكدّس القبول | `reports/plan39-realm-acceptance-export.json` (بلا أسرار)، مرجع للمقارنة |
| 0.4 | جرد العملاء والنطاقات المطلوبة | من compose (`ServiceIdentity__ClientId`، `ServiceIdentity__Scope`، `ServiceIdentity__ApiAudience`، `Keycloak__AdditionalValidAudiences`) ومن [sync-keycloak-dev-realm.ps1:505-537](../../scripts/sync-keycloak-dev-realm.ps1) | جدول في `reports/plan39-baseline.md` |
| 0.5 | جرد مستخدمي Data Protection | `AddHrDataProtection` في 15 مشروعاً، و`CreateProtector` | HR.Core وحده يشفّر بيانات مخزّنة ([PiiFieldEncryptionService.cs:21](../../src/Shared/HRManagement.ServiceDefaults/Privacy/PiiFieldEncryptionService.cs)، [CandidatePortalSession.cs:49](../../src/Services/HRCore/HR.Core.Web/Services/CandidatePortalSession.cs)) |

## In scope

### 1. Realm كشيفرة من مصدر واحد (G-OPS-03)

**المشكلة:**
- الإنتاج يشغّل `command: start` دون استيراد ([docker-compose.production.yml:105](../../docker-compose.production.yml)).
- القالب الوحيد تطويري:
  - `"sslRequired": "none"` ([realm-hrmanagement-dev.template.json:5](../../docker/keycloak/realm-hrmanagement-dev.template.json))؛
  - عناوين `localhost` ([:403-412](../../docker/keycloak/realm-hrmanagement-dev.template.json))؛
  - `directAccessGrantsEnabled: true` لعميل البوابة ([:415](../../docker/keycloak/realm-hrmanagement-dev.template.json))؛
  - سر واحد لكل عملاء الخدمات ([:501](../../docker/keycloak/realm-hrmanagement-dev.template.json)).
- القالبان يخلوان من `svc-hr-health` و`svc-hr-movements` و`svc-dev-analytics` و`svc-compliance`، ومن `dsar.subject.read` و`payroll.grades.read` و`file-storage.service-upload`. ولا تُنشأ إلا بسكربت التطوير بسر ثابت ([sync-keycloak-dev-realm.ps1:15,510-535](../../scripts/sync-keycloak-dev-realm.ps1)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| القائمة | `docker/keycloak/realm-manifest.json` (جديد، بلا أسرار) | المحتوى: الأدوار (من القالب الحالي، ويضيف إليها PLAN-43 `workflow-admin` وPLAN-47 `policy-author`/`policy-approver`)؛ وعميل البوابة (عناوين من `{APP_URLS}`)؛ و`directory-sync`؛ وجمهور كل نطاق S2S (`api-*`)؛ وعملاء `svc-*` **لا تُكتب هنا**: تُقرأ من `service-wiring.json` (`clientId`، `scopes`، `secretEnv`)، فيضم الـ realm كل عميل في المصفوفة، ومنه الأربعة الغائبون عن القالب (`svc-hr-health` و`svc-hr-movements` و`svc-dev-analytics` و`svc-compliance`). و`svc-file-storage` (في القالب، بلا استدعاء صادر اليوم) يبقى فقط إن أبقاه جرد PLAN-37 في المصفوفة. والنطاقات السبعة الحالية؛ وسمات ملف المستخدم `tenant_id` و`employeeId` و`scope_org_unit_id` (تحرير المدير فقط)؛ وmappers المطالبات الثلاث على عميل البوابة (كما في [:460-488](../../docker/keycloak/realm-hrmanagement-dev.template.json))؛ وإعدادات الأمان لكل ملف تعريف |
| المولّد | `scripts/keycloak/render-realm.ps1` (جديد) | `-Profile Production\|Demo\|Acceptance -EnvFile`. يدمج القائمة و`service-wiring.json` مع الأسرار من `.env` (`KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`، `KEYCLOAK_DIRECTORY_SYNC_SECRET`، `SVC_*_CLIENT_SECRET`) ويكتب إلى `artifacts/keycloak/<profile>/realm-hrmanagement.json` خارج git. **Production:** `sslRequired=external`، و`bruteForceProtected`، و`passwordPolicy`، و`directAccessGrantsEnabled=false` لعميل البوابة، ومستخدم bootstrap واحد. **Demo:** المستخدمون التجريبيون بـ `DEMO_USERS_PASSWORD`. **Acceptance:** كل شخصيات `testdata/acceptance/personas.json` بالمنطق الحالي (61 اليوم، وتضيف خطط لاحقة شخصياتها)، مع `directAccessGrantsEnabled=true` لأن أداة القبول تستخدم password grant ([AcceptanceHarness.cs:118](../../src/Tests/HRManagement.AcceptanceTests/AcceptanceHarness.cs)) |
| غلاف التوافق | `scripts/prepare-keycloak-import.ps1` | يستدعي `render-realm.ps1`. ملف التعريف `VpsDemo` الذي أضافه PLAN-36 يصبح `Demo` بعناوين `APP_DOMAIN` |
| سكربت التطوير | `scripts/sync-keycloak-dev-realm.ps1` | يقرأ عملاء الخدمات ونطاقاتهم من `service-wiring.json` (منذ PLAN-37، فقد حُذفت القوائم المكتوبة في [:505-537](../../scripts/sync-keycloak-dev-realm.ps1))، والأدوار وجمهور النطاقات من القائمة، ويبقى للمطورين |
| حذف القديم | `docker/keycloak/realm-hr-services.json`، `realm-rel001-staging.json` | يُحذفان أو يُوسمان «مهمل» مع إحالة إلى القائمة |
| تكافؤ مع المصفوفة | `Ops039RealmManifestParityTests.cs` (وحدة) | الـ realm المولَّد لكل ملف تعريف: كل عميل ونطاق في `service-wiring.json` موجود ومُسند كما في المصفوفة، ولكل نطاق جمهور في القائمة، وكل `ServiceIdentity__ApiAudience` في سلسلة الإنتاج هدف لنطاق ما. وهو الفحص الذي يضيفه PLAN-39 إلى قاعدة Keycloak في `ProductionWiringMatrixTests` (PLAN-37 §2، القاعدة 5)، فلا يُعاد تعريف العملاء هنا |

**القواعد:**
1. لا سر في القائمة ولا في المستودع. الأسرار من `.env` وقت التوليد فقط.
2. ملفات التعريف الثلاثة تختلف في المستخدمين وعناوين التحويل وإعدادات القبول المذكورة فقط. أي فرق آخر يفشل `Ops039RealmManifestParityTests`.
3. لا عميل S2S يحمل `directAccessGrantsEnabled` أو `standardFlowEnabled`.

### 2. الاستيراد والمطابقة على الخادم (G-OPS-03)

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| استيراد أول | `docker-compose.production.yml` (`keycloak`) | `command: ["start", "--import-realm"]` (أضافه PLAN-36)، وvolume `keycloak_import` يكتبه `remote-deploy.sh` من الملف المولّد ثم يحذف النسخة المؤقتة من المضيف |
| المطابقة | `deploy/keycloak/reconcile.sh` (جديد) | خدمة لمرة واحدة `keycloak-reconcile` من صورة Keycloak نفسها (`kcadm.sh` متاح)، بعد أن يصبح `keycloak` `healthy`. تفعل: `config credentials` بالمدير الرئيسي، ثم لكل عميل في القائمة إنشاء إن غاب، و`update clients/<id> -s secret=… -s redirectUris=…`، وربط النطاقات. لا `users` ولا `roles-mapping` |
| ترتيب الإقلاع | `depends_on` | التطبيقات التي تطلب توكن S2S عند الإقلاع تنتظر `keycloak-reconcile: service_completed_successfully` |

**القواعد:**
1. تشغيل المطابقة مرتين لا يغيّر شيئاً، ويسجّل «0 changes».
2. تدوير `SVC_PAYROLL_CLIENT_SECRET` في `.env` ثم النشر يجعل السر القديم مرفوضاً والجديد مقبولاً دون تدخل يدوي.
3. فشل المطابقة يُفشل النشر، فلا تُقلع خدمة بسر لا يطابق.

### 3. Keycloak بأقل صلاحية وبإعدادات حافة صحيحة (G-OPS-03)

**المشكلة:**
- Keycloak يتصل بقاعدته بالحساب الإداري (`KC_DB_USERNAME: ${POSTGRES_USER}`، [docker-compose.production.yml:115](../../docker-compose.production.yml)).
- `KEYCLOAK_DIRECTORY_SYNC_SECRET` إلزامي ([:524](../../docker-compose.production.yml)) بينما التزامن معطل في الإنتاج ([Auth.Directory appsettings.json:23-24](../../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json)).

**التصميم:**
- دور `keycloak` في `ensure-roles.sql` (PLAN-38): `LOGIN`، ومالك `keycloak_db`، وكلمة مرور `POSTGRES_KEYCLOAK_PASSWORD`، و`REVOKE CONNECT` على القواعد الأخرى.
- لخادم قائم: خطوة في التهيئة تنقل ملكية كائنات `keycloak_db` من الحساب الإداري إلى `keycloak`، داخل `keycloak_db` وحدها، وبعد نسخ يدوي موثق.
- `KC_DB_USERNAME: keycloak`، و`KC_DB_PASSWORD: ${POSTGRES_KEYCLOAK_PASSWORD:?…}`، و`KC_DB_POOL_MAX_SIZE: 20` (المجموع في PLAN-38).
- `KEYCLOAK_DIRECTORY_SYNC_SECRET`: يبقى عميل `directory-sync` في القائمة بسره المولّد، ويصبح المتغير في compose `${…:-}`. ويضاف إلى حارس الإعداد شرط: إلزامي فقط حين `Keycloak:DirectorySync:Enabled=true`.
- تحقق التكامل مع الحافة التي بناها PLAN-36: `/admin` و`/realms/master` محجوبان من الخارج، والمُصدِر في التوكن `https://${SSO_DOMAIN}/realms/hrmanagement`، و`KC_HOSTNAME_STRICT_BACKCHANNEL=false` يعطي الخدمات نقطة توكن داخلية.

### 4. حلقات Data Protection: حارس، وكاشف فقدان، وتشفير اختياري (G-OPS-04)

**المشكلة:**
- `AddHrDataProtection` لا يحفظ الحلقة إلا إذا ضُبط `DataProtection:KeyRingPath` ([DataProtectionExtensions.cs:24-43](../../src/Shared/HRManagement.ServiceDefaults/DataProtectionExtensions.cs)). وفي الإنتاج كان يُضبط للبوابة وحدها ([docker-compose.production.yml:223](../../docker-compose.production.yml)). PLAN-36 وصل الـ volumes، ولا شيء يمنع نسيانها.
- `PiiFieldEncryptionService` يرمي `CryptographicException` عند كل قراءة لموظف بعد فقدان المفتاح ([PiiFieldEncryptionService.cs:38-45](../../src/Shared/HRManagement.ServiceDefaults/Privacy/PiiFieldEncryptionService.cs))، دون أي إنذار عند الإقلاع.
- الحلقة غير مشفرة على القرص.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `DataProtectionStartupGuard` | `src/Shared/HRManagement.ServiceDefaults/DataProtectionStartupGuard.cs` (جديد) | في Production (أو `DataProtection:RequirePersistentKeyRing=true`): يفشل الإقلاع إن كان `KeyRingPath` فارغاً، أو غير قابل للكتابة (ملف اختبار يُكتب ويُحذف)، أو `SetApplicationName` غير مضبوط |
| حماية بشهادة | `AddHrDataProtection` | إن ضُبط `DataProtection:CertificatePath` (+`CertificatePassword` من البيئة): `ProtectKeysWithCertificate`، و`UnprotectKeysWithAnyCertificate` للشهادات السابقة في `DataProtection:PreviousCertificatePaths`. المفاتيح القديمة غير المشفرة تبقى مقروءة |
| مولّد الشهادة | `deploy/vps/make-dp-cert.sh` (جديد) | `openssl` RSA 3072، صلاحية 10 سنوات، إلى `/opt/hrm/secrets/dp-cert.pfx` (صلاحية 600). كلمة المرور تُضاف إلى `.env` |
| كاشف الفقدان | `src/Services/HRCore/HR.Core.Infrastructure/Data/EmployeePiiKeyRingCanary.cs` (جديد، `IHostedService`) | عند الإقلاع يقرأ بـ SQL خام أول `NationalId` يبدأ بـ `CfDJ8` (متجاوزاً معترض التجسيد، [EmployeePiiProtectionInterceptor.cs:47-52](../../src/Services/HRCore/HR.Core.Infrastructure/Data/EmployeePiiProtectionInterceptor.cs)) ويحاول فكه. إن فشل: في Production يوقف الإقلاع برسالة «حلقة مفاتيح حماية البيانات لا تطابق البيانات المخزنة…» إلا مع `DataProtection:AllowKeyRingReset=true`. إن لم توجد بيانات محمية: لا شيء |
| ملف القبول الشبيه بالإنتاج | `docker-compose.acceptance-prodlike.yml` | `DataProtection__RequirePersistentKeyRing: "true"` للخدمات، فيُختبر الحارس في القبول |
| تصدير المفاتيح للنسخ | `deploy/vps/export-dp-keys.sh` (جديد) | أرشيف مشفر للـ volumes `dp_*` (مع الشهادة إن وجدت) إلى مسار يحدده المالك. **ضمه إلى النسخ المجدولة ← الموجة 2 (OPS-E)** |

**القواعد:**
1. لا تُولَّد حلقة جديدة بصمت فوق بيانات محمية قائمة.
2. رسالة الحارس والكاشف تسمّي الخدمة والمسار، ولا تطبع مفتاحاً ولا قيمة محمية.
3. مفاتيح البوابة وHR.Core منفصلة (`SetApplicationName` مختلف)، ولا تُدمج الحلقات.
4. k8s خارج المسار. لكن الحارس سيوقف أي pod بلا مسار مستمر، وهذا مقصود ومذكور في `OPS_VPS_SINGLE_HOST.md`.

## Out of scope

- ربط `SVC_*` و`ServiceIdentity__Scope` و`Integration__*` بالخدمات في compose وk8s ← PLAN-37. هنا الجانب الآخر فقط: العملاء والنطاقات في Keycloak، مع اختبار التكافؤ بين الجانبين.
- تشديد التحقق من الجمهور في الخدمات (رفض `hr-gateway-client` المباشر، G-IAM-13)، وإثبات البوابة ← PLAN-42.
- حجب `/admin` و`/realms/master` وTLS والترويسات الممررة على الحافة ← PLAN-36. هنا يُتحقق منها فقط.
- تزويد حساب Keycloak عند التعيين وقطعه عند الفصل ← PLAN-62 (G-IDA-01). والإنشاء الجماعي لحسابات الموظفين القائمين وتزامن الدليل ← الموجة 2 (OPS-G).
- ضم حلقات المفاتيح والشهادة والـ realm إلى النسخ المجدولة واختبار الاستعادة ← الموجة 2 (OPS-E).
- RLS المفروض وأثره ← الموجة 2 (OPS-D).
- k8s: تخزين مشترك للحلقات بين النسخ ← خارج مسار الخادم الواحد.

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37) بـ realm مولّد من القائمة بملف تعريف `Acceptance`. وAC-OPS-39a وAC-OPS-39e أيضاً على ملف الخادم الواحد محلياً (PLAN-36) بملف تعريف `Production`. كل سيناريو `[RequiredAcceptance]` مع `EvidenceRecorder("AC-OPS-39")`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-OPS-39a | volume Keycloak فارغ ← realm مستورد دون تدخل: `sslRequired=external`، و`bruteForceProtected=true`، وكل عملاء `service-wiring.json` مع البوابة و`directory-sync`، وكل نطاقاتها (7 عند هذه الخطة)؛ والمدير bootstrap يطلب تغيير كلمة المرور عند أول دخول | password grant على `hr-gateway-client` في ملف Production ← `unauthorized_client` |
| AC-OPS-39b | كل عميل `svc-*` يحصل على توكن `client_credentials` بنطاقاته، و`aud` يحوي `api-*` المتوقع (مثل `svc-compliance` + `dsar.subject.read` ← `api-hr-core`) | `svc-payroll` يطلب `dsar.subject.read` ← `invalid_scope` |
| AC-OPS-39c | توكن مستخدم يحمل `tenant_id` و`employee_id` و`scope_org_unit_id` | المستخدم يحاول تعديل `tenant_id` عبر واجهة الحساب ← رفض |
| AC-OPS-39d | تدوير `SVC_PAYROLL_CLIENT_SECRET` ثم المطابقة ← التوكن بالسر الجديد يعمل | السر القديم ← `unauthorized_client`؛ وتشغيل المطابقة ثانية ← «0 changes» |
| AC-OPS-39e | `pg_stat_activity` يظهر اتصالات Keycloak بدور `keycloak` | دور `keycloak` لا يتصل بـ `hr_core_db` |
| AC-OPS-39f | إنشاء موظف برقم وطني ← `docker compose up -d --force-recreate` لكل الخدمات ← تفاصيل الموظف تعرض الرقم، ونموذج بـ antiforgery أُنشئ قبل الإعادة يُقبل بعدها | — |
| AC-OPS-39g | مع `DataProtection__AllowKeyRingReset=true`: الإقلاع يتم، ويُسجَّل `Critical` | حذف volume `dp_hr-core` مع بقاء البيانات ← HR.Core يخرج برسالة الكاشف، ولا يُقلع بحلقة جديدة |
| AC-OPS-39h | — | خدمة في Production بلا `DataProtection__KeyRingPath` ← خروج برسالة الحارس |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Ops039|FullyQualifiedName~DataProtection"
# تكامل حقيقي: Keycloak 24 وPostgreSQL
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Category=Testcontainers&FullyQualifiedName~Ops039"
pwsh scripts/keycloak/render-realm.ps1 -Profile Acceptance -EnvFile .env
pwsh scripts/keycloak/render-realm.ps1 -Profile Production -EnvFile deploy/vps/.env.vps-local
# الملف الشبيه بالإنتاج (بعد PLAN-37) على volumes جديدة لـ Keycloak
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcOps39"
# انحدار: المسارات التي تعتمد على شخصيات القبول وS2S
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcHcm25|FullyQualifiedName~AcHcm30|FullyQualifiedName~AcOps36"
```

## Definition of done

- realm يُبنى من القائمة لكل ملفات التعريف، ويُستورد تلقائياً، وتطابقه المطابقة في كل نشر.
- لا سر مشترك لعملاء الخدمات، ولا `sslRequired=none` ولا password grant لعميل البوابة في Production.
- Keycloak يعمل بدوره الخاص.
- كل خدمة في Production تحفظ حلقتها في مسار مستمر، وHR.Core يرفض الإقلاع عند فقدان المفاتيح.
- AC-OPS-39a..h = Pass في `acceptance-evidence/AC-OPS-39/`. وانحدار AcHcm25/30 وAC-OPS-36 أخضر.
- تحديث سطر «الحالة» في G-OPS-03 وG-OPS-04 في الدراسة، و`KEYCLOAK_AND_SECRETS.md` (استبدال «ابدأ من هنا» اليدوي بالقائمة والمولّد والمطابقة)، و`OPS_VPS_SINGLE_HOST.md` (الشهادة وتصدير المفاتيح).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md` |
| Realm | `docker/keycloak/realm-manifest.json`، `scripts/keycloak/render-realm.ps1`، `deploy/keycloak/reconcile.sh`، `scripts/prepare-keycloak-import.ps1` (غلاف)، `scripts/sync-keycloak-dev-realm.ps1` (يقرأ القائمة) |
| Compose | `docker-compose.production.yml` (`keycloak`، `keycloak-reconcile`، دور القاعدة)، `docker-compose.acceptance-prodlike.yml` (`RequirePersistentKeyRing`) |
| Bootstrap | `deploy/bootstrap/ensure-roles.sql` (دور `keycloak`) |
| Code | `DataProtectionStartupGuard.cs`، `DataProtectionExtensions.cs` (شهادة)، `EmployeePiiKeyRingCanary.cs`، تمديد حارس الإعداد لشرط `directory-sync` |
| Scripts | `deploy/vps/make-dp-cert.sh`، `deploy/vps/export-dp-keys.sh` |
| Unit | `Ops039RealmManifestParityTests.cs`، `Ops039RenderRealmTests.cs` (لا أسرار في المخرَج بلا `.env`، والفروق بين الملفات محصورة)، `DataProtectionStartupGuardTests.cs` |
| Integration | `Ops039KeycloakRealmImportTests.cs` (Testcontainers Keycloak 24: استيراد ملف Production، وتوكنات العملاء، والمطابقة مرتين)، `Ops039PiiKeyRingCanaryTests.cs` (Testcontainers PostgreSQL: بيانات محمية بحلقة A، ثم إقلاع بحلقة B ← رفض) |
| Acceptance | `AcOps39KeycloakDataProtectionTests.cs` |
| Docs | `KEYCLOAK_AND_SECRETS.md`، `OPS_VPS_SINGLE_HOST.md`، `IAM_001_JWT_KEYCLOAK.md` (إعدادات الإنتاج)، أسطر «الحالة» في الدراسة |

## Risks

| الخطر | التخفيف |
|-------|---------|
| `--import-realm` لا يحدّث realm قائماً، فتنحرف بيئة قديمة عن القائمة | المطابقة تغطي العملاء والنطاقات والأسرار والعناوين. وما عداها (سياسة كلمات المرور…) يطبّقه `reconcile.sh` بـ `update realms/hrmanagement` لحقول مسماة |
| صورة Keycloak (UBI micro) بلا أدوات تحليل JSON | `kcadm.sh` بـ `--fields` و`--format csv`؛ والبديل حاوية `curl`+`jq` على Admin REST |
| تغيير `directAccessGrants` يكسر أداة القبول | ملف `Acceptance` يبقيه مفعلاً، ويثبت ذلك `Ops039RenderRealmTests` |
| نقل ملكية `keycloak_db` على خادم عرض قائم | خطوة منفصلة موثقة تسبقها نسخة يدوية؛ والخادم الجديد يبدأ بالدور مباشرة |
| ضياع الشهادة يعني ضياع الحلقة كلها | الشهادة اختيارية في العرض؛ وفي التجربة يُشترط حفظها خارج الخادم (موثق)، وضمها إلى النسخ ← OPS-E |
| الكاشف يبطئ الإقلاع على جداول كبيرة | استعلام `LIMIT 1` بفهرس، دون فحص الجدول كله |

## Effort estimate

8–9 أيام عمل:
- المرحلة 0: 0.5
- القائمة والمولّد وملفات التعريف: 2.5
- الاستيراد والمطابقة: 1.5
- دور Keycloak والإعدادات: 0.5
- الحارس والكاشف والشهادة: 1.5
- الاختبارات والقبول والوثائق: 1.5–2
