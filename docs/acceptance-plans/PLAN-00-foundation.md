# PLAN-00 — الأساس: بيئة حية + بذور متعددة الجهات + شخصيات + إطار أدلة

**التغطية:** بوابة إلزامية لكل الخطط. **الطبقة:** 0. **يعتمد على:** — .

## الهدف

توفير حزمة Docker حية صحّية، وبيانات واقعية متعددة الجهات (مركز + فروع)، ومستخدمي Keycloak للأدوار المطلوبة، ومشروع اختبارات قبول مع مُسجِّل أدلة موحّد. بدون هذه الخطة لا يمكن تشغيل أي سيناريو قبول حي.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: حزمة Docker كاملة عبر [docker-compose.services.yml](../../docker-compose.services.yml) + `--profile full-stack`، و[scripts/verify-staging-stack.ps1](../../scripts/verify-staging-stack.ps1)، و[scripts/run-staging-e2e.ps1](../../scripts/run-staging-e2e.ps1)، وأنماط Playwright في [src/Tests/HRManagement.E2E/E2eHarness.cs](../../src/Tests/HRManagement.E2E/E2eHarness.cs) و`PlaywrightFixture`.
- منفّذ جزئياً: بذور جهة واحدة `PLATFORM` عبر [DemoDataSeeder.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Data/DemoDataSeeder.cs) و`NationalSeedOrchestrator` (شجرة أقسام لا مركز/فروع).
- فجوة: لا بذرة متعددة الجهات؛ تضارب معرفات (`testdata/e2e` يستخدم `1111…/3333…` مقابل الافتراضي `…0001`)؛ لا mapper لـ`scope_org_unit_id`؛ لا مستخدمي `platform-admin`/`branch-manager`/`auditor` مبذورين؛ لا مشروع قبول ولا مُسجِّل أدلة.

## تغييرات التنفيذ (مهام)

### 0.1 البيئة الحية + CI
- سكربت `scripts/acceptance-up.ps1` يغلّف أمر الإقلاع الكامل + `verify-staging-stack.ps1`، ويطبع حالة `/health/upstreams/*` لكل الخدمات.
- توحيد معرفات `Tenant-A`/`Tenant-B` عبر [testdata/e2e/manifest.json](../../testdata/e2e/manifest.json) والبذور وrealm القبول (معرفات ثابتة موثّقة).

### 0.2 بذرة القبول متعددة الجهات (`AcceptanceDataSeeder`)
- بذرة قابلة لإعادة التشغيل (idempotent) تُشغَّل في بيئة `Acceptance`/`Development` فقط، تُنشئ:
  - **Tenant-A** (AGENCY-A): مركز (جذر) ← فرع1 ← قسم تابع لفرع1؛ فرع2 شقيق تحت المركز. مع `MaterializedPath` صحيح.
  - **Tenant-B** (AGENCY-B): شجرة صغيرة + موظف واحد (لسيناريوهات العابر السالبة).
  - موظفون: واحد في فرع1، واحد في القسم، واحد في فرع2، وواحد في Tenant-B.
  - قالب سياسة بإصدارين (لـPLAN-02)، ودورة رواتب تجريبية على لقطة مدخلات ثابتة (لـPLAN-06/10).
- البناء فوق أنماط [DemoDataSeeder.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Data/DemoDataSeeder.cs)، [MdmReferenceSeeder.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Data/MdmReferenceSeeder.cs). (يُفضَّل تمرير الإنشاء عبر مسار `AgencyManifest` بعد توسعته في PLAN-08 ليخدم AC-07 نفسه.)

### 0.3 شخصيات Keycloak
- إضافة protocol mapper لـ`scope_org_unit_id` (موجود لـ`tenant_id` فقط) في [docker/keycloak/realm-hrmanagement-dev.template.json](../../docker/keycloak/realm-hrmanagement-dev.template.json)، وإنشاء `docker/keycloak/realm-acceptance.json`.
- إضافة دور `auditor` (مفقود) وبذر مستخدمين بسمات `tenant_id`+`scope_org_unit_id`:
  - مركزي (Tenant-A، بلا تقييد نطاق)، مدير فرع1 (scope=فرع1)، مدير فرع2 (scope=فرع2)، موظف، `platform-admin`، مدقق، ومستخدم Tenant-B.
- التحقق من الخريطة في [KeycloakRoleMapping.cs](../../src/Shared/HRManagement.ServiceDefaults/KeycloakRoleMapping.cs).

### 0.4 مشروع القبول + مُسجِّل الأدلة
- مشروع جديد `src/Tests/HRManagement.AcceptanceTests` (xUnit + Playwright/APIRequest) يعيد استخدام `E2eHarness`/`PlaywrightFixture`، مع فئة دخول لكل شخصية تُرجع جلسة/توكن.
- `EvidenceRecorder` ينفّذ عقد الأدلة (انظر [README](README.md)) ويكتب `acceptance-evidence/AC-XX/` و`summary.md`.
- علم `RUN_ACCEPTANCE=true`؛ ربط بوظيفة CI بعد رفع الحزمة.
- إعادة استخدام fixtures البنية الحقيقية من [src/Tests/HRManagement.IntegrationTests/TestFixtures.cs](../../src/Tests/HRManagement.IntegrationTests/TestFixtures.cs) (`RealInfra`, `TenantTestFixtures`) للفحوص التي تحتاج DB مباشرة.

## معايير القبول (بوابة الأساس)

- الحزمة صحّية: كل `/health/upstreams/{service}` = 200.
- البذرة تُنتج الجهتين والشجرة والموظفين والمستخدمين، وإعادة تشغيلها لا تُنشئ ازدواجاً.
- تسجيل الدخول بكل شخصية ينجح ويُصدر JWT يحمل `tenant_id` (وللمديرين `scope_org_unit_id`).
- تشغيل تجريبي لـ`AC-01` عبر الإطار يُنتج مجلد أدلة كامل وفق العقد.

## مخرجات الأدلة

`acceptance-evidence/_foundation/`: لقطة صحة الحزمة، تقرير البذرة (عدد الجهات/الوحدات/الموظفين)، قائمة مستخدمي Keycloak وسماتهم (مقنّعة)، ومثال أدلة `AC-01`.

## تعريف الإنجاز

بوابة الأساس مستوفاة أعلاه، والإطار جاهز لاستقبال بقية الخطط، ووظيفة CI تُشغّل مجموعة القبول على الحزمة الكاملة.
