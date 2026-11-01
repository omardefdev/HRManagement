# PLAN-37 — توصيل الخدمات بإعدادات الإنتاج وشحن حزمة أنظمة الخدمة (INT)

**Scope:** INT — [دراسة الترميز والهيكل](../COD_ORG_GAP_STUDY.md): G-COD-01، G-COD-06 · [دراسة الصلاحيات](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-03 · [دراسة السياسات](../POL_001_GAP_STUDY.md): G-POL-01 · [دراسة الحركات](../FUN_003_MOVEMENTS_GAP_STUDY.md): G-MOV-02 (شحن الحزمة) · [دراسة الفصل](../FUN_002_SEPARATION_GAP_STUDY.md): جزء الشحن والتوصيل من G-SEP-02  
**Status:** Implemented — 2026-11-01 (التقرير: `reports/plan37-final-report.md`)  
**Priority:** P0  
**Depends on:** PLAN-36 (إقلاع الإنتاج على خادم واحد)  
**Related cards:** COD-001، IAM-004 (هوية الخدمات)، SYR-003 (`service-regimes.json`)، OPS-005، PLAN-25/29/33 (توصيل القبول الذي لم يُنقل إلى الإنتاج)، AC-30 (رفض S2S)

## Goal

أن تحصل **كل خدمة، بإعدادات الإنتاج الفعلية** (compose وk8s)، على ما تقرؤه شيفرتها بالضبط: عناوين الخدمات الأخرى، وهوية الخدمة، ونطاقاتها، وسرّها، وحزمة أنظمة الخدمة. وأن يفشل الإقلاع بوضوح عند أي نقص، لا عند أول طلب.

النتائج المطلوبة:

1. **مصفوفة توصيل واحدة** قابلة للقراءة آلياً. لكل خدمة: المفاتيح التي تقرؤها شيفرتها، وعميل Keycloak، والنطاقات، ومتغير السر. وفحص آلي يفشل إن نقص مفتاح في compose الإنتاج أو k8s أو سكربت Keycloak، أو إن سجّلت الشيفرة عميلاً لا تعرفه المصفوفة.
2. **يعمل في الإنتاج** ما يعمل الآن في القبول وحده: طلب الإجازة والسلفة والفصل والتجربة، ومعاملة التعيين، وقرار التعيين، وأرقام البطاقات والأجهزة والقضايا ومراجع الأرشيف.
3. **مستهلكات الأرشيف لا تُسقط الرسائل بصمت.**
4. **`service-regimes.json` في كل صورة تقرؤه** (HR.Core وHR.Contracts وHR.Movements)، مع بصمة SHA-256 في سجل الإقلاع، وفحص جاهزية يفشل إن كانت القواعد فارغة.
5. **الملف الشبيه بالإنتاج يأخذ التوصيل من ملفات الإنتاج نفسها** ولا يرث إصلاحات القبول. فيفشل حيث يفشل الإنتاج.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-INT-1 طريقة شحن `service-regimes.json` | ملف `Content` يعرّفه `HRManagement.SharedKernel.csproj` وينتقل عبر مراجع المشاريع إلى `seed-packs/syr003-service-regimes-v1/regimes.json` في مخرجات كل خدمة ونشرها. ومفتاح اختياري `LegalPacks:ServiceRegimesPath` لملف خارجي (volume) يتقدم على المضمَّن. لا قراءة عبر API في هذه الموجة (POL-F، الموجة 2) | §5 |
| D-INT-2 شكل التوصيل في k8s | patch استراتيجي لكل Deployment في `deploy/k8s/base/<svc>/wiring-patch.yaml`. يضبط `Integration__*` و`ServiceIdentity__ClientId/Scope`، و`ServiceIdentity__ClientSecret` من `secretKeyRef` على `hr-app-secrets` بمفتاح `SVC_*`. **عزل** السر لكل خدمة (ExternalSecret مستقل) ← PLAN-42 | §3 |
| D-INT-3 المفاتيح القديمة `Services:HRCore:BaseUrl` و`HrCore:BaseUrl` | تبقى احتياطاً مُهملاً (deprecated) يُكتب عنه تحذير في السجل. المفتاح المعتمد `Integration:HrCoreBaseUrl`. وتُحذف في الموجة 2 | §4 |
| D-INT-4 خدمات تسجّل عميل HR.Core ولا تستدعيه (`AddHrPolicyEnforceClient` في Health وTraining وDevAnalytics وPayroll وMovements وRecordsArchive وIT) | تأخذ العنوان، لأن التسجيل يتطلبه بعد هذه الخطة، ولا تأخذ نطاق `hr-core.workflow` (أقل صلاحية). وإزالة التسجيلات غير المستخدمة قرار PLAN-49 | §1، §3 |
| D-INT-5 السلوك عند غياب العنوان | فشل الإقلاع في كل بيئة غير `Development`. وفي `Development` خارج الحاويات فقط يُقبل `http://localhost:5010` الافتراضي. وفي أي حاوية يُرفض `localhost` (على نمط `HrGatewayConfigurationExtensions.RequireInternalBaseUrl`) | §4 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-37-int-production-wiring-legal-pack` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "Architecture\|CodeGeneration\|Syr00\|Sec001\|Hard001"` | أخضر، أو إخفاقات موثقة في `reports/plan37-baseline.md` |
| 0.3 | جرد ما تقرؤه الشيفرة | `rg -n "Integration:\|Services:[A-Za-z]+:BaseUrl\|HrCore:BaseUrl\|HrGateway:InternalBaseUrl\|ServiceIdentity:" src --glob "*.cs"` و`rg -n "AddHr(CodeGeneration\|PolicyEnforceClient\|CoreLookupClient\|CoreActiveRegimeClient\|CoreServiceRegimeClient\|FileReferenceClient\|DsarDeletion)\|AddServiceAuthenticatedClient\|AddPayrollGradeLookup" src/Services` | جدول خدمة × مفتاح × عميل في `reports/plan37-baseline.md` يطابق §1 أو يشرح الفرق |
| 0.4 | جرد ما يضبطه النشر | سلسلتا الإنتاج اللتان يعرّفهما PLAN-36 في `scripts/vps/lint-compose.ps1`: `docker-compose.yml + services + images + production` وسلسلة الخادم الواحد بإضافة `docker-compose.vps.yml`، كلٌّ بـ `config --format json` وملف `.env` وهمي. ثم `kubectl kustomize deploy/k8s/overlays/production-ha` إن توفر | المتغيرات الفعلية لكل خدمة في التقرير |
| 0.5 | إثبات العطل الحالي | `docker compose -p hrm-acceptance <ملفات القبول> --profile full-stack run --rm --no-deps --entrypoint ls hr-contracts-web /app/seed-packs` | يُسجَّل غياب الحزمة (أو وجودها) في التقرير |

---

## In scope

### 1. مصفوفة التوصيل مصدراً واحداً للحقيقة (G-IAM-03، G-COD-01)

**المشكلة:**
- كل عميل HR.Core يقرأ `Integration:HrCoreBaseUrl`. فإن غاب سجّل `HttpClient` بلا عنوان دون أي تحذير ([CodeGenerationClient.cs:79-85](../../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)، [PolicyEnforceClient.cs:180-192](../../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)، [HrCoreLookupClient.cs:80-84](../../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreLookupClient.cs)). أو سقط على `http://localhost:5010` ([HrCoreActiveRegimeClient.cs:44-45](../../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreActiveRegimeClient.cs)، [HrCoreServiceRegimeClient.cs:44-45](../../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreServiceRegimeClient.cs)).
- مكدّس الإنتاج يضبط العنوان لـ payroll وadmin-logistics وcompliance فقط ([docker-compose.services.yml:149, 257, 361](../../docker-compose.services.yml)، [docker-compose.production.yml:371, 442](../../docker-compose.production.yml)). و`hr-app-config` في k8s بلا أي مفتاح `Integration__*` ([configmap-app.yaml:7-29](../../deploy/k8s/base/configmap-app.yaml)).
- النطاق `hr-core.workflow`، الذي يتطلبه توليد الأرقام والإنفاذ ([HrManagementAuthorizationExtensions.cs:73-77, 91-97](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، مضبوط في الإنتاج لـ admin-logistics وحدها ([docker-compose.production.yml:441](../../docker-compose.production.yml)).
- HR.Core بلا `Integration__PayrollBaseUrl` و`Integration__FileStorageBaseUrl` وبلا النطاقين `payroll.grades.read` و`file-storage.service-upload` في الإنتاج ([docker-compose.production.yml:256-259](../../docker-compose.production.yml)). وهي موجودة في القبول وحده ([docker-compose.acceptance.yml:60-68](../../docker-compose.acceptance.yml)).
- سياسات الخادم ترفض عملاء تستعملهم الشيفرة فعلاً:
  - Compliance يحقن `IFileReferenceClient` ([ComplaintsController.cs:15](../../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs))، لكن `svc-compliance` ليس في قائمة `Service.FileStorageReferences` ([ServiceAuthorizationExtensions.cs:84-93](../../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)).
  - Auth.Directory يستعمل `IHrCoreLookupClient` ([AuthUsersController.cs:18-23](../../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs))، لكن `svc-auth-directory` ليس في قائمة `Service.HrCoreLookup` ([ServiceAuthorizationExtensions.cs:74-82](../../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs))، ولا يملك `ServiceIdentity__ClientId` في الإنتاج.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `service-wiring.json` | `deploy/wiring/service-wiring.json` | لكل خدمة: `composeService`، `k8sDeployment`، `projects[]`، `clientId`، `secretEnv`، `urls{key: value}`، `scopes[]`، `calls[]` (`client`، `endpoint`، `serverPolicy`، `scope`). وقسم `registrations` يربط كل طريقة تسجيل بمفاتيحها ونطاقها |
| `ServiceWiringMatrix` (قارئ) | `src/Tests/HRManagement.TestSupport/Wiring/ServiceWiringMatrix.cs` | تحميل المصفوفة والتحقق من مخططها، ويُستخدم في اختبارات الوحدة والقبول |
| `SERVICE_WIRING_MATRIX.md` | `docs/SERVICE_WIRING_MATRIX.md` | جدول مقروء يُولَّد من JSON (`scripts/int037-render-wiring-doc.ps1`)، ويحل محل §86 من `SERVICE_TRUST_INVENTORY.md` |

**المحتوى المطلوب (من جرد الشيفرة؛ المرحلة 0.3 تؤكده أو تصححه):**

| الخدمة | `clientId` | العناوين | النطاقات |
|--------|-----------|----------|----------|
| hr-core-web | `svc-hr-core` | `HrGateway__InternalBaseUrl`، `Integration__PayrollBaseUrl`، `Integration__FileStorageBaseUrl` | `file-storage.references payroll.grades.read file-storage.service-upload` |
| hr-contracts-web | `svc-hr-contracts` | `Integration__HrCoreBaseUrl`، `HrGateway__InternalBaseUrl` | `hr-core.workflow hr-core.lookup file-storage.references` |
| hr-movements-web | `svc-hr-movements` | `Integration__HrCoreBaseUrl` | `hr-core.lookup` |
| payroll-web | `svc-payroll` | `Integration__HrCoreBaseUrl` | `hr-core.lookup` |
| idcard-web | `svc-idcard` | `Integration__HrCoreBaseUrl`، `HrGateway__InternalBaseUrl` | `hr-core.workflow file-storage.references` |
| records-archive-web | `svc-records-archive` | `Integration__HrCoreBaseUrl`، `HrGateway__InternalBaseUrl` | `hr-core.workflow file-storage.references` |
| admin-logistics-web | `svc-admin-logistics` | `Integration__HrCoreBaseUrl`، `HrGateway__InternalBaseUrl` | `hr-core.lookup hr-core.workflow file-storage.references` |
| it-infra-web | `svc-it-infra` | `Integration__HrCoreBaseUrl`، `Services__AuthDirectory__BaseUrl` | `hr-core.workflow directory.sync` |
| compliance-web | `svc-compliance` | `Integration__HrCoreBaseUrl`، `Integration__PayrollBaseUrl`، `Integration__FileStorageBaseUrl`، `HrGateway__InternalBaseUrl` | `hr-core.lookup hr-core.workflow dsar.subject.read file-storage.references` |
| auth-directory-web | `svc-auth-directory` | `Integration__HrCoreBaseUrl` | `hr-core.lookup` |
| hr-health-web، hr-training-web، dev-analytics-web | عملاؤها الحاليون | `Integration__HrCoreBaseUrl` (+ `HrGateway__InternalBaseUrl` للتدريب) | Training: `file-storage.references`؛ والبقية بلا نطاق HR.Core (D-INT-4) |
| hr-gateway | `hr-gateway-client` | `Integration__HrCoreBaseUrl`، `Integration__AuthDirectoryBaseUrl` | — |

**القواعد:**
1. المصفوفة هي المصدر الوحيد للحقيقة. compose وk8s وسكربت Keycloak وقوائم العملاء المسموح بهم في الخادم كلها تُطابَق بها آلياً (§2).
2. لكل نطاق في المصفوفة استدعاء فعلي واحد على الأقل (`calls[]`). نطاق بلا استدعاء يُرفض (أقل صلاحية).
3. قوائم `IsAllowedClient` في `ServiceAuthorizationExtensions` تُصحَّح لتطابق المصفوفة: إضافة `svc-compliance` إلى `FileStorageReferences`، و`svc-auth-directory` إلى `HrCoreLookupS2S`. ولا يُضاف عميل لا تبرره المصفوفة.
4. HR.Core يسجّل `AddHrPolicyEnforceClient` و`AddHrCodeGeneration` ([Program.cs:277-278](../../src/Services/HRCore/HR.Core.Web/Program.cs)) ولا يحقن أياً منهما (يستعمل `ICodeGenerator` والمنسق داخلياً). يُحذف التسجيلان، فلا يحتاج HR.Core عنوان نفسه.
5. **عقد للخطط اللاحقة:** كل خطة تضيف عميل S2S أو استدعاءً أو نطاقاً، أو تغيّر سياسة الخادم لنقطة يستدعيها عميل، أو تحذف تسجيلاً، تحدّث `service-wiring.json` في الـ commit نفسه، و`ProductionWiringMatrixTests` يحكم. أمثلة معروفة: PLAN-42 (نطاق `dsar.subject.erase`، وسياسات `api/internal/*` الجديدة، وسر لكل خدمة)، وPLAN-46 (بدء الاعتماد من IDCard وCompliance)، وPLAN-49 (حذف تسجيلات عميل الإنفاذ غير المستعملة)، وPLAN-50 (نقطة التفويض الداخلية).

### 2. الفحص الآلي للتوصيل

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ProductionWiringMatrixTests` | `src/Tests/HRManagement.UnitTests/Architecture/ProductionWiringMatrixTests.cs` | أربعة فحوص (القواعد أدناه). يقرأ YAML بـ `YamlDotNet` (حزمة اختبار جديدة في `Directory.Packages.props`، الإصدار 16.3.0، وتحديث `packages.lock.json` بـ `dotnet restore --force-evaluate`) عبر `MergingParser` لدعم `<<: *anchor`، ويتجاهل الوسوم `!override`/`!reset` |
| `ComposeEffectiveEnvironment` | `src/Tests/HRManagement.TestSupport/Wiring/ComposeEffectiveEnvironment.cs` | دمج `environment` عبر ملفات compose بالترتيب، والملف الأخير يفوز، مع حل المراسي. يعيد متغيرات كل خدمة |
| `KustomizeEffectiveEnvironment` | `src/Tests/HRManagement.TestSupport/Wiring/KustomizeEffectiveEnvironment.cs` | قراءة `configmap-app.yaml` و`deployment.yaml` و`wiring-patch.yaml` لكل خدمة، ومفاتيح `hr-app-secrets.yaml` |
| `int037-wiring-check.ps1` | `scripts/int037-wiring-check.ps1` | الفحص «المُصيَّر»: `docker compose … config --format json` لسلاسل الإنتاج (تعريفات السلاسل من `scripts/vps/lint-compose.ps1` في PLAN-36، دون تكرارها) وللمكدّس الشبيه بالإنتاج، بملف `.env` وهمي، و`kubectl kustomize` للطبقات `production-ha`/`staging-ha`/`dr-secondary` إن توفر `kubectl`. يقارن بالمصفوفة ويكتب `artifacts/int037/wiring-report.json`. رمز خروج ≠ 0 عند أي نقص |

**القواعد (الفحوص الأربعة):**
1. **الشيفرة ← المصفوفة:** كل طريقة تسجيل في `registrations`، وكل قراءة لمفتاح عنوان (`"Integration:…"`، `"Services:…:BaseUrl"`، `"HrCore:BaseUrl"`، `"HrGateway:InternalBaseUrl"`) في مشاريع الخدمة، يجب أن تقابلها مدخلة في المصفوفة. وتسجيل جديد بلا مدخلة يُفشل الاختبار ويسمّي الخدمة والطريقة.
2. **المصفوفة ← سياسات الخادم:** لكل `calls[]` يُبنى `ClaimsPrincipal` لحساب خدمة (`azp=clientId`، `preferred_username=service-account-<clientId>`، `scope=<scope>`). ثم يُقيَّم بـ `IAuthorizationService` على سياسة الخادم المعلنة، بعد تسجيل `AddHrManagementAuthorization` و`AddServiceToServiceAuthorizationPolicies` الحقيقيين. والنتيجة يجب أن تكون نجاحاً. ونفس الهوية **بلا** النطاق يجب أن تُرفض (فحص سالب).
3. **المصفوفة ← النشر:** في سلسلتي الإنتاج (`docker-compose.yml` + `services` + `images` + `production`، ومعها `docker-compose.vps.yml` لسلسلة الخادم الواحد من PLAN-36)، وفي k8s (`base` + الطبقات الثلاث):
   - كل مفتاح عنوان موجود بالقيمة المتوقعة.
   - `ServiceIdentity__ClientId` يساوي `clientId`.
   - `ServiceIdentity__Scope` يساوي اتحاد النطاقات بالضبط.
   - السر في compose `${<secretEnv>:?…}`، وفي k8s `secretKeyRef` على مفتاح موجود في `hr-app-secrets.yaml`.
4. **تكافؤ الشبيه بالإنتاج:** لكل خدمة، مفاتيح التوصيل (`Integration__*`، `Services__*__BaseUrl`، `HrGateway__InternalBaseUrl`، `ServiceIdentity__ClientId`، `ServiceIdentity__Scope`) في مكدّس `…acceptance.yml + acceptance-prodlike.yml` تساوي مكدّس الإنتاج. الاستثناء الوحيد المسموح: قيمة السر المختبرية `ServiceIdentity__ClientSecret`.
5. **Keycloak:** لكل عميل، `Ensure-ServiceClient -OptionalScopes` في `scripts/sync-keycloak-dev-realm.ps1` يساوي نطاقات المصفوفة، ولكل نطاق `Ensure-ClientScope`. أما realm الإنتاج فيولّده PLAN-39 من المصفوفة نفسها، ويضيف PLAN-39 فحصه إلى هذا الاختبار.
6. **الفحص السالب للفاحص:** حالة اختبار تغذّي `ComposeEffectiveEnvironment` بنسخة من compose ينقصها `Integration__HrCoreBaseUrl` لـ `hr-contracts-web`، وتتوقع رسالة فشل تسمّي الخدمة والمفتاح.

### 3. تصحيح ملفات النشر (compose وk8s وKeycloak) (G-IAM-03، G-COD-01)

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| التوصيل في الملفات المشتركة | `docker-compose.yml` (`hr-core-web`، `hr-gateway`) و`docker-compose.services.yml` (بقية الخدمات) | العناوين و`ServiceIdentity__ClientId` و`ServiceIdentity__Scope` تُكتب **مرة واحدة** هنا، لأن الملفين مشتركان بين مكدّسَي الإنتاج والقبول. والسر في `services.yml` بقيمة مختبرية `${SVC_X:-dev-svc-secret-local-only}` كالحالي |
| `docker-compose.production.yml` | الملف القائم | تُحذف منه `ServiceIdentity__Scope` و`Integration__*` و`HrGateway__InternalBaseUrl` (تنتقل إلى الملفات المشتركة). ويبقى فيه السر الإلزامي `${SVC_X:?…}` و`ClientId` لكل خدمة، ومنها `auth-directory-web` (`svc-auth-directory`، `SVC_AUTH_DIRECTORY_CLIENT_SECRET`) |
| `docker-compose.acceptance.yml` | الملف القائم | تُحذف مفاتيح التوصيل من `hr-core-web` (`:60-68`) و`hr-contracts-web` (`:99-105`). يبقى السر المختبري فقط |
| `docker-compose.acceptance-prodlike.yml` | الملف القائم | يُحدَّث رأسه: المكدّس الشبيه يأخذ التوصيل من الملفات المشتركة، ويُعاد إنشاء **كل** الخدمات لا خدمتين |
| `wiring-patch.yaml` لكل Deployment | `deploy/k8s/base/<svc>/wiring-patch.yaml` + إدراجها في `patches` في `deploy/k8s/base/kustomization.yaml` | `env`: العناوين بأسماء Service DNS (`http://hr-core-web:8080`)، و`ServiceIdentity__ClientId`، و`ServiceIdentity__Scope`، و`ServiceIdentity__ClientSecret` بـ `valueFrom.secretKeyRef {name: hr-app-secrets, key: SVC_X_CLIENT_SECRET}` |
| `hr-app-secrets.yaml` (الطبقات) | `deploy/k8s/overlays/{production-ha,staging-ha}/external-secrets/hr-app-secrets.yaml` | إضافة `SVC_AUTH_DIRECTORY_CLIENT_SECRET` إن لم يوجد |
| سكربت Keycloak | `scripts/sync-keycloak-dev-realm.ps1:519-537` | استبدال قوائم `Ensure-ServiceClient` الثابتة بقراءة `deploy/wiring/service-wiring.json` (`ConvertFrom-Json`)، وإضافة `svc-auth-directory` بنطاقه |
| `acceptance-up.ps1` | `scripts/acceptance-up.ps1` | مفتاح جديد `-ProdLike` يضيف `docker-compose.acceptance-prodlike.yml` إلى قائمة الملفات (`:39-45`) ويشغّل كل الخدمات به |
| فحص الخادم الواحد | `scripts/vps/smoke.ps1` (PLAN-36 §8) | قلب `-RequireIntegrationWiring` إلى إلزامي: الفحوص التي كانت تُبلَّغ `Deferred(PLAN-37)` تصبح شرط نجاح، وتقرأ قائمتها من `service-wiring.json` |

**القواعد:**
1. لا يكتب ملف خاص بالقبول أي مفتاح توصيل. قيمة مختبرية مسموحة فقط للسر.
2. في k8s لا يُضاف شيء إلى `hr-app-config` المشترك إلا ما هو مشترك فعلاً. التوصيل الخاص بخدمة يُكتب في patch الخدمة.
3. أسماء المفاتيح في k8s بصيغة المتغيرات `__`، مطابقة لـ compose حرفياً، ويتحقق من ذلك الفحص §2.

### 4. فشل صريح عند نقص العنوان والسر، وتوحيد مفتاح HR.Core (G-COD-01، G-IAM-03)

**المشكلة:**
- `ClientCredentialsTokenProvider` يقرأ `configuration["ServiceIdentity:ClientSecret"] ?? Environment…`. والقيمة الفارغة `""` في `appsettings.json` تمنع الاحتياط ([ServiceIdentityClient.cs:31-35](../../src/Shared/HRManagement.ServiceDefaults/ServiceIdentityClient.cs)).
- بوابة الحجز القانوني في HR.Contracts تقرأ `Services:HRCore:BaseUrl` أو `HrCore:BaseUrl` فقط ([DependencyInjection.cs:36-45](../../src/Services/HRContracts/HR.Contracts.Infrastructure/DependencyInjection.cs)، [EmployeeLifecycleGate.cs:23-24](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Services/EmployeeLifecycleGate.cs)). ولا يضبط أيَّ المفتاحين إلا [docker-compose.dev-contracts.yml:18](../../docker-compose.dev-contracts.yml)، فتُسجَّل `PassThroughEmployeeLifecycleGate` في الإنتاج والقبول.
- `WorkflowTriggerService` يرسل إلى `/api/workflow-instances` بلا `GatewayCallerProof.Sign` ([WorkflowTriggerService.cs:50-56, 108-113](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)). والمسار غير مستثنى من الإثبات ([GatewayCallerProofMiddleware.cs:42-50](../../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs))، بخلاف `HrCoreLookupClient` الذي يوقّع ([HrCoreLookupClient.cs:50](../../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreLookupClient.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `HrCoreEndpoint` | `src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreEndpoint.cs` | `RequireBaseUrl(IConfiguration, IHostEnvironment?)`: يقرأ `Integration:HrCoreBaseUrl`، ثم المفتاحين القديمين بتحذير (D-INT-3). يرمي `InvalidOperationException` برسالة تسمّي المفتاح عند الغياب خارج Development، ويرفض `localhost` داخل الحاوية (إعادة استعمال `ValidateInternalBaseUrlForContainer`) |
| استبدال القراءات المتفرقة | `CodeGenerationClient.cs:79-85`، `PolicyEnforceClient.cs:185-187`، `HrCoreLookupClient.cs:66-67, 80-84`، `HrCoreActiveRegimeClient.cs:44-45, 58-62`، `HrCoreServiceRegimeClient.cs:44-45, 58`، `DsarDeletionOrchestrator.cs:66, 115`، `Admin.Logistics DependencyInjection.cs:25`، `WorkflowTriggerService.cs:27, 53, 84, 111`، HR.Contracts `DependencyInjection.cs:36` و`EmployeeLifecycleGate.cs:23` | كلها تستدعي `HrCoreEndpoint`. ويُحذف `?? "http://localhost:5010"` من الشيفرة الإنتاجية، ولا يبقى فرع `AddHttpClient(name)` بلا عنوان |
| بوابة الحجز | HR.Contracts `DependencyInjection.cs` | تُسجَّل `HttpEmployeeLifecycleGate` حين يوجد المفتاح المعتمد. **المصادقة والفشل المغلق** ← PLAN-42. هذه الخطة تغيّر المفتاح والتوصيل فقط |
| السر الفارغ | `ServiceIdentityClient.cs:33-35` | معاملة `string.IsNullOrWhiteSpace` كغياب، ثم الرجوع إلى متغير البيئة، ثم الخطأ. (رفض القيم المؤقتة مثل `USE_ENV_OR_SECRET_MANAGER` ← PLAN-42) |
| توقيع الإثبات | `WorkflowTriggerService.cs` (الموضعان) | `GatewayCallerProof.Sign(request, configuration, "/api/workflow-instances")` قبل الإرسال، مثل `HrCoreLookupClient`. (حلقة اعتماد العهدة كلها ← PLAN-45/46) |

**القواعد:**
1. لا عميل S2S يُسجَّل بلا عنوان في أي بيئة غير Development. الخدمة لا تقلع ورسالتها تسمّي المفتاح.
2. المفتاح القديم يعمل مع تحذير واحد عند الإقلاع، ولا يعمل إن تعارض مع المفتاح المعتمد (خطأ).
3. لا تغيير في سلوك الفشل المفتوح للبوابة أو عميل الإنفاذ هنا. ذلك من نطاق PLAN-42.

### 5. توليد الأرقام في المستهلكات: لا إسقاط صامت (G-COD-06)

**المشكلة:** `ArchiveReferenceFactory` يعيد `null` ([ArchiveReferenceFactory.cs:17-21](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ArchiveReferenceFactory.cs)). وسبعة مواضع تفعل `if (reference is null) return;`، فتؤكد الرسالة بلا أرشفة:
- [LeaveAndAdvanceApprovedConsumer.cs:21, 52](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs)؛
- [SeparationCompletedConsumer.cs:27](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/SeparationCompletedConsumer.cs)؛
- [MedicalArchiveConsumers.cs:26, 55](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/MedicalArchiveConsumers.cs)؛
- [HireCaseCompletedConsumer.cs:24-28](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/HireCaseCompletedConsumer.cs)؛
- [ComplaintResolvedArchiveConsumer.cs:22](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ComplaintResolvedArchiveConsumer.cs).

والعميل يحوّل كل فشل إلى `null` ([CodeGenerationClient.cs:40-56](../../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `CodeGenerationUnavailableException` | `ServiceDefaults/CodeGeneration/` | استثناء عابر يرث `Exception` مباشرةً (لا `InvalidOperationException` ولا `ArgumentException`، لأنهما في قائمة التجاهل في [MassTransitExtensions.cs:151-154](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs))، فتعيد MassTransit المحاولة ثم تنقل الرسالة إلى `_error` |
| `ICodeGenerationClient.GenerateRequiredAsync` | `CodeGenerationClient.cs` | يرمي الاستثناء عند الفشل بدل `null`، ويسجّل رمز HTTP. ويبقى `GenerateOrErrorAsync` للواجهات التفاعلية |
| `ArchiveReferenceFactory.CreateReferenceAsync` | الملف القائم | يستدعي `GenerateRequiredAsync`. وتُحذف أسطر `if (reference is null) return;` السبعة |
| مقياس | `OutboxMetrics` أو `Meter` في Records.Archive | عدّاد `archive_reference_failures_total` بوسم نوع الحدث |

**القواعد:**
1. المستهلك لا يؤكد رسالة لم ينشئ لها `ArchiveItem`، إلا في حالة التكرار الحالية (`ExistsByTitleAsync`).
2. إعادة التسليم بعد عودة HR.Core تنشئ صفاً واحداً، لأن فحص العنوان الحالي هو ضمان التكرار.

### 6. شحن `service-regimes.json` مع كل خدمة تقرؤه (G-POL-01، G-MOV-02، جزء G-SEP-02)

**المشكلة:**
- `HR.Core.Infrastructure.csproj:16` هو المشروع الوحيد الذي ينسخ الحزمة. والصورة تنسخ `/app/publish` فقط ([DotnetService.Dockerfile:51](../../docker/DotnetService.Dockerfile)).
- القواعد تعود قواميس فارغة عند الغياب ([ServiceRegimeSeparationRules.cs:91-95](../../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)). فيُرفض كل فصل ([SeparationRecordHandlers.cs:166](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)) وكل نقل ([TransferRequestsController.cs:94](../../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)) وكل ترفيع ([ServiceRegimePromotionRules.cs:69-70](../../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs)).
- البحث عن الحزمة مكرر في خمسة مواضع:
  - [ServiceRegimeJsonPackPaths.cs:6-31](../../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeJsonPackPaths.cs)؛
  - [ServiceRegimeMovementRules.cs:124-130](../../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeMovementRules.cs)؛
  - [ServiceRegimeTransitionRules.cs:80-86](../../src/Services/HRCore/HR.Core.Domain/Services/ServiceRegimeTransitionRules.cs)؛
  - [ServiceRegimeSeeder.cs:144-155](../../src/Services/HRCore/HR.Core.Infrastructure/Data/ServiceRegimeSeeder.cs)؛
  - وأي مسار آخر يكشفه الجرد.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `Content` مشترك | `src/Shared/HRManagement.SharedKernel/HRManagement.SharedKernel.csproj` | `<Content Include="..\..\..\docs\syr\service-regimes.json" Link="seed-packs\syr003-service-regimes-v1\regimes.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />`. ويُحذف السطر المقابل من `HR.Core.Infrastructure.csproj:16` لتجنب تعارض الوجهة |
| `ServiceRegimePack` | `src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePack.cs` | مصدر واحد: (1) `LegalPacks:ServiceRegimesPath` إن ضُبط، ويجب أن يوجد وإلا خطأ؛ (2) `AppContext.BaseDirectory/seed-packs/syr003-service-regimes-v1/regimes.json`؛ (3) في `Development`/الاختبار فقط: الصعود إلى `docs/syr/service-regimes.json`. يعيد `Bytes`، و`Sha256`، و`Source`، و`Counts` (أنظمة، قواعد فصل، أنواع حركة، قواعد ترفيع، تعارضات المادة 159) |
| إعادة توجيه القرّاء | `ServiceRegimeSeparationRules`، `ServiceRegimeMovementRules`، `ServiceRegimePromotionRules`، `ServiceRegimeArticle159Rules`، `ServiceRegimeTransitionRules`، `ServiceRegimeSeeder` | كلها تقرأ عبر `ServiceRegimePack`. ويُحذف `ServiceRegimeJsonPackPaths` والقوائم المكررة |
| فحص جاهزية | `src/Shared/HRManagement.ServiceDefaults/ServiceRegimePackHealthCheck.cs` + `AddServiceRegimePackCheck()` | وسم `ready`. يعيد `Unhealthy` إن غابت الحزمة أو كانت أي عدّادات مطلوبة صفراً. يُسجَّل في HR.Core وHR.Contracts وHR.Movements |
| سجل الإقلاع | `IHostedService` صغير في الخدمات الثلاث | سطر واحد: `LegalPack loaded pack=syr003-service-regimes-v1 source=<Path\|Content> sha256=<hex> regimes=N separationRules=N movementTypes=N promotionRules=N` |
| مصفوفة | `service-wiring.json` | حقل `legalPacks: ["syr003-service-regimes-v1"]` للخدمات الثلاث. والفحص §2 يتحقق من أن كل مشروع يستدعي `ServiceRegime*Rules` يسجّل الفحص |

**القواعد:**
1. لا قاموس فارغ صامت في Production. الحزمة الغائبة أو الفارغة تُفشل الجاهزية برسالة تسمّي الحزمة.
2. المسار الخارجي المضبوط والمفقود خطأ إقلاع، ولا يُستبدل بالمضمَّن بصمت.
3. بصمة SHA-256 للملف في المستودع هي المرجع. والاختبار يقارن بها.

### 7. الملف الشبيه بالإنتاج يفشل حيث يفشل الإنتاج

**المشكلة:** الملف الشبيه يُطبَّق فوق ملف القبول فيرث توصيله ([docker-compose.acceptance-prodlike.yml:5-8](../../docker-compose.acceptance-prodlike.yml))، ولذلك نجحت رحلة AC-HCM-33 «الشبيهة بالإنتاج» بينما الإنتاج مكسور.

**التصميم:**
- بعد §3 لا يحوي ملف القبول أي توصيل. والفحص §2 (القاعدة 4) يمنع عودته.
- `scripts/acceptance-up.ps1 -ProdLike` يبني المكدّس بالملفات الستة ويشغّل كل الخدمات.
- رحلة `RecruitmentToHireJourneyProdLikeE2ETests` (PLAN-33) تُعاد على المكدّس الجديد دليلاً على أن التوصيل المشترك يكفي.

## Out of scope

| البند | الخطة |
|-------|-------|
| سر لكل خدمة في k8s (ExternalSecret مستقل لكل Deployment)، ورفض القيم المؤقتة عند الإقلاع، وإثبات بوابة غير قابل للتزوير، وجعل عميل الإنفاذ وبوابة الحجز القانوني مصادَقتين ومغلقتين عند الفشل، وتجاهل `TenantId` من جسم طلب الترميز (G-COD-05) | PLAN-42 |
| قالب realm إنتاجي يُستورد عند الإقلاع الأول ويضم كل العملاء والنطاقات من `service-wiring.json` | PLAN-39 |
| `.env.example` وأسرار الإقلاع الأول والوسيط | PLAN-38 |
| TLS والتعريض وحدود الذاكرة على الخادم الواحد | PLAN-36 |
| بادئات الأرقام والسياسات الوطنية للجهات وتصادمات السياسات | PLAN-48 |
| حلقة اعتماد العهدة والفقدان كاملة | PLAN-45/46 |
| كتالوج أنواع الفصل لكل نظام (الجزء القانوني من G-SEP-02) | PLAN-61 |
| وصول المعتمد إلى الحركات (G-MOV-01) | PLAN-50 |
| إزالة تسجيلات عميل الإنفاذ غير المستخدمة | PLAN-49 |
| ربط الإصدار بالكيان وإبطال الأرقام (G-COD-04) | الموجة 2 (COD-C) |
| تشغيل التطبيقات بأدوار `*_app` وRLS مفروض | الموجة 2 (OPS-D) |

## Acceptance scenarios

على الملف الشبيه بالإنتاج (`scripts/acceptance-up.ps1 -ProdLike`)، تحت `ACCEPTANCE_REQUIRE_ALL=true`. الفئة `AcInt37ProductionWiringTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-INT-37")`). الشخصيات: `a-contracts-maker`، `a-contracts-checker`، `central-a`، `a-idcard`، `a-it`، `a-compliance`، `a-logistics-maker`، `a-movements`، `a-management-approver`، `employee-a`.

> ملاحظة: في جهة غير افتراضية تبقى الأرقام بلا بادئة حتى PLAN-48 (G-COD-02). لذلك تفحص هذه الخطة **وجود** الرقم وصف `CodeIssuances` المقابل، لا صيغته.

| ID | Positive | Negative |
|----|----------|----------|
| AC-INT-37a | `a-contracts-maker` ينشئ طلب إجازة وسلفة، وسجل فصل من `SeparationWizard/Step1`. كل `ServiceTransaction` يحمل `ReferenceNumber` غير فارغ، وفي `hr_core_db.CodeIssuances` صف `ServiceTransaction` لكل منها | — |
| AC-INT-37b | رحلة التوظيف حتى التعيين (`RecruitmentToHireJourneyProdLikeE2ETests`) تنجح بلا توصيل من ملف القبول. ومعاملة `Appointment` برقم، و`IssueAppointmentDecision` يصدر رقم قرار | — |
| AC-INT-37c | `a-idcard` يصدر بطاقة (رقم تسلسلي)، و`a-it` يسجّل جهازاً (وسم)، و`a-compliance` يفتح قضية (رقم)، و`a-logistics-maker` ينشئ أصلاً (وسم). كلها بأرقام ومعها صفوف إصدار | — |
| AC-INT-37d | اعتماد إجازة ← `ArchiveItem` بمرجع خلال 60 ثانية | إيقاف `hr-core-web` ثم اعتماد إجازة ← لا `ArchiveItem` والرسالة في `records-archive` قيد الإعادة أو في `_error` (لا إسقاط). بعد التشغيل وإعادة تشغيل الرسالة (`ErrorQueueReplay`) ← صف واحد بالضبط |
| AC-INT-37e | سجلات حاويات `hr-core-web` و`hr-contracts-web` و`hr-movements-web` تحوي `LegalPack loaded … sha256=<بصمة الملف في المستودع>`. فصل `Resignation` لموظف `law50` يُنشر، ونقل يُنشأ، وترفيع يُنشأ | تشغيل `hr-movements-web` بـ `LegalPacks__ServiceRegimesPath=/nonexistent.json` ← لا يقلع أو الجاهزية `Unhealthy` برسالة تسمّي الحزمة |
| AC-INT-37f | — | تشغيل `hr-contracts-web` بـ `Integration__HrCoreBaseUrl=` (فارغ) ← يخرج عند الإقلاع برسالة تسمّي المفتاح. ثم بـ `http://localhost:5010` ← يخرج برسالة «Docker service DNS» |
| AC-INT-37g | توكن `svc-hr-contracts` (بالنطاقات المصفوفية) ← `POST /api/internal/codes/generate` لنوع `ServiceTransaction` بترويسة `X-Tenant-ID` ← 200 (الصيغة تبقى صالحة بعد قيود PLAN-42 على المالك والمستأجر) | توكن `svc-hr-health` (بلا `hr-core.workflow`) ← 403، وعدد `CodeIssuances` قبل وبعد متساوٍ (يغطي AC-IAM-Cb وجزء AC-30) |
| AC-INT-37h | `scripts/int037-wiring-check.ps1` يمر على compose الإنتاج والشبيه وعلى طبقات k8s | `ProductionWiringMatrixTests` بنسخة compose ينقصها مفتاح ← فشل برسالة تسمّي الخدمة والمفتاح |

## Verification

```powershell
dotnet restore HRManagement.slnx --force-evaluate   # YamlDotNet الجديدة
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "ProductionWiringMatrix|HrCoreEndpoint|ServiceRegimePack|Int037|Architecture|CodeGeneration"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Int037|ArchiveReference"
pwsh scripts/int037-wiring-check.ps1        # compose الإنتاج + الشبيه + k8s (يتخطى k8s إن غاب kubectl مع تحذير)
# صور: الحزمة داخل كل صورة تقرؤها (بعد البناء)
$f = @('-f','docker-compose.yml','-f','docker-compose.services.yml','-f','docker-compose.dev-profiles.yml',
       '-f','docker-compose.full-stack-gateway.yml','-f','docker-compose.acceptance.yml','-f','docker-compose.acceptance-prodlike.yml')
foreach ($svc in 'hr-core-web','hr-contracts-web','hr-movements-web') {
  docker compose -p hrm-acceptance @f --profile full-stack run --rm --no-deps --entrypoint ls $svc /app/seed-packs/syr003-service-regimes-v1
}
# قبول على الملف الشبيه بالإنتاج
pwsh scripts/acceptance-up.ps1 -ProdLike
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcInt37|FullyQualifiedName~RecruitmentToHireJourneyProdLike"
```

**اختبارات جديدة:**
- **Unit:**
  - `ProductionWiringMatrixTests`: الفحوص الستة في §2.
  - `HrCoreEndpointTests`: غياب، ومفتاح قديم مع تحذير، وتعارض، و`localhost` في حاوية (بمحاكاة `DOTNET_RUNNING_IN_CONTAINER`).
  - `ServiceIdentitySecretTests`: سر فارغ في JSON مع متغير بيئة.
  - `ServiceRegimePackTests`: المصادر الثلاثة، والبصمة، والعدّادات، والمسار المضبوط المفقود.
  - `Int037ArchiveConsumerTests`: `CodeGenerationUnavailableException` يُرمى ولا يُنشأ صف.
  - `Int037LifecycleGateRegistrationTests`: `Integration:HrCoreBaseUrl` وحده يسجّل `HttpEmployeeLifecycleGate`.
- **Integration:** `Int037ArchiveRetryIntegrationTests` (MassTransit test harness): فشل الترميز يعيد المحاولة ثم يذهب إلى `_error`، والنجاح بعدها ينشئ صفاً واحداً.
- **Publish:** `Int037PublishOutputTests`: `dotnet publish` لـ HR.Contracts وHR.Movements إلى مجلد مؤقت، ثم فحص وجود `seed-packs/syr003-service-regimes-v1/regimes.json` وتطابق البصمة.

## Definition of done

- `deploy/wiring/service-wiring.json` موجود، و`ProductionWiringMatrixTests` أخضر، و`int037-wiring-check.ps1` يمر على سلسلتي الإنتاج والشبيه وk8s، و`smoke.ps1 -RequireIntegrationWiring` إلزامي وأخضر على الخادم الواحد.
- لا تقرأ الشيفرة الإنتاجية `localhost:5010` افتراضياً، ولا يُسجَّل عميل S2S بلا عنوان خارج Development.
- لا يحوي `docker-compose.acceptance.yml` أي مفتاح توصيل.
- مستهلكات الأرشيف السبعة لا تؤكد رسالة بلا أرشفة.
- الحزمة في صور الخدمات الثلاث، وبصمتها في السجل، وفحص الجاهزية يرفض غيابها.
- AC-INT-37a..h = Pass، والأدلة في `acceptance-evidence/AC-INT-37/` بالعناصر الثمانية.
- سطور «الحالة» محدَّثة في الدراسات: G-COD-01 وG-COD-06 (COD)، وG-IAM-03 (IAM)، وG-POL-01 (POL)، وG-MOV-02 (MOV)، وجزء الشحن من G-SEP-02 (SEP).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md` |
| Matrix | `deploy/wiring/service-wiring.json`، `docs/SERVICE_WIRING_MATRIX.md` |
| Compose | `docker-compose.yml`، `docker-compose.services.yml`، `docker-compose.production.yml`، `docker-compose.acceptance.yml`، `docker-compose.acceptance-prodlike.yml` |
| k8s | `deploy/k8s/base/<svc>/wiring-patch.yaml` (14)، `deploy/k8s/base/kustomization.yaml`، `deploy/k8s/overlays/*/external-secrets/hr-app-secrets.yaml` |
| Keycloak | `scripts/sync-keycloak-dev-realm.ps1` (يقرأ المصفوفة) |
| Code | `HrCoreEndpoint.cs`، `ServiceIdentityClient.cs`، `CodeGenerationClient.cs`، `CodeGenerationUnavailableException.cs`، `ArchiveReferenceFactory.cs` + 6 مستهلكات، `WorkflowTriggerService.cs`، `ServiceAuthorizationExtensions.cs`، HR.Contracts `DependencyInjection.cs`، `ServiceRegimePack.cs`، `ServiceRegimePackHealthCheck.cs`، `HRManagement.SharedKernel.csproj`، `HR.Core.Infrastructure.csproj`، `HR.Core.Web/Program.cs` |
| Scripts | `scripts/int037-wiring-check.ps1`، `scripts/int037-render-wiring-doc.ps1`، `scripts/acceptance-up.ps1` (`-ProdLike`)، `scripts/vps/smoke.ps1` (إلزام فحوص التوصيل) |
| Tests | الوحدة والتكامل أعلاه، و`AcInt37ProductionWiringTests.cs` |
| Docs | `SERVICE_TRUST_INVENTORY.md`، `COD_001_CODE_GENERATION.md` («إضافة مستهلك» تشير إلى المصفوفة)، `SYR_003_SERVICE_REGIMES.md` (الشحن والبصمة)، `KEYCLOAK_AND_SECRETS.md` (النطاقات من المصفوفة)، وسطور «الحالة» في الدراسات الخمس |

## Risks

| الخطر | التخفيف |
|-------|---------|
| فشل الإقلاع الجديد يوقف خدمات كانت «تعمل» بنصف وظيفة | المرحلة 0.4 تجرد المتغيرات الفعلية قبل التغيير. وكل مفتاح مطلوب يُضاف إلى compose وk8s في الـ commit نفسه الذي يجعله إلزامياً |
| `YamlDotNet` لا يطابق دلالات compose (المراسي، `!override`) | الفحص المُصيَّر `docker compose config --format json` في السكربت هو الحكم النهائي، واختبار الوحدة للسرعة |
| انتقال `Content` عبر مراجع المشاريع لا يصل إلى مخرجات النشر | `Int037PublishOutputTests` على `dotnet publish` الحقيقي، والتحقق داخل الصور في Verification |
| نقل التوصيل إلى الملفات المشتركة يغيّر سلوك مكدّس القبول | القيم مطابقة لما كان القبول يضبطه. وتُعاد AC-HCM-21..33 على الملف الشبيه (يغطيها PLAN-65) |
| ترتيب Keycloak: النطاقات الجديدة تحتاج إعادة مزامنة realm القبول | `acceptance-up.ps1` يستدعي `sync-keycloak-dev-realm.ps1` الذي يقرأ المصفوفة. ويُوثَّق الأمر في `KEYCLOAK_AND_SECRETS.md` |

## Effort estimate

9–10 أيام عمل:
- المصفوفة والفحص الآلي: 2.5.
- compose وk8s وKeycloak: 1.5.
- `HrCoreEndpoint` والسر والإثبات وبوابة الحجز: 1.
- مستهلكات الأرشيف: 1.
- شحن الحزمة والفحص والبصمة: 1.5.
- الملف الشبيه والقبول والأدلة: 1.5.
- الوثائق: 0.5.

## ملاحظات التنفيذ والانحرافات

نُفّذت الخطة في جلسة واحدة (المراحل P0–P9 من `docs/acceptance-plans/execution/PLAN-37-AGENT-PROMPT.md`)؛ التفاصيل والأزمنة في `reports/plan37-final-report.md`.

- **§1 القاعدة 3:** لم يُضَف `svc-auth-directory` إلى `Service.HrCoreLookup`. استدعاؤه الوحيد (`/api/lookup/employees/by-ids`) تحت سياسة `HrManagementPolicies.HrCoreLookup` التي تكتفي بالنطاق، فلا استدعاء يبرر إضافته إلى القائمة. ومداخل القوائم الموجودة بلا استدعاء (`hr-gateway-client`، `svc-admin-logistics`، `svc-compliance` في `Service.HrCoreLookup`، و`svc-hr-core`، `svc-auth-directory` في `Service.DirectorySync`) بقيت ودُوّنت في `allowListExceptions`، وإزالتها ← PLAN-42.
- **المصفوفة:** فيها فهرس `endpoints` (الهدف والمسار والمتحكم والسياسات والنطاق) تشير إليه `calls[]`، وفهرس `scopes` بالجماهير الذي تقرؤه سكربتات Keycloak. والتسجيلات المضمّنة صارت دوال مسمّاة (`AddHrCoreWorkflowClient`، `AddDirectorySyncClient`، `AddEmployeeLifecycleGate`).
- **§2:** `MergingParser` في YamlDotNet يرفض مفتاحاً صريحاً يتجاوز مرساة مدموجة (يعدّه مكرراً)، فيحلّ `ComposeEffectiveEnvironment` المفتاح `<<` بنفسه بدلالات YAML. وتعريفات سلاسل compose انتقلت إلى `scripts/vps/compose-chains.ps1` يقرؤها `lint-compose.ps1` و`int037-wiring-check.ps1` والاختبارات.
- **§3:** `staging-ha` لم يكن فيه أي مفتاح `SVC_*` فأُضيفت كلها، و`dr-secondary` يستعمل السر المؤقت في `base` فأُضيفت إليه قيم `replace-me`. وعناوين البوابة انتقلت من `hr-yarp-config` إلى `hr-gateway/wiring-patch.yaml`. و`ServiceIdentity__Scope` يُكتب صراحةً (فارغاً لمن لا نطاق له).
- **§4:** القاعدة نفسها (`ServiceEndpoint.Require`) طُبّقت أيضاً على البوابة (عنوانان بقيمة `localhost` افتراضية)، وعلى مزامنة دليل IT.Infra (كانت تنجح دون نداء)، وعلى جامع DSAR في Compliance. و`PayrollGradeLookup`/`CandidateDocumentUpload` أبقيا تنفيذهما «غير المتاح» الصريح. ووُجد في الطريق تحويل متصفح في HR.Core إلى `http://localhost:5000` في الإنتاج فأصبح مساراً نسبياً.
- **§6:** الملفات `docs/syr/*.json` صارت LF في كل نسخة عمل (`.gitattributes`): على Windows كانت البصمة المحلية تختلف عن البصمة على الخادم بسبب CRLF. والمرجع هو الكائن الملتزم.
- **القبول:** 37g السالب يجيب 401 (لا جمهور `api-hr-core` في توكن بلا نطاق) لا 403. و37c يصدر البطاقة عبر `Renew` لأن `IssuedCards/Create` لا يجتاز التحقق من النموذج أبداً (عيب سابق). و37d ينشر `ILeaveApproved` كما ينشره HR.Contracts لأن اعتماد الإجازة من الواجهة يفشل على PostgreSQL بـ `DbUpdateConcurrencyException` (عيب سابق). كلاهما في عيوب التقرير.
- **الخادم الواحد:** realm الخادم لم يُحذف؛ `deploy/vps/keycloak-sync-clients.sh` يزامنه مع المصفوفة بعد كل نشر (أسرار العملاء من بيئة حاوية Keycloak). وفشل أول نشر عند تعيين سر عميل (`kcadm update -f --merge`) فرُجع عنه، ثم أُصلح (`-s secret=`) وأُعيد النشر. والاختبار الذاتي بفحوص التوصيل الإلزامية أخضر (49/0).
