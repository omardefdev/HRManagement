# دراسة محرك الترميز والهيكل التنظيمي المؤرخ والبيانات المرجعية (COD) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** ثلاثة مسارات مترابطة في HR.Core ومستهلكيها:
- محرك توليد الأرقام COD-001: `CodeGeneratorService`، `ICodeGenerator`، `ICodeGenerationClient`، `CodeGenerationRequests`، `CodeGenerationSeeder`، `CodePoliciesController`، `CodeGenerationController`، وكل خدمة تحصل منه على رقم.
- الهيكل التنظيمي المؤرخ ORG-001: `OrgUnit`، `OrgUnitVersion`، `OrgUnitsController`، `OrgStructureApiController`، استعلام as-of، ونفاذ الملاك الزمني (PLAN-16).
- البيانات المرجعية MDM-001/002: `ReferenceCatalogEntry`، `ReferenceDataController`، `ReferenceCatalogController`، `MasterDataDuplicatesController`، دليل الوظائف، ومستهلكو MDM (PLAN-18/19/20).

**المنهج:** قراءة ثابتة للشيفرة والاختبارات والوثائق والأدلة عند `f9eab1a` (2026-10-29)، ومقارنتها بما تدّعيه الوثائق. وتتبّع كل مسار بإعدادات Production (`docker-compose.production.yml` وk8s) لا بإعدادات المختبر.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK في النسخة المدروسة). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [COD_001_CODE_GENERATION.md](COD_001_CODE_GENERATION.md) · [ORG_001_DATED_STRUCTURE.md](ORG_001_DATED_STRUCTURE.md) · [ORG_UNIT_HIERARCHY.md](ORG_UNIT_HIERARCHY.md) · [EST_001_ESTABLISHMENT.md](EST_001_ESTABLISHMENT.md) · [MDM_001_REFERENCE_DATA.md](MDM_001_REFERENCE_DATA.md) · [MIG_003_COD_CODE_LINEAGE.md](MIG_003_COD_CODE_LINEAGE.md) · [PLAN-04](acceptance-plans/PLAN-04-codes-temporal-org.md) · [PLAN-16](acceptance-plans/PLAN-16-establishment-temporal.md) · [PLAN-17](acceptance-plans/PLAN-17-establishment-planning.md) · [PLAN-18](acceptance-plans/PLAN-18-establishment-mdm-consumer.md) · [PLAN-19](acceptance-plans/PLAN-19-establishment-job-catalog-import.md) · [PLAN-20](acceptance-plans/PLAN-20-establishment-mdm-admin-ui.md) · [completion-backlog/09](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md) (MDM-001/MDM-002/ORG-001 = «Done»)

---

## 1. الخلاصة

1. **نواة العدّاد سليمة، لكن ما حولها لا يعمل كما يُظن.** على PostgreSQL يُحجز التسلسل بأمر `INSERT … ON CONFLICT DO UPDATE … RETURNING` ذرّي مع فهرس فريد، فلا تكرار تحت الحمل. ويكتب `OrgUnitsController` الآن نسخاً مؤرخة عند الإنشاء والتسمية والنقل، وتوجد واجهة as-of. لكن عبارات «Done» في `completion-backlog/09` لـ ORG-001 وMDM-001 وMDM-002 لا تطابق الواقع، وAC-13 وAC-14 لم يُشغَّلا قط.
2. **ثلاثة عوائق P0:**
   - **(أ) G-COD-01:** في مكدّس الإنتاج لا تستطيع HR.Contracts وIDCard وRecords.Archive وIT.Infra الوصول إلى خدمة الترميز (لا عنوان أساس)، ولا تملك هي وCompliance نطاق `hr-core.workflow` المطلوب. أي أن طلب الإجازة والانفكاك وقرار التعيين ومعاملة التعيين من التوظيف والبطاقة والقضية تفشل كلها. الإصلاح موجود في `docker-compose.acceptance.yml` فقط، لذلك نجحت الرحلة «الشبيهة بالإنتاج».
   - **(ب) G-COD-02:** السياسات الوطنية تُبذر في المستأجر الافتراضي، ومرشح المستأجر يخفيها عن كل جهة. فتأخذ كل جهة الصيغة الاحتياطية بلا بادئة. والدليل حيّ: أرقام طلبات الشاغر وطلبات المواطنين في أدلة القبول بالشكل `-2026-00031` و`-2026-00005`.
   - **(ج) G-COD-03:** في المستأجر الذي يرى السياسات، ثلاث سياسات مبذورة تولّد تصادماً ثم استثناء: مركز الكلفة بلا تسلسل (يفشل المنصب الثاني في كل وحدة)، والمنصب الممول عند كل سنة موازنة جديدة، وطلب الشاغر لموازنة السنة القادمة. وإصلاح (ب) وحده ينقل هذه الأعطال إلى كل الجهات.
3. **ثغرات سلامة وحوكمة (P1):**
   - الرقم يُصرف خارج معاملة العمل ولا يرتبط بالكيان، فتظهر فجوات بلا أثر.
   - واجهة التوليد تقبل `TenantId` من جسم الطلب.
   - مستهلكات الأرشيف تُسقط الرسائل بصمت.
   - سجل القرارات الإدارية بثلاثة أنظمة ترقيم، منها يدوي قابل للتعديل بلا تفرّد.
   - تغييرات الهيكل التنظيمي تُطبَّق فوراً بلا اعتماد ولا مرجع قرار. والتاريخ المستقبلي أو الرجعي يُفسد السجل. وواجهة `UpdateUnit` تحفظ الأب قبل فحص الدورة.
   - نسخ الهيكل في 6 خدمات بلا ترتيب ولا تاريخ نفاذ.
   - واجهة MDM البرمجية تتجاوز حوكمة الشاشة، وتحليل الأثر يرى دليل الوظائف فقط.
   - الحدث `IReferenceDataChanged` بلا ناشر، والدرجات في Payroll منفصلة عن MDM.
4. **فجوات قدرات (P2):** دمج الوحدات وتقسيمها وإغلاقها (والحذف يفشل دائماً). شاشة تاريخ وas-of للشجرة. إدارة سياسات الترميز من الواجهة، ومقطع الجهة في الرقم، وترقيم العقود والنقل والترفيع والتأديب. وطابور التكرار الذي لا يدمج شيئاً.
5. **فجوة تحقق:** AC-13 وAC-14 وAC-EST-07..09 وAC-EST-13..18 بلا أي مجلد أدلة. واختبار AC-13 يمرّر `TenantId` في الجسم ويختبر `AssetTag` وحده، وهذا النوع يملك صيغة احتياطية تشبه الوطنية، فلا يكشف شيئاً. واختبار AC-EST-14 يكتفي بأن الرمز غير فارغ، فيقبل `-2026-00001`. واختبارات الملاك تستبدل `IFundedPositionCodeService` بنسخة وهمية، فلا تصل أبداً إلى تصادم مركز الكلفة.

**التقدير الإجمالي:** نحو 54–65 يوم عمل لمطوّر واحد، منها 11–14 يوماً لعوائق P0 (الخطتان COD-A وCOD-B).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | تعريف سياسات الترميز | بذر وطني في `CodeGenerationSeeder` تحت `DefaultTenantId`، وطبقات جهة من `seed-packs/code-overlays` (OrgUnit فقط)، وشاشة `/CodePolicies` للقراءة | ⚠️ لا تحرير ولا نشر ولا تحقق من صحة السياسة (G-COD-17، G-COD-03) |
| 2 | حل السياسة لكل مستأجر | `ResolveSnapshotAsync` عبر مرشح المستأجر، ثم صيغة احتياطية صامتة | ❌ الجهات لا ترى السياسات الوطنية (G-COD-02)، واختيار AH/VIP غير حتمي (G-COD-03) |
| 3 | حجز العدّاد تحت التزامن | PostgreSQL: `INSERT … ON CONFLICT DO UPDATE RETURNING` + فهرس فريد على `(TenantId, EntityKind, ScopeKey, Year)` | ✅ ذرّي بلا حاجة لإعادة المحاولة |
| 4 | نطاق العدّاد ومحتوى الرقم | مفتاح النطاق من `scopeFields` + السنة، ونص الرقم من المقاطع | ❌ حقول نطاق لا تظهر في الرقم، فيقع تصادم (G-COD-03) |
| 5 | سجل الإصدار والتدقيق | `CodeIssuance` بفهرس فريد `(TenantId, EntityKind, Code)`، بلا ربط بكيان | ⚠️ فجوات بلا أثر ولا إبطال (G-COD-04) |
| 6 | الحصول على الرقم من الخدمات الأخرى | `CodeGenerationClient` عبر HTTP S2S إلى `/api/internal/codes/generate` | ❌ لا عنوان ولا نطاق في الإنتاج (G-COD-01)، والأرشيف يُسقط الرسالة (G-COD-06) |
| 7 | أمان واجهة التوليد | `HrCoreWrite` للتوليد و`HrCoreReadOnly` للمعاينة | ⚠️ `TenantId` من الجسم، وحرق أرقام (G-COD-05) |
| 8 | تغطية الكيانات | 17 نوعاً عبر COD، والقرارات العامة يدوية، و`AUTO-timestamp` لقرار التغيير الوظيفي | ⚠️ سجل قرارات غير موحّد (G-COD-07، G-COD-17) |
| 9 | إنشاء/تسمية/نقل وحدة | يكتب `OrgUnitVersion` فوراً بـ `DateTime.UtcNow` | ⚠️ بلا اعتماد ولا قرار ولا تاريخ نفاذ (G-COD-08)، وواجهة API تُفسد الشجرة (G-COD-10) |
| 10 | دمج/تقسيم/إغلاق | قيم في `OrgOperationType` فقط | ❌ غير منفّذة، والحذف يفشل (G-COD-15) |
| 11 | as-of للهيكل والملاك | `GET api/org-structure/units/{id}/as-of`، `api/establishment/*/as-of` | ⚠️ يعمل لوحدة واحدة، والتاريخ المستقبلي/الرجعي يُفسد السجل (G-COD-09)، ولا شاشة (G-COD-16) |
| 12 | مزامنة الهيكل للخدمات | `IOrgUnitChanged` عبر Outbox إلى 6 مستهلكين | ⚠️ بلا ترتيب ولا تاريخ نفاذ (G-COD-11) |
| 13 | شاشة المرجع الوطني | `ReferenceCatalogController` + `ReferenceCatalogAdminService`، فحص مركزي ومنع تعطيل المستخدَم | ✅ مع حدود الأثر (G-COD-13) |
| 14 | واجهة MDM البرمجية | `ReferenceDataController` (CRUD) | ❌ تتجاوز قواعد الشاشة (G-COD-12) |
| 15 | مزامنة MDM عبر الخدمات | `IReferenceDataChanged` + مستهلك يسجّل فقط | ❌ لا ناشر ولا مستهلك خارج HR.Core (G-COD-14) |
| 16 | التكرار والدمج | `MasterDataDuplicatesController` (API للمدير) + دمج في `EmployeeLifecycleController` | ⚠️ لا كشف ولا شاشة، و`PersonMergeService` غير مستخدم (G-COD-18) |
| 17 | الأدلة | AC-13/14/EST-07..09/EST-13..18 | ❌ لم تُشغَّل (G-COD-20) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **ذرّية العدّاد على PostgreSQL:** [CodeGeneratorService.cs:334-377](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs) ينفذ upsert ذرياً واحداً، وهذا أفضل مما يصفه PLAN-04 (`Serializable`). وهناك فهرسان فريدان: على العدّاد وعلى الإصدار ([HRCoreDbContext.cs:1128, 1135](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)). ومسار `Serializable` لا يُستخدم إلا مع SQLite في الاختبارات.
- **اختبار تزامن حقيقي:** [CodeGenerationConcurrencyTests.cs:38-87](../src/Tests/HRManagement.IntegrationTests/CodeGenerationConcurrencyTests.cs) يولّد 250 رمزاً بتوازي 50 على PostgreSQL عبر Testcontainers، ويتحقق من التفرد ومن قيمة العدّاد.
- **عدم قابلية الرمز للتعديل** في واجهة الوحدات ([OrgUnitsController.cs:249](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)) ودليل الوظائف (JobCode للقراءة فقط بحسب PLAN-18).
- **نسخ الوحدات مكتوبة الآن:** `Create` و`Edit` تكتبان `OrgUnitVersion` ([OrgUnitsController.cs:190, 270, 280](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)). وتُستكمل النسخ الناقصة عند الإقلاع ([NationalSeedOrchestrator.cs:37, 72](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/NationalSeedOrchestrator.cs)). وتعيد واجهة as-of الرئيس والاسم والمناصب كما كانت، مع فحص نطاق ([OrgStructureApiController.cs:23-33](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)). فجزء من «فجوة AC-14» في PLAN-04 لم يعد قائماً.
- **منع الدورات والعمق ونقل الفرع ذرياً** في `OrgUnitPathService` (المسار المادي، حد 32 مستوى، معاملة واحدة لإعادة كتابة المسارات).
- **نسخ الملاك المؤرخة (PLAN-16)** تحمل `LegalSourceId` و`DecisionReference` و`ChangeRequestId`، والتطبيق محكوم بمسار اعتماد بعد PLAN-25.
- **شاشة MDM (PLAN-20)** تفحص الكتابة المركزية، وتمنع تعديل سجلات المنصة، وترفض تعطيل رمز مستخدم، وتتحقق من رمز الاستبدال ([ReferenceCatalogAdminService.cs:200-215](../src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceCatalogAdminService.cs)). ويتحقق `EstablishmentReferenceValidator` من الدرجة والتصنيف عند إنشاء الدليل والمنصب.
- **الاستيراد الجماعي لدليل الوظائف** داخل معاملة واحدة، فلا فجوات أرقام عند الفشل.
- **كل إجراءات POST في هذا المجال تحمل سياسة صريحة.** لا يوجد أي منها في `tests/architecture/hrcore-post-policy-allowlist.txt`.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-COD-01 — الخدمات الأخرى لا تحصل على أرقام بإعدادات الإنتاج
- **الحالة:** ✅ عولجت في [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) — كل خدمة تأخذ عنوان HR.Core وهويتها ونطاقاتها (`hr-core.workflow` لمن يولّد أرقاماً) وسرّها من `deploy/wiring/service-wiring.json` في compose الإنتاج وk8s والمكدّس الشبيه بالإنتاج، و`ProductionWiringMatrixTests` و`int037-wiring-check.ps1` يحكمان. غياب العنوان يوقف الإقلاع برسالة تسمّي المفتاح. AC-INT-37a/b/c/g ناجحة على الملف الشبيه بالإنتاج، وعلى الخادم الواحد توكن كل عميل بنطاقاته في الاختبار الذاتي الإلزامي. البادئات (G-COD-02) ← PLAN-48.
- **الدليل:**
  - العميل يقرأ `Integration:HrCoreBaseUrl` (أو `Services:HRCore:BaseUrl` أو `HrCore:BaseUrl`). إن غاب المفتاح يسجّل `HttpClient` بلا عنوان أساس، دون أي تحذير عند الإقلاع ([CodeGenerationClient.cs:79-85](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)). فيفشل الطلب، ويُلتقط الاستثناء، ويعيد العميل `null` ([:40-56](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)). والنتيجة رسالة «تعذّر توليد الرمز — خدمة الترميز غير متاحة» ([:62-71](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)).
  - مكدّس الإنتاج الموثّق في رأس [docker-compose.production.yml:1-2](../docker-compose.production.yml) (`docker-compose.yml` + `services` + `observability` + `production`) لا يضبط المفتاح لـ `hr-contracts-web` و`idcard-web` و`records-archive-web` و`it-infra-web` ([docker-compose.production.yml:279-298, 386-426, 451-470](../docker-compose.production.yml)). المفتاح موجود لـ payroll وadmin-logistics وcompliance فقط ([docker-compose.services.yml:149, 257, 361](../docker-compose.services.yml)). وفي k8s لا يحويه `hr-app-config` أصلاً ([configmap-app.yaml:7-29](../deploy/k8s/base/configmap-app.yaml))، بل يوجد في إعداد البوابة وحدها ([configmap-yarp.yaml:39](../deploy/k8s/base/configmap-yarp.yaml)).
  - مكدّس القبول أضاف المفتاح يدوياً لـ HR.Contracts فقط، مع التعليق: `Without these the CandidateAccepted consumer faulted in acceptance ("BaseAddress must be set")` ([docker-compose.acceptance.yml:99-105](../docker-compose.acceptance.yml)). والملف الشبيه بالإنتاج يُبنى فوق ملف القبول ([docker-compose.acceptance-prodlike.yml:6-7](../docker-compose.acceptance-prodlike.yml))، فيرث الإصلاح. لذلك نجحت رحلة AC-HCM-33 «الشبيهة بالإنتاج»، بينما الإنتاج نفسه لم يُصلَح.
  - نقطة التوليد محمية بـ `HrCoreWrite` ([CodeGenerationController.cs:20-21](../src/Services/HRCore/HR.Core.Web/Controllers/CodeGenerationController.cs)). وحساب الخدمة يمرّ فقط إذا حمل النطاق `hr-core.workflow` ([HrManagementAuthorizationExtensions.cs:73-77](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). أما النطاقات المضبوطة في الإنتاج فهي:
    - `file-storage.references` فقط لـ contracts وidcard وarchive ([docker-compose.production.yml:292, 399, 420](../docker-compose.production.yml)).
    - `directory.sync` لـ it-infra ([:465](../docker-compose.production.yml)).
    - `hr-core.lookup dsar.subject.read` لـ compliance ([docker-compose.services.yml:366](../docker-compose.services.yml)).
    - وسكربت Keycloak يمنح `hr-core.workflow` لـ contracts وadmin-logistics فقط ([sync-keycloak-dev-realm.ps1:521-535](../scripts/sync-keycloak-dev-realm.ps1)).
  - الحصيلة: من 6 خدمات تطلب أرقاماً، خدمة واحدة (`admin-logistics`) مضبوطة في compose، ولا خدمة في k8s.
- **الأثر:**
  - في HR.Contracts يرمي `ApplyReferenceAsync` استثناءً عند غياب الرقم ([ServiceTransactionReferenceSupport.cs:25-30](../src/Services/HRContracts/HR.Contracts.Application/Services/ServiceTransactionReferenceSupport.cs)). وهو مستدعى في 12 موضعاً، منها:
    - طلب الإجازة ([LeaveRequestHandlers.cs:91](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
    - السلفة، والتجربة، والانفكاك ومعالجه.
    - القرار الإداري المرتبط بموظف.
    - معاملة التعيين القادمة من التوظيف ([InboundEventCommands.cs:119](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)) وبقية أحداث ESS الواردة.
  - ويفشل أيضاً:
    - قرار التعيين ([IssueAppointmentDecision.cs:55-58](../src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/IssueAppointmentDecision.cs)).
    - إصدار البطاقة ([IssuedCardsController.cs:80, 288](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)).
    - تسجيل جهاز IT ([UserDevicesController.cs:29-30](../src/Services/ITInfrastructure/IT.Infra.Web/Controllers/UserDevicesController.cs)).
    - فتح القضية القانونية ([ComplianceExtendedControllers.cs:34-35](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)).
    - مراجع الأرشيف، التي تضيع بصمت (G-COD-06).
  - أي أن مسار التوظيف حتى التعيين، الذي أُعلن ناجحاً في PLAN-25، ينقطع في الإنتاج عند HR.Contracts.
- **الإصلاح المقترح:**
  - ضبط `Integration__HrCoreBaseUrl` والنطاق `hr-core.workflow` لكل خدمة تستدعي `AddHrCodeGeneration`، في compose وk8s وفي استيراد Keycloak الإنتاجي.
  - جعل `AddHrCodeGeneration` يفشل عند الإقلاع خارج Development إن غاب العنوان.
  - فحص صحة (`/health/ready`) يطلب معاينة رمز من HR.Core.
  - اختبار معماري يقارن قائمة الخدمات التي تستدعي `AddHrCodeGeneration` بمفاتيح compose وk8s والنطاقات.
- **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md)
- **التحقق المطلوب:**
  - (موجب) على مكدّس مبني من `docker-compose.production.yml` (لا من ملف القبول): طلب إجازة، وقرار تعيين، وبطاقة، وجهاز IT، وقضية، ومرجع أرشيف، كلها تحمل أرقاماً بصيغتها.
  - (سالب) حذف المفتاح، فتفشل الخدمة عند الإقلاع برسالة صريحة، لا عند أول طلب.

#### G-COD-02 — الجهات لا ترى السياسات الوطنية فتصدر أرقاماً بلا بادئة
- **الدليل:**
  - كل السياسات الوطنية تُبذر بـ `TenantId = DefaultTenantId` ([CodeGenerationSeeder.cs:302-316](../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)). والبذر الوطني يعمل دائماً على هذا المستأجر، حتى عند تجهيز جهة جديدة ([NationalSeedOrchestrator.cs:24](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/NationalSeedOrchestrator.cs)، [AgencyProvisioningService.cs:86](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)).
  - `CodePolicyTemplate` كيان تابع لمستأجر، فيطبَّق عليه مرشح EF `e.TenantId == EffectiveTenantId` ([TenantQueryFilterExtensions.cs:64-69](../src/Shared/HRManagement.ServiceDefaults/TenantQueryFilterExtensions.cs)). لذلك يبقى الشرط `t.TenantId == tenantId || t.Scope == National` في [CodeGeneratorService.cs:82-89](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs) بلا أثر، ولا يرى مستأجر الجهة أي قالب وطني.
  - طبقة الجهة الوحيدة الموجودة تغطي `OrgUnit` فقط ([code-ministry-finance-v1.json](../seed-packs/code-overlays/code-ministry-finance-v1.json)).
  - عند غياب القالب يُستعمل احتياط صامت ([CodeGeneratorService.cs:32-33](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). صيغته العامة `{EntityPrefix}-YYYY-#####` ([:177-190](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). والحقيقة `EntityPrefix` لا يرسلها إلا مرجع الأرشيف، والمقطع الناقص يصير نصاً فارغاً ([:257-258](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). أما `FallbackPrefix` فيُحسب ولا يُستعمل في التركيب أبداً.
  - الطلب المجهول (بوابة `/careers`) لا مستأجر له، فيكون `EffectiveTenantId = Guid.Empty` ([TenantAwareDbContext.cs:27](../src/Shared/HRManagement.ServiceDefaults/TenantAwareDbContext.cs)) ويأخذ الاحتياط أيضاً.
  - **دليل حيّ:** رقم طلب الشاغر في إعادة التشغيل الشبيهة بالإنتاج هو `"reference": "-2026-00031"` ([repair-dryrun-20261029-202041.json:8](../acceptance-evidence/AC-HCM-25/repair/repair-dryrun-20261029-202041.json)). ورقم طلب المواطن `ReferenceNumber=-2026-00005` ([30b-30b-otp-request-citizen.json:9](../acceptance-evidence/AC-HCM-30/http/30b-30b-otp-request-citizen.json)). كان المتوقع `REQ-2026-00031` و`CIT-2026-000005`.
  - شاشة `/CodePolicies` تعرض للجهة قائمة فارغة ([CodePoliciesController.cs:14-22](../src/Services/HRCore/HR.Core.Web/Controllers/CodePoliciesController.cs))، ولم يلاحظ أحد ذلك.
  - مفتاح السياسة (`SelectCodePolicy`/`ASSET-PRIVILEGED`) لا يجد قالبه في الجهة أيضاً، فتأخذ العهدة المميزة رقم `AH-` العادي ([CodeGeneratorService.cs:94-108](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
- **الأثر:**
  - كل رقم يصدر في جهة يكون بلا بادئة، ويتطابق نصه بين الأنواع: طلب الشاغر `-2026-00031`، وخطاب العرض `-2026-00031`، وقرار التعيين `-2026-00031`. هذه أرقام تُطبع على وثائق رسمية وتُسجَّل في الديوان.
  - الأرقام غير قابلة للتعديل بعد الإصدار (مبدأ COD-001)، فكل رقم يصدر قبل الإصلاح يبقى خطأً دائماً.
- **الإصلاح المقترح:**
  - حل القالب صراحةً: `IgnoreTenantFilterForSystem` مع أولوية «طبقة الجهة ثم الوطني»، على نمط `ReferenceDataProvider.QueryAsync`.
  - **منع الاحتياط في الإنتاج:** غياب السياسة خطأ صريح، أو يستعمل الاحتياط `FallbackPrefix` على الأقل.
  - تحديد مستأجر الطلب المجهول من الإعلان.
  - سكربت يكشف الأرقام بلا بادئة في أي بيئة قائمة، ويطبق القرار D-COD-3.
- **الخطة:** [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md)
- **التحقق المطلوب:**
  - (موجب) شخصية في جهة غير افتراضية تنشئ طلب شاغر وعرضاً وقرار تعيين وطلب مواطن، فتحمل الأرقام البادئات `REQ-`/`OFF-`/`DEC-APPT-`/`CIT-`، وتعرض `/CodePolicies` القوالب الموروثة.
  - (سالب) نوع بلا سياسة في Production يُرفض برسالة، ولا يصدر رقم يبدأ بـ `-`.

#### G-COD-03 — سياسات مبذورة تولّد تصادماً ثم استثناءً بعد حرق الرقم
- **الدليل:**
  - **(أ) مركز الكلفة:** السياسة `Field OrgUnitCode` وحده، بلا تسلسل، و`Never` ([CodeGenerationSeeder.cs:153-159](../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)). ويولَّد لكل منصب ممول ([FundedPositionCodeService.cs:38-49](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/FundedPositionCodeService.cs)). فالمنصب الثاني في الوحدة نفسها يولّد النص نفسه، فيكسر الفهرس الفريد للإصدار ([HRCoreDbContext.cs:1135](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)) ويرمي `DbUpdateException` بعد زيادة العدّاد ([CodeGeneratorService.cs:409-419](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
  - **(ب) المنصب الممول:** نطاق العدّاد يشمل `BudgetYear`، ونص الرقم لا يشمله (`{OrgUnitCode}-{GradeCode}-###`) ([CodeGenerationSeeder.cs:60-70](../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)). والتفرد في الكيان مقصود أن يكون لكل سنة: `(TenantId, PositionCode, BudgetYear)` ([HRCoreDbContext.cs:970](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)، [EST_001_ESTABLISHMENT.md:55](EST_001_ESTABLISHMENT.md)). لكن سجل الإصدار يمنع تكرار النص، فأول منصب في سنة موازنة جديدة يفشل لكل وحدة ودرجة سبق أن صدر لها رمز.
  - **(ج) طلب الشاغر:** النطاق `BudgetYear`، والرقم يحمل سنة التقويم `REQ-YYYY-` ([CodeGenerationSeeder.cs:172-181](../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)، [RecruitmentRequisitionsController.cs:203-204](../src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentRequisitionsController.cs)). فطلب لمنصب موازنة 2027 في أواخر 2026 يبدأ عدّاداً جديداً ويولّد `REQ-2026-00001` مكرراً.
  - **(د) اختيار غير حتمي:** للعهدة قالبان وطنيان بالنوع نفسه (`AH` و`VIP`) ([CodeGenerationSeeder.cs:72-81, 220-235](../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs)). والاستعلام الأول يأخذ أول قالب بترتيب متعادل، ولا يقارن `PolicyKey` إلا إذا أرسله الطلب ([CodeGeneratorService.cs:82-96](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). فقد تأخذ العهدة العادية رقم `VIP-` (يحتاج تحقق من ترتيب PostgreSQL). ويتشارك القالبان عدّاداً واحداً.
  - **(هـ) تهيئة العدّادات بعد الترحيل:** للمنصب الممول تُبنى بمفتاح `{tenant}|FundedPosition|{Org-Grade}` ([CodSequenceBootstrapService.cs:98-121](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodSequenceBootstrapService.cs)). والمولّد يستعمل `{tenant}|FundedPosition|{Org}|{Grade}|{BudgetYear}` ([CodeGeneratorService.cs:222-244](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). فالعدّاد المهيأ لا يُقرأ أبداً، ويبدأ الترقيم من 001 فوق الأرقام المرحَّلة. ولا يستدعي أي مسار تشغيل `BootstrapFromObservedCodesAsync` لبقية الخدمات (الاختبارات فقط).
  - لم يكشف أي اختبار (أ) لأن اختبارات الملاك تستبدل الخدمة: `draft.CostCenter = "CC-GEN"` ([Est002EstablishmentGovernanceTests.cs:120-129](../src/Tests/HRManagement.UnitTests/Est002EstablishmentGovernanceTests.cs)). ولأن شخصيات القبول في جهة تأخذ الاحتياط (G-COD-02).
- **الأثر:**
  - في المستأجر الافتراضي يتوقف بناء جدول الملاك بعد منصب واحد في كل وحدة. ويتوقف مع كل سنة موازنة جديدة. ويتوقف إعداد طلبات موازنة السنة التالية.
  - الطلب المعتمد يبقى `Approved` ويفشل التطبيق بـ 500 عند كل محاولة، وكل محاولة تحرق رقماً.
  - **إصلاح G-COD-02 وحده ينقل هذه الأعطال إلى كل الجهات**، فالإصلاحان متلازمان.
- **الإصلاح المقترح:**
  - مدقق للسياسة عند البذر والنشر: كل حقل نطاق يجب أن يظهر في الرقم، ووجود مقطع `Sequence` إلزامي إلا بإعلان صريح، والتوافق مع فهرس الكيان (تضمين السنة أو جعل الإصدار فريداً لكل سنة).
  - تصحيح السياسات الثلاث.
  - في الاستعلام الأول: `PolicyKey == null` إن لم يُطلب مفتاح.
  - توحيد بناء مفتاح النطاق في دالة مشتركة بين المولّد والتهيئة.
  - تحويل `DbUpdateException` على الإصدار إلى خطأ مفهوم.
- **الخطة:** [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md)
- **التحقق المطلوب:**
  - (موجب) في المستأجر الافتراضي وفي جهة، بالخدمة الحقيقية لا بنسخة وهمية: منصبان في الوحدة نفسها، ثم منصب للوحدة والدرجة نفسيهما في سنة موازنة تالية، ثم طلب شاغر لموازنة السنة القادمة. كلها تنجح بأرقام فريدة.
  - (سالب) نشر سياسة نطاقها يحوي حقلاً غائباً عن الرقم يُرفض.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-COD-04 — الرقم يُصرف خارج معاملة العمل ولا يرتبط بكيان
- **الدليل:**
  - داخل HR.Core، يزيد `GenerateAsync` العدّاد ثم يحفظ `CodeIssuance` بـ `SaveChangesAsync` خاص به ([CodeGeneratorService.cs:42-45, 409-419](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). هذا الحفظ يدفع معه أي تغييرات معلّقة في `DbContext` المستدعي. أمثلة: [RecruitmentOfferService.cs:106](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs)، [EstablishmentChangeLifecycleService.cs:38](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs)، [OrgUnitsController.cs:168](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs). وما لم يفتح المستدعي معاملة، يُثبَّت الرقم قبل حفظ الكيان.
  - `CodeIssuance` لا يحوي `EntityType` ولا `EntityId` ولا حالة (مُصدر/مبطل) ([CodeGenerationEntities.cs:93-109](../src/Services/HRCore/HR.Core.Domain/Entities/CodeGeneration/CodeGenerationEntities.cs)).
  - عبر HTTP لا يوجد مفتاح idempotency. إن انتهت المهلة بعد الإصدار يعيد العميل `null` ([CodeGenerationClient.cs:52-55](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs))، فيُعاد الطلب برقم جديد.
  - شاشة السجل تعرض آخر 100 إصدار بلا ربط ولا تصدير ([CodePoliciesController.cs:24-32](../src/Services/HRCore/HR.Core.Web/Controllers/CodePoliciesController.cs)).
- **الأثر:** كل فشل بعد التوليد يترك فجوة في التسلسل، بلا أثر يشرحها: تحقق فاشل، أو تصادم، أو 500، أو مهلة. وهذا يصيب سجل القرارات الإدارية (`DEC-`) وطلبات الشاغر والعروض. ولا يمكن الجواب عن سؤال المدقق: «أين القرار رقم 17؟». ودفع تغييرات المستدعي يكسر ذرّية عملياته.
- **الإصلاح المقترح:**
  - داخل HR.Core: إضافة سجل الإصدار إلى `DbContext` دون حفظ، ليُحفظ مع الكيان في معاملة واحدة.
  - عبر HTTP: حجز ثم تأكيد أو إبطال (`Reserve` → `Confirm(entityId)` / `Void(reason)`) بمفتاح idempotency، ومهمة تبطل الحجوزات اليتيمة بعد مهلة.
  - تقرير فجوات لكل نوع وسنة.
- **التحقق المطلوب:**
  - (موجب) فشل حفظ عرض بعد التوليد لا يترك إصداراً مؤكداً، ويظهر الرقم «مبطلاً» بسبب في التقرير.
  - (سالب) تكرار طلب التوليد بالمفتاح نفسه يعيد الرقم نفسه.

#### G-COD-05 — واجهة التوليد تثق بـ `TenantId` من جسم الطلب وتسمح بحرق الأرقام
- **الدليل:**
  - `var tenantId = request.TenantId ?? db.CurrentTenantId ?? …` ([CodeGeneratorService.cs:31](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). الزيادة تتم بـ SQL خام لا يمر بمرشح المستأجر ([:349-370](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)). ثم يرفض `TenantIdStampInterceptor` حفظ الإصدار لمستأجر آخر ([TenantIdStampInterceptor.cs:104-120](../src/Shared/HRManagement.ServiceDefaults/TenantIdStampInterceptor.cs))، لكن بعد أن تثبتت الزيادة.
  - اختبار AC-13 نفسه يرسل `TenantId` في الجسم ([Ac13ParallelCodeGenerationTests.cs:32-37](../src/Tests/HRManagement.AcceptanceTests/Ac13ParallelCodeGenerationTests.cs)).
  - أي مستخدم `hr-core` يستطيع طلب `generate` لأي نوع وأي حقائق، مثل أرقام `AdministrativeDecision` ([CodeGenerationController.cs:20-27](../src/Services/HRCore/HR.Core.Web/Controllers/CodeGenerationController.cs)).
  - جداول الترميز أُنشئت بعد ترحيل RLS العام، فلا `EnableRls` لها في الترحيلات. لكن مسار النشر (`deploy/k8s/base/migrate-job.yaml:62` ← `scripts/run-migrations.sh:78`) يطبّق [ten002c-harden-tenant-rls.sql](../scripts/sql/ten002c-harden-tenant-rls.sql) على **كل** جدول فيه `TenantId`، بـ `FORCE ROW LEVEL SECURITY` وسياسة `USING`/`WITH CHECK` على `app.current_tenant`. ويضبط `NpgsqlTenantSessionInterceptor` هذا المتغير عند فتح الاتصال على مستأجر الطلب. لذلك يُتوقَّع أن يُرفض أمر `INSERT … ON CONFLICT` لعدّاد الجهة (ب) على قاعدة مُرحَّلة بهذا المسار («يحتاج تحقق» حيّ).
- **الأثر:** على قاعدة لم يمر عليها سكربت التقوية (مختبر، SQLite، ترحيل عند الإقلاع)، يحرق مستخدم في جهة (أ) أرقام الجهة (ب) ويترك فيها فجوات. وفي كل البيئات يستطيع مستخدم HR عادي استهلاك أرقام السجل الرسمي لجهته بلا كيان. *(صُحّح بعد التحقق المستقل: سكربت TEN-002C يفرض RLS على جداول الترميز في مسار النشر، فالحرق العابر للجهات يُتوقَّع أن يفشل بخطأ قاعدة بيانات؛ الثقة بـ `TenantId` من الجسم والحرق داخل الجهة مؤكدان)*
- **الإصلاح المقترح:**
  - تجاهل `TenantId` من الجسم إلا لحساب خدمة أو مشغّل منصة.
  - قصر `generate` على حسابات الخدمة، وتوليد الأرقام داخل HR.Core عبر `ICodeGenerator` فقط.
  - تفعيل RLS على جداول الترميز.
- **الخطة:** [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md)
- **التحقق المطلوب:**
  - (سالب) مستخدم جهة (أ) يرسل `TenantId` للجهة (ب)، فيُرفض الطلب أو يُطبَّق على (أ)، ولا يتغير عدّاد (ب).
  - (سالب) مستخدم `hr-core` يطلب `generate` مباشرة فيحصل على 403.

#### G-COD-06 — مستهلكات الأرشيف تُسقط الرسالة بصمت إن تعذّر الرقم
- **الحالة:** ✅ عولجت في [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) — `GenerateRequiredAsync` يرمي `CodeGenerationUnavailableException` (عابر، خارج قائمة التجاهل)، فتعيد MassTransit المحاولة ثم تنقل الرسالة إلى `_error`، ولا تؤكد المستهلكات السبعة رسالة بلا `ArchiveItem`. عدّاد `archive_reference_failures_total`. AC-INT-37d: إيقاف HR.Core ← `_error`، ثم إعادة التشغيل ← صف واحد بالضبط.
- **الدليل:**
  - `ArchiveReferenceFactory` يعيد `null` عند الفشل ([ArchiveReferenceFactory.cs:17-21](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ArchiveReferenceFactory.cs)).
  - سبعة مستهلكات تفعل `if (reference is null) return;`، فتؤكد الرسالة دون أرشفة:
    - [LeaveAndAdvanceApprovedConsumer.cs:21, 52](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs).
    - [SeparationCompletedConsumer.cs:27](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/SeparationCompletedConsumer.cs).
    - [MedicalArchiveConsumers.cs:26, 55](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/MedicalArchiveConsumers.cs).
    - [HireCaseCompletedConsumer.cs:24-28](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/HireCaseCompletedConsumer.cs).
    - [ComplaintResolvedArchiveConsumer.cs:22](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ComplaintResolvedArchiveConsumer.cs).
  - ومع G-COD-01 يحدث هذا لكل رسالة في الإنتاج.
- **الأثر:** لا يُنشأ ملف أرشيف لقرار الإجازة أو السلفة أو الانفكاك أو التعيين، ولا يُنشر `ITransactionArchived`. ولا تبقى رسالة في طابور الخطأ يمكن إعادة تشغيلها.
- **الإصلاح المقترح:** أن يرمي العميل استثناءً عابراً عند تعذّر الخدمة، فتعيد MassTransit المحاولة ثم تنقل الرسالة إلى `_error`. وعدّاد ومنبّه لرسائل الأرشيف الفاشلة.
- **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md)
- **التحقق المطلوب:**
  - (سالب) إيقاف HR.Core ثم اعتماد إجازة، فتبقى الرسالة في `_error` أو قيد إعادة المحاولة.
  - (موجب) بعد إعادة التشغيل يُنشأ `ArchiveItem` مرة واحدة.

#### G-COD-07 — سجل القرارات الإدارية بثلاثة أنظمة ترقيم
- **الدليل:**
  - **قرار التعيين** وحده يُرقَّم عبر COD ([IssueAppointmentDecision.cs:55-58](../src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/IssueAppointmentDecision.cs)).
  - **القرار العام** رقمه نص يدوي من النموذج ([AdministrativeDecisionHandlers.cs:42](../src/Services/HRContracts/HR.Contracts.Application/Features/AdministrativeDecisions/AdministrativeDecisionHandlers.cs)). ويمكن تعديله بعد الإصدار ([:86](../src/Services/HRContracts/HR.Contracts.Application/Features/AdministrativeDecisions/AdministrativeDecisionHandlers.cs)). ولا فهرس فريد على `DecisionNumber` في `ContractsDbContext`.
  - **قرار التغيير الوظيفي** الوارد من ESS رقمه `DecisionNumber = $"AUTO-{DateTime.UtcNow:yyyyMMddHHmmss}"` ([InboundEventCommands.cs:351](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs))، فيتكرر الرقم لقرارين في الثانية نفسها.
  - مرجع قرار النقل والترفيع نص يدوي `TransitionDecisionRef` ([TransferRequestsController.cs:167](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)).
- **الأثر:** لا يوجد سجل قرارات متسلسل موحّد للجهة. يمكن تكرار رقم قرار، أو تعديله بعد نشره في حدث `JobUpdateApplied` إلى خدمات أخرى. وتظهر في السجل الرسمي أرقام `AUTO-…` غير قانونية.
- **الإصلاح المقترح:**
  - كل قرار يُنشأ داخل النظام يأخذ رقمه من COD بنوع القرار (`TypeCode`) عند الإصدار، والرقم غير قابل للتعديل بعده.
  - القرار الخارجي المسجَّل يدوياً يُعلَّم «مرجع خارجي» مع فهرس فريد `(TenantId, DecisionNumber)`.
  - إلغاء `AUTO-`.
- **التحقق المطلوب:**
  - (موجب) قرار عام وقرار تغيير وظيفي يأخذان `DEC-{TYPE}-YYYY-#####` متتالياً.
  - (سالب) تعديل رقم قرار صادر يُرفض، وإدخال رقم خارجي مكرر يُرفض.

#### G-COD-08 — تغييرات الهيكل التنظيمي تُطبَّق فوراً بلا اعتماد ولا مرجع قرار
- **الدليل:**
  - `OrgUnitsController.Create/Edit` تعدّل الوحدة وتكتب النسخة بـ `DateTime.UtcNow`، دون `ApprovedBy` ولا `DecisionReference` ولا تاريخ نفاذ ([OrgUnitsController.cs:186-192, 250-281](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)). وبهذا يبقى حقلا القرار في `OrgUnitVersion` فارغين دائماً ([OrgUnitVersion.cs:62-68](../src/Services/HRCore/HR.Core.Domain/Entities/OrgUnitVersion.cs)).
  - `EstablishmentChangeType` لا يحوي أي عملية على الوحدات ([EstablishmentChangeRequest.cs:7-21](../src/Services/HRCore/HR.Core.Domain/Entities/EstablishmentChangeRequest.cs))، فالهيكل خارج مسار الاعتماد الذي أصلحه PLAN-25 للملاك.
  - الوثيقة تعد بـ «Approval and impact analysis before execution» و«Cancellation of pending changes» و«every structural change traces to a decree» ([ORG_001_DATED_STRUCTURE.md:12-13, 35](ORG_001_DATED_STRUCTURE.md))، و`completion-backlog/09` يعلن ORG-001 «Done».
- **الأثر:** هذا توأم نمط «التطبيق عند التقديم» الذي وجده PLAN-25 في الملاك. أي مستخدم `hr-core` يستطيع إعادة تسمية وحدة أو تغيير رئيسها أو نقلها تحت فرع آخر، فوراً. والنقل يغيّر في اللحظة نطاق رؤية مديري الفروع لموظفي الفرع المنقول (`MaterializedPath`)، دون قرار أو أثر قانوني.
- **الإصلاح المقترح:** طلب تغيير هيكلي (إنشاء/تسمية/تكليف رئيس/نقل/إغلاق) على نمط `EstablishmentChangeRequest`:
  - مرجع قرار وسند قانوني إلزاميان.
  - عرض أثر قبل الاعتماد: عدد الموظفين والمناصب والمستخدمين ذوي النطاق.
  - مسار اعتماد.
  - إلغاء قبل النفاذ.
  - تبقى الشاشة الحالية للمسودة فقط.
- **التحقق المطلوب:**
  - (موجب) نقل وحدة يمر بخطوتي اعتماد، ولا يتغير `MaterializedPath` قبل الاعتماد.
  - (سالب) POST مباشر إلى `Edit` يغيّر الأب يُرفض أو ينشئ مسودة فقط.

#### G-COD-09 — التاريخ المستقبلي يُطبَّق فوراً، والتاريخ الرجعي يُفسد السجل (الهيكل والملاك)
- **الدليل:**
  - `AppendVersionAsync` يغلق النسخة المفتوحة بـ `open.ValidTo = effectiveFrom` دون فحص أن `effectiveFrom > open.ValidFrom` ([OrgUnitVersionService.cs:36-54](../src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs)). ويأخذ الحالة من الصف الحي الذي عدّله المستدعي مسبقاً ([:58-84](../src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs)). والنمط نفسه في [FundedPositionVersionService.cs:70, 78](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/FundedPositionVersionService.cs).
  - `OrgStructureApiController.UpdateUnit` يقبل أي `EffectiveFrom` ([OrgStructureApiController.cs:49](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)). وكذلك طلب الملاك عند التقديم ([EstablishmentChangeLifecycleService.cs:116](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs)) والتطبيق ([EstablishmentChangeApplier.cs:87-89](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeApplier.cs)). ولا جدولة للنفاذ المستقبلي، وPLAN-17 يستبعدها صراحةً.
  - لا قيد على وجود نسخة مفتوحة واحدة: الفهارس `(OrgUnitId, ValidFrom)` غير فريدة ([HRCoreDbContext.cs:945-955](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)). ولا رمز تزامن على `OrgUnit` ولا `FundedPosition`.
  - الكتابة على أربع دفعات مستقلة: الوحدة، ثم المسار، ثم النسخة، ثم الحدث ([OrgUnitsController.cs:187-192](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)).
  - الاستكمال الآلي يصنع «تاريخاً» من الحالة الحالية منذ `CreatedAt` ([OrgUnitVersionService.cs:228](../src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs)).
- **الأثر:**
  - **مستقبلي:** إعادة هيكلة نافذة في 1/1 تظهر اليوم في القوائم والنطاق والأحداث. وتجميد منصب مستقبلي يجعل الصف الحي `Frozen` الآن، بينما as-of(اليوم) يقول `Vacant`.
  - **رجعي:** نسخة بـ `ValidTo < ValidFrom` ونسختان متداخلتان، فتتغير نتيجة as-of لتاريخ سابق. وهذا خرق مباشر لمعيار «التاريخ لا يعاد كتابته».
  - فشل كتابة النسخة يترك وحدة معدَّلة بلا تاريخ.
- **الإصلاح المقترح:**
  - كتابة الصف الحي والنسخة والحدث في معاملة واحدة.
  - رفض `effectiveFrom` قبل بداية النسخة المفتوحة. التصحيح الرجعي عملية مستقلة بدور مركزي وتدقيق (D-COD-5).
  - التغيير المستقبلي يُخزَّن «مجدولاً»، ومهمة تطبقه عند النفاذ.
  - فهرس فريد جزئي `WHERE ValidTo IS NULL`، وفحص عدم التداخل.
  - رمز تزامن `xmin` على `OrgUnit` و`FundedPosition`.
  - وسم النسخ المستكملة `Backfilled`.
- **التحقق المطلوب:**
  - (موجب) تغيير نافذ غداً لا يظهر في القائمة ولا في النطاق اليوم، ويظهر بعد تشغيل المهمة في موعده.
  - (سالب) تغيير بتاريخ قبل النسخة المفتوحة يُرفض، وas-of لتاريخ سابق لا يتغير بعد تعديل لاحق (جوهر AC-14).

#### G-COD-10 — `UpdateUnit` يحفظ الأب قبل فحص الدورة، وتعديل MVC الفاشل يحفظ الاسم بلا نسخة
- **الدليل:**
  - في `OrgStructureApiController.UpdateUnit` يُضبط `orgUnit.ParentId = request.ParentId` ([OrgStructureApiController.cs:56-58](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)) ويُحفظ ([:62](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)) **قبل** `MoveSubtreeAsync`. ثم يُفحص النقل عبر المسار المادي القديم، فإن رفضه يعود الطلب `BadRequest` ([:63-64](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)) والأب الجديد محفوظ.
  - لا فحص نطاق للأب الجديد. أما `OrgUnitsController.Edit` فيفحصه ([OrgUnitsController.cs:241-246](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)).
  - في `OrgUnitsController.Edit` يُحفظ الاسم والرئيس ([:257](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)) قبل النقل. فإن رُفض النقل يبقى التعديل محفوظاً بلا نسخة وبلا حدث ([:258-264](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)).
  - عند الإقلاع يبني `RecomputeAllAsync` المسارات من `ParentId` بحد 64 دورة ([OrgUnitPathService.cs:35-50](../src/Services/HRCore/HR.Core.Infrastructure/OrgUnitPathService.cs)، [Program.cs:308](../src/Services/HRCore/HR.Core.Web/Program.cs)).
- **الأثر:** طلب نقل خاطئ (وحدة تحت أحد فروعها) يُنشئ دورة في `ParentId` رغم رسالة الرفض. وفي الإقلاع التالي تتحول الدورة إلى مسار مشوّه من 64 مقطعاً، فيفسد حساب النطاق لكل من تحت الجذر (يحتاج تحقق). وإن نجح النقل فقد يخرج من نطاق صاحبه.
- **الإصلاح المقترح:** عدم لمس `ParentId` إلا داخل `MoveSubtreeAsync`، وكل العملية في معاملة واحدة. وفحص نطاق الأب الجديد. ويُفضَّل أن تمر هذه الواجهة بطلب التغيير في G-COD-08.
- **التحقق المطلوب:**
  - (سالب) PUT ينقل وحدة تحت ابنها يعيد 400، و`ParentId` في القاعدة لم يتغير، وإعادة تشغيل HR.Core تُبقي المسارات صحيحة.
  - (سالب) مستخدم فرع ينقل وحدته تحت فرع آخر فيحصل على 403.

#### G-COD-11 — نسخ الهيكل في الخدمات الست بلا ترتيب ولا تاريخ نفاذ
- **الدليل:**
  - `IOrgUnitChanged` يحمل الحالة الحالية فقط، بلا `ValidFrom` ولا رقم نسخة ([IOrgUnitChanged.cs:3-14](../src/Shared/HRManagement.MessageContracts/Events/IOrgUnitChanged.cs)).
  - المستهلكون (Payroll وContracts وMovements وTraining وHealth وAdminLogistics) يكتبون آخر رسالة تصل، أياً كان ترتيبها. مثال: [Payroll OrgUnitChangedConsumer.cs:14-42](../src/Services/Payroll/Payroll.Infrastructure/Consumers/OrgUnitChangedConsumer.cs).
  - نقل فرع ينشر رسالة لكل وحدة متأثرة ([OrgUnitsController.cs:267](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)).
  - إعادة النشر الكاملة عند الإقلاع ([Program.cs:309](../src/Services/HRCore/HR.Core.Web/Program.cs)) تمر عبر Outbox (`UseBusOutbox`)، ولا يليها `SaveChanges`، فلا تُسلَّم الرسائل (يحتاج تحقق). وهي على الأرجح تقرأ وحدات المستأجر الافتراضي وحده بسبب المرشح (يحتاج تحقق).
- **الأثر:** نطاق الفروع في Payroll وContracts قد يعتمد نسخة قديمة إذا وصلت رسالة متأخرة بعد أحدث منها. ولا آلية إعادة مزامنة فعالة. والتقارير في تلك الخدمات تجمّع بالهيكل الحالي، فلا يمكن إخراج تقرير رواتب «حسب الهيكل في آذار».
- **الإصلاح المقترح:**
  - إضافة `Version` (أو `UpdatedAt`) و`ValidFrom` للحدث، ورفض المستهلك أي نسخة أقدم.
  - إعادة مزامنة إدارية مجزأة لكل مستأجر تحفظ عبر Outbox.
  - سياسة صريحة للتقارير التاريخية.
- **التحقق المطلوب:**
  - (سالب) تسليم رسالتين بترتيب معكوس يُبقي اللقطة على الأحدث.
  - (موجب) أمر إعادة المزامنة يصل إلى Payroll لكل مستأجر، ويُرى في `OrgUnitSnapshots`.

#### G-COD-12 — واجهة MDM البرمجية تتجاوز حوكمة الشاشة
- **الدليل:**
  - `ReferenceDataController` (`api/reference-data`) يكفيه `HrCoreWrite` للإنشاء والتعديل ([ReferenceDataController.cs:130-185](../src/Services/HRCore/HR.Core.Web/Controllers/ReferenceDataController.cs)) و`HrCoreDelete` للتعطيل ([:187-198](../src/Services/HRCore/HR.Core.Web/Controllers/ReferenceDataController.cs)). ولا يستدعي شيئاً مما تفرضه الشاشة:
    - `ValidateCentralWriteAsync`.
    - منع تعديل سجلات المنصة `CanMutate` ([ReferenceCatalogAdminService.cs:257-258](../src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceCatalogAdminService.cs)).
    - تحليل الأثر قبل `IsActive=false`.
    - التحقق من `ReplacedByCode`.
    - `ValidateEffectiveDates`.
  - الوثيقة تقول «Admin UI **and API** reject deactivation when any affected count > 0» و«`IsActive = false` + `ValidTo` set» ([MDM_001_REFERENCE_DATA.md:68, 76](MDM_001_REFERENCE_DATA.md)). والواجهة البرمجية لا تفعل أياً منهما.
  - PLAN-20 يستبعد «workflow اعتماد لتغييرات MDM».
- **الأثر:** أي مستخدم `hr-core`، ولو كان نطاقه فرعاً، يستطيع بطلب PUT تعطيل درجة وطنية مستخدمة، أو تغيير اسمها، أو ربطها برمز استبدال غير موجود. وفي المستأجر الافتراضي يعني ذلك تعديل المرجع الوطني نفسه بلا اعتماد.
- **الإصلاح المقترح:** أن تمر الواجهة البرمجية عبر `IReferenceCatalogAdminService` نفسه، بالفحوص نفسها. وإضافة اعتماد ثنائي (maker/checker) لتغيير المرجع الوطني (D-COD-7).
- **التحقق المطلوب:**
  - (سالب) PUT بـ `isActive=false` لدرجة مستخدمة يعيد 409.
  - (سالب) مستخدم بنطاق فرع يحصل على 403.
  - (سالب) تعديل سجل منصة من جهة يعيد 403.

#### G-COD-13 — تحليل الأثر يرى دليل الوظائف فقط
- **الدليل:**
  - للدرجة، يعدّ `ReferenceImpactAnalyzer` استخدامها في `JobCatalogEntries.GradeCode` فقط ([ReferenceImpactAnalyzer.cs:21-26](../src/Services/HRCore/HR.Core.Domain/Services/ReferenceImpactAnalyzer.cs)، [ReferenceCatalogAdminService.cs:308-316](../src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceCatalogAdminService.cs)).
  - يتجاهل استخدامها في `FundedPosition.GradeCode` و`OfferLetter.GradeCode` و`CompetitionLine.GradeCode` و`WorkforcePlanLine` و`FinanceBudgetLine`، وكذلك `PayGrade` في Payroll و`Contract` في HR.Contracts.
  - للتصنيف يعدّ `JobCatalogEntry.OccupationalGroup` و`EmploymentRecord` و`CareerPathStep` فقط.
- **الأثر:** يمكن تعطيل درجة تحملها مئات المناصب الممولة، ما دام لا يوجد إدخال دليل نشط بها. بعدها يرفض `EstablishmentReferenceValidator` أي تغيير على تلك المناصب.
- **الإصلاح المقترح:** سجل مستهلكين لكل `CatalogKind` يشمل جداول HR.Core كلها، واستعلام أثر S2S إلى Payroll وContracts. ويُعرض الأثر في التفاصيل قبل التعطيل.
- **التحقق المطلوب:**
  - (سالب) تعطيل درجة يحملها منصب ممول واحد يُرفض مع العدد.
  - (موجب) تعطيل درجة غير مستخدمة في أي خدمة ينجح.

#### G-COD-14 — لا مزامنة MDM عبر الخدمات، والدرجات في Payroll سجل منفصل
- **الدليل:**
  - `IReferenceDataChanged` له مستهلك يسجّل فقط ([ReferenceDataChangedConsumer.cs:9-16](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ReferenceDataChangedConsumer.cs))، ولا ناشر له في أي مكان. والوثيقة تقول «published on create/update/deactivate» ([MDM_001_REFERENCE_DATA.md:79](MDM_001_REFERENCE_DATA.md)).
  - لا خدمة خارج HR.Core تقرأ MDM:
    - `PayGrade.GradeCode` في Payroll نص حر يُدار من شاشته ([PayGradesController.cs:70](../src/Services/Payroll/Payroll.Web/Controllers/PayGradesController.cs))، والبذور التجريبية `G1..G5` ([DemoPayrollSeeder.cs:24-28](../src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs)).
    - المرجع الوطني يحوي `GR-01..GR-15, GR-EX` ([catalog.json](../seed-packs/national-mdm-sy-v1/catalog.json)).
    - بذور القبول للمناصب والعروض تستعمل `"G5"` ([AcceptanceRecruitmentStatesPatch.cs:273](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceRecruitmentStatesPatch.cs)).
    - `BankName` في ملف الأجر نص حر، رغم وجود فهرس `Bank`.
  - الجهة تحصل على **نسخة** من المرجع عند التجهيز ([MdmReferenceSeeder.cs:65-109](../src/Services/HRCore/HR.Core.Infrastructure/Data/MdmReferenceSeeder.cs)، [AgencyProvisioningService.cs:127](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)) لا تُحدَّث بعدها. والبذر الوطني يُدرج فقط ولا يحدّث ([MdmReferenceSeeder.cs:37-62](../src/Services/HRCore/HR.Core.Infrastructure/Data/MdmReferenceSeeder.cs)).
  - `ReferenceDataProvider` يأخذ نسخة الجهة **أو** المنصة، لا الاثنتين معاً ([ReferenceDataProvider.cs:102-105](../src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceDataProvider.cs)). أما شاشة الإدارة فتدمجهما ([ReferenceCatalogAdminService.cs:263-285](../src/Services/HRCore/HR.Core.Infrastructure/Services/MasterData/ReferenceCatalogAdminService.cs)). فجهة بلا نسخة تضيف رمزاً محلياً واحداً، فتختفي كل الدرجات الوطنية من المنتقي والتحقق (يحتاج تحقق حسب بيان التجهيز).
- **الأثر:**
  - تحقق الراتب عند العرض (PLAN-27) يبحث عن درجة المنصب في Payroll بالرمز. حين تختلف الرموز بين MDM وPayroll يسقط إلى «راتب غير متحقق» أو استثناء (يحتاج تحقق).
  - تعديل وطني لاسم درجة لا يصل إلى أي جهة.
  - لا مصدر حقيقة (SoR) معلن للدرجة: `docs/master-data/field-ownership.json` لا يذكرها.
- **الإصلاح المقترح:**
  - نشر `IReferenceDataChanged` عبر Outbox من خدمة الإدارة.
  - مستهلك في Payroll يربط `PayGrade` برمز MDM ويرفض الرموز غير المعروفة.
  - مزامنة نسخ الجهات أو إلغاؤها لصالح الدمج.
  - توحيد منطق الدمج بين المزوّد والشاشة.
  - قرار SoR للدرجة (D-COD-6).
- **التحقق المطلوب:**
  - (موجب) تعديل اسم `GR-05` وطنياً يصل إلى Payroll خلال دقيقة.
  - (سالب) إنشاء `PayGrade` برمز غير موجود في MDM يُرفض.
  - (موجب) جهة تضيف رمزاً محلياً وتبقى ترى الدرجات الوطنية.

### 4.3 فجوات قدرات (P2)

#### G-COD-15 — لا دمج ولا تقسيم ولا إغلاق للوحدات، والحذف يفشل دائماً
- **الدليل:**
  - `Merge` و`Split` و`Close` لا تظهر إلا في اختبار قيم التعداد ([Org001DatedStructureTests.cs:47-49](../src/Tests/HRManagement.UnitTests/Org001DatedStructureTests.cs)).
  - `OrgUnit` بلا حالة إغلاق ([OrgUnit.cs:7-36](../src/Services/HRCore/HR.Core.Domain/Entities/OrgUnit.cs)).
  - `Delete` يحذف حذفاً فعلياً ([OrgUnitsController.cs:307-309](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)). لكن كل وحدة لها نسخة بعد الاستكمال، والمفتاح الأجنبي `Restrict` ([HRCoreDbContext.cs:951-954](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs))، فيفشل الحذف بخطأ قاعدة بيانات (يحتاج تحقق من رسالة الواجهة).
  - المناصب الممولة لا تنتقل بين الوحدات، ومنطق as-of للمناصب يفترض ذلك، إذ يختار بالوحدة الحالية ([FundedPositionVersionService.cs:134](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/FundedPositionVersionService.cs)).
- **الأثر:** إعادة الهيكلة الحكومية المعتادة (دمج مديريتين، فصل قسم، إلغاء وحدة بمرسوم) غير ممكنة. والوحدات الملغاة تبقى في المنتقيات والنطاق.
- **الإصلاح المقترح:**
  - `Close` بنسخة إغلاق وحالة على الوحدة، تُستبعد من المنتقيات، ويُمنع الإغلاق مع وجود موظفين أو مناصب نشطة.
  - `Merge` و`Split` كطلب تغيير واحد ينقل الموظفين والمناصب (بنسخ مؤرخة للمناصب) ويغلق المصدر.
  - استبدال الحذف بالإغلاق.
- **التحقق المطلوب:**
  - (موجب) دمج وحدتين ينقل 3 مناصب، وas-of قبل الدمج يعيد الوحدتين.
  - (سالب) إغلاق وحدة فيها موظف نشط يُرفض.

#### G-COD-16 — لا شاشة تاريخ ولا as-of للشجرة ولا تقارير بهيكل تاريخي
- **الدليل:**
  - الاستعلام الزمني لوحدة واحدة عبر API فقط ([OrgStructureApiController.cs:23-33](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)).
  - لا مرجع لـ as-of في `Views/OrgUnits` (البحث يجد as-of في شاشات المناصب والدليل والخطط فقط).
  - لا as-of للأبناء ولا للشجرة. والوثيقة تعد بـ «org snapshots».
- **الأثر:** لا يستطيع المستخدم رؤية «الهيكل في تاريخ القرار» ولا خط زمن الوحدة. وهذا مطلوب للتدقيق والتظلمات وتقارير نهاية السنة.
- **الإصلاح المقترح:** خط زمن في `OrgUnits/Details`، وشجرة as-of بتاريخ مختار، وتصدير للهيكل بتاريخ.
- **التحقق المطلوب:**
  - (موجب) شجرة 1/3/2026 تُظهر الاسم القديم بعد إعادة تسمية في 1/6.
  - (سالب) مستخدم بنطاق فرع لا يرى فروعاً أخرى في الشجرة التاريخية.

#### G-COD-17 — إدارة السياسات للقراءة فقط، ولا مقطع جهة، وتغطية الترقيم ناقصة
- **الدليل:**
  - `CodePoliciesController` يعرض ويعاين فقط ([CodePoliciesController.cs:14-53](../src/Services/HRCore/HR.Core.Web/Controllers/CodePoliciesController.cs)). لا إنشاء ولا نسخة جديدة ولا نشر ولا إيقاف، والتغيير لا يكون إلا عبر قاعدة البيانات.
  - قائمة الأنواع في الوثيقة 10 من 18 ([COD_001_CODE_GENERATION.md:17](COD_001_CODE_GENERATION.md)).
  - لا مقطع للجهة في أي سياسة وطنية، فـ `DEC-APPT-2026-00001` يتكرر بين الجهات.
  - لا رقم للعقد، ولا لقرار النقل أو الترفيع أو الندب (`TransitionDecisionRef` يدوي)، ولا للإجراء التأديبي، ولا لمسير الرواتب.
  - `MemberNumber = $"HIRE-{employeeId:N}"[..20]` في HRHealth ([PartyAckConsumers.cs:157](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs)).
- **الأثر:** لا يستطيع مسؤول الجهة تعديل صيغة رقم دون مطوّر. والأرقام غير قابلة للتمييز بين الجهات في التبادل الوطني والأرشيف. والوثائق الرسمية الأساسية بلا رقم صادر.
- **الإصلاح المقترح:**
  - شاشة إدارة سياسات بنسخ «مسودة ← اعتماد ← نشر» مع المدقق من G-COD-03.
  - مقطع `AgencyCode` من بيان الجهة (D-COD-1).
  - توسيع التغطية للعقود وقرارات الحركة والتأديب.
- **التحقق المطلوب:**
  - (موجب) نشر نسخة جديدة بتاريخ نفاذ يغيّر صيغة الأرقام بعد التاريخ فقط.
  - (سالب) نسخة لا تجتاز المدقق لا تُنشر.

#### G-COD-18 — طابور التكرار لا يكشف ولا يدمج
- **الدليل:**
  - `MasterDataDuplicatesController` واجهة JSON للمدير فقط (`HrCoreDelete`) ([MasterDataDuplicatesController.cs:13-16](../src/Services/HRCore/HR.Core.Web/Controllers/MasterDataDuplicatesController.cs)).
  - لا منتج آلي للحالات: الإنشاء يدوي فقط ([:52-71](../src/Services/HRCore/HR.Core.Web/Controllers/MasterDataDuplicatesController.cs)).
  - يمكن نقل الحالة إلى `Merged` دون أي دمج ([:116-124](../src/Services/HRCore/HR.Core.Web/Controllers/MasterDataDuplicatesController.cs)).
  - الدمج الفعلي في `EmployeeLifecycleController.Merge` يعلّم المصدر فقط ويحيل الخدمات الأخرى إلى «runbook» يدوي ([EmployeeLifecycleController.cs:67-106](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleController.cs)).
  - `PersonMergeService` المذكور دليلاً لإغلاق MDM-002 غير مستدعى في أي مسار تشغيل ([PersonMergeService.cs:16-27](../src/Shared/HRManagement.ServiceDefaults/MasterData/PersonMergeService.cs)).
- **الأثر:** ادعاء «golden person record with reversible merge» لا يقابله تشغيل. والتكرار عبر الجهات لا يُكتشف.
- **الإصلاح المقترح:** كشف دوري (اسم مطبّع + تاريخ ميلاد + رقم وطني مشفّر بين الجهات) ينشئ حالات، وشاشة مراجعة، وجعل `Merged` نتيجة للدمج فقط، وحدث دمج تستهلكه الخدمات.
- **التحقق المطلوب:**
  - (موجب) موظفان بالاسم المطبّع وتاريخ الميلاد نفسيهما في جهتين تظهر لهما حالة.
  - (سالب) `UpdateStatus` إلى `Merged` دون دمج يُرفض.

### 4.4 تحسينات (P3)

#### G-COD-19 — المنطقة الزمنية وطول الرموز ومقاطع غير منفّذة
- **الدليل:**
  - السنة في الرقم وفي مفتاح التصفير تُحسب من `DateTime.UtcNow` ([CodeGeneratorService.cs:240-241, 268-275, 284](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs))، بينما الجهة على `Asia/Damascus` ([AgencyManifestModels.cs:72](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyManifestModels.cs)). فما يصدر بين 00:00 و03:00 من 1/1 يحمل السنة السابقة.
  - رمز الوحدة يطول 3 أحرف مع كل مستوى، وحده 50 حرفاً ([OrgUnit.cs:11](../src/Services/HRCore/HR.Core.Domain/Entities/OrgUnit.cs)) مع عمق مسموح 32. ورمز المنصب حده 30 ([FundedPosition.cs:14](../src/Services/HRCore/HR.Core.Domain/Entities/FundedPosition.cs)). بالصيغة الوطنية يتجاوز رمزُ المنصب 30 حرفاً من المستوى السابع، فيُحرق الرقم ثم يفشل الحفظ.
  - `LookupPrefix` و`ParentCode` تُعامَلان كحقل عادي ولا تبحثان في شيء ([CodeGeneratorService.cs:257-258](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
  - المعاينة لا تحجز الرقم، ولا تقول الواجهة إنه تقريبي.
- **الإصلاح المقترح:** منطقة زمنية للجهة في حساب السنة، وفحص الطول في مدقق السياسة مقابل عمود الكيان، وإزالة المقاطع غير المنفّذة أو تنفيذها، وعبارة «رقم تقريبي» في المعاينة.
- **التحقق المطلوب:**
  - (موجب) رقم يصدر 1/1 الساعة 01:00 بتوقيت دمشق يحمل السنة الجديدة.
  - (سالب) سياسة قد يتجاوز ناتجها طول العمود تُرفض.

### 4.5 فجوات التحقق والتوثيق

#### G-COD-20 — AC-13 وAC-14 ورفاقهما لم تُشغَّل، وتصميمها لا يكشف الفجوات (P1)
- **الدليل:**
  - [summary.md](../acceptance-evidence/summary.md) يضع AC-13 وAC-14 في «Not yet executed». ولا مجلد أدلة لـ AC-EST-13..18 ولا لـ AC-EST-07..09، ولا ملف اختبار لـ AC-EST-07..09 أصلاً.
  - [Ac13ParallelCodeGenerationTests.cs:27-71](../src/Tests/HRManagement.AcceptanceTests/Ac13ParallelCodeGenerationTests.cs):
    - يختبر `AssetTag` وحده، بلا حقول نطاق، في جهة تأخذ احتياطاً شكله `AH-` يشبه الوطني.
    - يمرّر `TenantId`.
    - لا يختبر حالة «لا كسر لنطاق (جهة/نوع/إصدار)» المطلوبة في PLAN-04.
    - `WriteDbChecks` ينسخ عدد الاستجابات ولا يقرأ قاعدة البيانات.
    - وليس موسوماً `[RequiredAcceptance]`.
  - [AcEst14JobCatalogCodPreviewTests.cs:35-38](../src/Tests/HRManagement.AcceptanceTests/AcEst14JobCatalogCodPreviewTests.cs) يكتفي بأن الرمز غير فارغ.
  - [Org001DatedStructureTests.cs](../src/Tests/HRManagement.UnitTests/Org001DatedStructureTests.cs) يختبر كائنات بسيطة وقيم تعداد ووجود ملفات. و`scripts/org001-validate.ps1` فحص وجود ملفات.
  - اختبارات الملاك تستبدل `IFundedPositionCodeService` (G-COD-03).
  - الوثائق:
    - PLAN-04 يصف `Serializable` ويقول إن `OrgUnitsController` لا يكتب نسخة. كلا الوصفين لم يعد صحيحاً ([PLAN-04:11-13](acceptance-plans/PLAN-04-codes-temporal-org.md)).
    - ORG-001 وMDM-001 وMDM-002 «Done» في `completion-backlog/09`، رغم G-COD-08/12/14/15/18.
- **الأثر:** لا دليل على أن الترقيم صحيح لأي جهة، ولا على أن as-of يصمد أمام تعديل لاحق. والاختبارات الحالية تنجح فوق الأعطال.
- **الإصلاح المقترح:**
  - إعادة كتابة AC-13: عدة أنواع، ونطاقات متعددة، وجهتان، وفحص DB حقيقي، وفحص الصيغة، و`[RequiredAcceptance]`.
  - AC-14 بسيناريو مستقبلي وآخر رجعي.
  - كتابة AC-EST-07..09.
  - تشغيل AC-EST-13..18.
  - اختبار تكامل بخدمة المنصب الحقيقية على المستأجر الافتراضي.
  - تصحيح الوثائق وبطاقات backlog.
- **الخطة:** [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md) لجزء AC-13 فقط. وبقية الأدلة (AC-14 وAC-EST) ← الموجة 2 (COD-G)
- **التحقق المطلوب:** مجلدات أدلة كاملة (العناصر الثمانية) لـ AC-13 وAC-14 وAC-EST-07..09 وAC-EST-13..18 على المكدّس الشبيه بالإنتاج، وكل سيناريو سالب مسجَّل في `result.json`.

### 4.6 تقاطعات مع الدراسات الأخرى

- → OPS: `hr-app-config` في k8s بلا `Integration__HrCoreBaseUrl` ولا `ServiceIdentity__Scope` لأي خدمة، فيتأثر `PolicyEnforceClient` و`HrCoreLookupClient` أيضاً، لا الترميز وحده ([configmap-app.yaml:7-29](../deploy/k8s/base/configmap-app.yaml)).
- → OPS: كل نسخة من HR.Core تعيد عند الإقلاع كتابة مسارات كل الوحدات لكل المستأجرين، وتعيد نشرها ([Program.cs:308-309](../src/Services/HRCore/HR.Core.Web/Program.cs)).
- → IAM: جداول `CodeSequenceCounters`/`CodeIssuances`/`CodePolicy*` أُنشئت في `20260805125450_COD001_CodeGenerationEngine` بعد `TEN002B_ExtendRls`، بلا `EnableRls`. والعدّاد يُحدَّث بـ SQL خام خارج مرشح EF ([CodeGeneratorService.cs:349-370](../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs)).
- → IAM: كاش الشجرة `IOrgUnitHierarchyCache` في ذاكرة كل نسخة (≈90 ثانية)، فيبقى النطاق قديماً بعد النقل حين تعمل عدة نسخ ([ORG_UNIT_HIERARCHY.md](ORG_UNIT_HIERARCHY.md)).
- → IAM: `HeadEmployeeId` في POST لا يُفحص مقابل النطاق ([OrgUnitsController.cs:183, 252](../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)). وفحص نطاق as-of يستعمل الهيكل الحالي لا التاريخي ([OrgStructureApiController.cs:26-28](../src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs)).
- → WF: لا تعريف مسار اعتماد لتغيير الهيكل، و`EstablishmentChangeType` بلا عمليات على الوحدات ([EstablishmentChangeRequest.cs:7-21](../src/Services/HRCore/HR.Core.Domain/Entities/EstablishmentChangeRequest.cs)).
- → QA: كل شخصيات القبول في جهة `AgencyA` (غير الافتراضية)، فمسارات المستأجر الافتراضي لا تُختبر حيّاً أبداً. والملف «الشبيه بالإنتاج» يرث إصلاحات ملف القبول ([docker-compose.acceptance-prodlike.yml:6-7](../docker-compose.acceptance-prodlike.yml)).
- → POL: أثر `SelectCodePolicy` (`ASSET-PRIVILEGED`) يضيع في الجهات لأن القالب غير مرئي ([AssetsController.cs:820-824](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-COD-1 | هل يحمل الرقم الرسمي (قرار، طلب شاغر، عرض، مرجع أرشيف) رمز الجهة، وهل التسلسل لكل جهة؟ | تسلسل لكل جهة، ومقطع `AgencyCode` إلزامي في أرقام الوثائق الرسمية (`DEC-MOF-APPT-2026-00001`) | G-COD-02، G-COD-03، G-COD-17 |
| D-COD-2 | سياسة الفجوات في سجل القرارات | سجل القرارات بلا فجوات (الرقم يصدر عند الاعتماد أو التوقيع داخل المعاملة)، وبقية الأنواع تُقبل فيها الفجوات بشرط تسجيل الإبطال وسببه | G-COD-04، G-COD-07 |
| D-COD-3 | ماذا يُفعل بالأرقام التي صدرت بلا بادئة في أي بيئة قائمة؟ | لا إنتاج بعد: إعادة توليدها في بيئات الاختبار. وإن وُجدت بيانات حقيقية يُحفظ الرقم القديم مرجعاً بديلاً، ويُصدر رقم صحيح | G-COD-02 |
| D-COD-4 | من يعتمد تغيير الهيكل التنظيمي وتكليف رئيس الوحدة، وما السند المطلوب؟ | مسار مركزي من خطوتين بمرجع مرسوم أو قرار وسند قانوني إلزاميين، وتكليف الرئيس قرار إداري مرقَّم | G-COD-08، G-COD-15 |
| D-COD-5 | هل يُسمح بتغيير هيكلي أو ملاكي بأثر رجعي؟ | لا. التصحيح الرجعي عملية «تصحيح سجل» منفصلة بدور مركزي وتدقيق، ولا يغيّر نسخاً أُغلقت قبل آخر إقفال رواتب | G-COD-09 |
| D-COD-6 | ما مصدر الحقيقة للدرجة الوظيفية بين MDM وPayroll؟ | رمز الدرجة في MDM هو المصدر، وPayroll يملك المبالغ فقط ويربط `PayGrade` برمز MDM | G-COD-13، G-COD-14 |
| D-COD-7 | هل تُنشئ الجهة رموزاً محلية للدرجة أو التصنيف، ومن يعتمد تغيير المرجع الوطني؟ | الدرجة وطنية فقط (من نظام العاملين). التصنيف يقبل رموزاً فرعية للجهة باعتماد مركزي. وتغيير المرجع الوطني باعتماد ثنائي | G-COD-12، G-COD-14 |
| D-COD-8 | أي سنة يحملها رقم طلب الشاغر والمنصب: سنة التقويم أم سنة الموازنة؟ وبأي منطقة زمنية؟ | سنة الموازنة حين تكون جزءاً من النطاق، وتوقيت `Asia/Damascus` | G-COD-03، G-COD-19 |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 0 (P0)   COD-A (ربط الترميز S2S في الإنتاج)  ║  COD-B (حل السياسات وتصحيحها)
الموجة 1 (P1)   COD-C (دورة حياة الإصدار + سجل القرارات)  ║  COD-D (حوكمة الهيكل وسلامة التاريخ)  ║  COD-F (حوكمة MDM ومزامنتها)
الموجة 2        COD-E (نسخ القراءة + as-of + إغلاق/دمج/تقسيم)  ║  COD-H (إدارة السياسات والتغطية)
مستمر           COD-G (الأدلة والوثائق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة الموجة 1 |
|-------|---------|---------|----------|-----------|-------------------|--------------|
| COD-A | ربط توليد الأرقام بين الخدمات في الإنتاج | G-COD-01، G-COD-06 | P0 | — | 4–5 | [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (مع G-IAM-03 وG-POL-01 والشحن) |
| COD-B | حل السياسات لكل جهة وتصحيح التعريفات | G-COD-02، G-COD-03، G-COD-05، G-COD-19 | P0 | D-COD-1، D-COD-3، D-COD-8 | 7–9 | G-COD-02/03 ← [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md) (مع G-POL-05)؛ G-COD-05 ← [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md)؛ G-COD-19 ← الموجة 2 |
| COD-C | دورة حياة الإصدار وسجل القرارات الموحّد | G-COD-04، G-COD-07 | P1 | COD-B، D-COD-2 | 7–9 | الموجة 2 |
| COD-D | حوكمة تغيير الهيكل وسلامة التاريخ | G-COD-08، G-COD-09، G-COD-10 | P1 | D-COD-4، D-COD-5 | 9–10 | الموجة 2 |
| COD-E | مزامنة الهيكل وas-of والإغلاق/الدمج/التقسيم | G-COD-11، G-COD-15، G-COD-16 | P1/P2 | COD-D | 9–10 | الموجة 2 |
| COD-F | حوكمة MDM ومزامنتها والتكرار | G-COD-12، G-COD-13، G-COD-14، G-COD-18 | P1/P2 | D-COD-6، D-COD-7 | 8–10 | الموجة 2 |
| COD-H | إدارة سياسات الترميز وتوسيع التغطية | G-COD-17 | P2 | COD-B، COD-C | 5–6 | الموجة 2 |
| COD-G | الأدلة الحية وتصحيح الوثائق | G-COD-20 | P1 (مستمر) | كل الخطط | 5–6 | AC-13 ← [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md)؛ الباقي الموجة 2 |
| **المجموع** | | | | | **≈ 54–65** | |

**ربط الموجة 1 (2026-10-30):** خطتا الموجة 1 من هذه الدراسة هما [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (INT، يجمع COD-A مع G-IAM-03 وG-POL-01 وشحن الحزمة لـ G-MOV-02/G-SEP-02) و[PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md) (TEN، يجمع جزء COD-B مع G-POL-05 وقاعدة رؤية وطنية تعمل تحت RLS، ومعه AC-13). وG-COD-05 في [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md). أما COD-C وCOD-D وCOD-E وCOD-F وCOD-H وبقية COD-G وG-COD-19 فتبقى بحروفها في **الموجة 2**.

**COD-A — ربط توليد الأرقام بين الخدمات في الإنتاج (4–5 أيام).**
- `Integration__HrCoreBaseUrl` والنطاق `hr-core.workflow` لـ HR.Contracts وIDCard وRecords.Archive وIT.Infra وCompliance في `docker-compose.services.yml` و`production.yml` وk8s (`hr-app-config` أو بيئة كل نشر)، وفي استيراد Keycloak الإنتاجي.
- فشل الإقلاع خارج Development حين يغيب العنوان، وفحص جاهزية يعاين رمزاً.
- العميل يرمي استثناءً عابراً بدل `null`، ومستهلكات الأرشيف السبعة تعيد المحاولة ثم تنقل الرسالة إلى `_error`.
- اختبار معماري: كل `AddHrCodeGeneration` يقابله مفتاح ونطاق في كل ملف نشر.
- القبول:
  - **AC-COD-Aa:** على مكدّس `production.yml`: إجازة، وقرار تعيين، وبطاقة، وجهاز، وقضية، كلها مرقّمة.
  - **AC-COD-Ab:** غياب العنوان يُفشل الإقلاع.
  - **AC-COD-Ac:** HR.Core متوقف فتبقى رسالة الأرشيف في `_error` ثم تُعالَج مرة واحدة.

**COD-B — حل السياسات لكل جهة وتصحيح التعريفات (7–9 أيام).**
- حل القالب بأولوية «جهة ثم وطني» عبر `IgnoreTenantFilterForSystem`، ورفض الاحتياط في Production.
- مستأجر الطلب المجهول من الإعلان.
- مدقق السياسة: حقول النطاق داخل الرقم، والتسلسل، والطول مقابل العمود، والتوافق مع فهرس الكيان.
- تصحيح سياسات `CostCenter` و`FundedPosition` و`RecruitmentRequisition`، واختيار `PolicyKey` الحتمي.
- دالة واحدة لمفتاح النطاق يشترك فيها المولّد والتهيئة.
- تجاهل `TenantId` من الجسم، وقصر `generate` على حسابات الخدمة.
- سنة بتوقيت الجهة.
- سكربت كشف الأرقام بلا بادئة.
- القبول:
  - **AC-COD-Ba:** جهة غير افتراضية تصدر `REQ-`/`OFF-`/`DEC-APPT-`/`CIT-`.
  - **AC-COD-Bb:** منصبان في وحدة، ومنصب في سنة موازنة تالية، وطلب لموازنة السنة القادمة، كلها بلا تصادم وفي المستأجرين.
  - **AC-COD-Bc:** سياسة نطاقها خارج الرقم تُرفض.
  - **AC-COD-Bd:** `TenantId` غريب لا يغيّر عدّاد جهة أخرى.

**COD-C — دورة حياة الإصدار وسجل القرارات الموحّد (7–9 أيام).**
- `CodeIssuance` بـ `EntityType/EntityId/Status/VoidReason`، والإصدار داخل معاملة الكيان في HR.Core.
- للخدمات الأخرى: `Reserve/Confirm/Void` بمفتاح idempotency، ومهمة لإبطال الحجوزات اليتيمة.
- تقرير فجوات.
- كل قرار داخلي يُرقَّم عبر COD بنوعه، وإلغاء `AUTO-`، ومنع تعديل الرقم، وفهرس فريد للمراجع الخارجية.
- القبول:
  - **AC-COD-Ca:** فشل الحفظ بعد التوليد يترك الرقم «مبطلاً» بسببه.
  - **AC-COD-Cb:** تكرار الطلب بالمفتاح نفسه يعيد الرقم نفسه.
  - **AC-COD-Cc:** قرار عام وقرار تغيير وظيفي متتاليان في السجل.
  - **AC-COD-Cd:** تعديل رقم قرار صادر مرفوض.

**COD-D — حوكمة تغيير الهيكل وسلامة التاريخ (9–10 أيام).**
- `OrgStructureChangeRequest` بمسار اعتماد ومرجع قرار وسند وعرض أثر وإلغاء قبل النفاذ.
- النفاذ المستقبلي مجدول بمهمة، والتاريخ الرجعي مرفوض إلا بعملية تصحيح.
- معاملة واحدة للصف الحي والنسخة والحدث.
- فهرس فريد للنسخة المفتوحة، و`xmin` على `OrgUnit`/`FundedPosition`.
- إصلاح ترتيب `ParentId` في `UpdateUnit` وفحص نطاق الأب الجديد.
- تطبيق القواعد الزمنية نفسها على طلبات الملاك.
- القبول:
  - **AC-COD-Da:** نقل وحدة باعتماد خطوتين، ولا تغيير قبل الاعتماد.
  - **AC-COD-Db:** تغيير نافذ غداً لا يظهر اليوم ويظهر في موعده.
  - **AC-COD-Dc:** تاريخ رجعي مرفوض، وas-of السابق ثابت (AC-14).
  - **AC-COD-Dd:** نقل دوري مرفوض والقاعدة سليمة بعد إعادة التشغيل.

**COD-E — مزامنة الهيكل وas-of والإغلاق/الدمج/التقسيم (9–10 أيام).**
- `IOrgUnitChanged` v3 بـ `Version` و`ValidFrom`، ومستهلكون يرفضون النسخة الأقدم، وإعادة مزامنة إدارية لكل مستأجر عبر Outbox.
- `Close`، ثم `Merge`/`Split` كطلب تغيير واحد ينقل الموظفين والمناصب بنسخ مؤرخة.
- خط زمن الوحدة وشجرة as-of وتصدير.
- القبول:
  - **AC-COD-Ea:** رسالتان معكوستان تُبقيان الأحدث في Payroll.
  - **AC-COD-Eb:** دمج وحدتين، وas-of قبل الدمج يعيدهما.
  - **AC-COD-Ec:** إغلاق وحدة فيها موظف مرفوض.
  - **AC-COD-Ed:** شجرة بتاريخ سابق تُظهر الأسماء القديمة.

**COD-F — حوكمة MDM ومزامنتها والتكرار (8–10 أيام).**
- `ReferenceDataController` يمر عبر خدمة الإدارة بالقواعد نفسها، مع اعتماد ثنائي للمرجع الوطني.
- سجل مستهلكين للأثر يشمل المناصب والعروض وPayroll وContracts.
- نشر `IReferenceDataChanged` عبر Outbox، ومستهلك في Payroll يربط `PayGrade` بـ MDM.
- دمج موحّد بين نسخة الجهة والمنصة.
- كشف تكرار دوري، و`Merged` نتيجة للدمج فقط.
- القبول:
  - **AC-COD-Fa:** PUT تعطيل درجة مستخدمة يعيد 409.
  - **AC-COD-Fb:** درجة على منصب واحد لا تُعطَّل.
  - **AC-COD-Fc:** تعديل اسم وطني يصل إلى Payroll.
  - **AC-COD-Fd:** `PayGrade` برمز مجهول مرفوض.
  - **AC-COD-Fe:** حالة تكرار تُنشأ آلياً.

**COD-H — إدارة سياسات الترميز وتوسيع التغطية (5–6 أيام).**
- شاشة نسخ السياسات «مسودة ← اعتماد ← نشر» بتاريخ نفاذ مع المدقق.
- مقطع `AgencyCode`.
- أرقام للعقود وقرارات النقل والترفيع والندب والتأديب.
- القبول:
  - **AC-COD-Ha:** نسخة جديدة تؤثر بعد تاريخ نفاذها فقط.
  - **AC-COD-Hb:** نسخة لا تجتاز المدقق لا تُنشر.
  - **AC-COD-Hc:** قرار نقل يحمل رقماً صادراً.

**COD-G — الأدلة الحية وتصحيح الوثائق (5–6 أيام، مستمر).**
- إعادة كتابة AC-13: أنواع ونطاقات وجهتان وفحص DB وصيغة، و`[RequiredAcceptance]`.
- توسيع AC-14 بحالتي المستقبل والرجعي.
- كتابة AC-EST-07..09، وتشغيل AC-EST-13..18.
- اختبار تكامل بالخدمة الحقيقية للمنصب على المستأجر الافتراضي.
- إضافة شخصية في المستأجر الافتراضي إلى كتالوج القبول.
- تصحيح COD-001 وORG-001 وMDM-001 وPLAN-04 وبطاقات `completion-backlog/09`.
- القبول:
  - **AC-COD-Ga:** مجلدات أدلة كاملة لكل ما سبق.
  - **AC-COD-Gb:** لا اختبار في المجال يستبدل `IFundedPositionCodeService` إلا اختبارات الوحدة المعلَّمة.

---

## 7. معيار «اكتمال الترميز والهيكل والمرجع» المقترح

- [ ] **1. لا رقم بلا صيغته في أي مستأجر.** كل نوع له سياسة منشورة تجتاز المدقق. وعلى المستأجر الافتراضي وعلى جهة: منصبان في وحدة، ومنصب في سنة موازنة تالية، وطلب شاغر لموازنة السنة القادمة، كلها تنجح بأرقام مسبوقة بالبادئة، ولا يوجد في القاعدة رمز يبدأ بـ `-`.
- [ ] **2. كل الخدمات تحصل على أرقامها بإعدادات الإنتاج الفعلية.** على مكدّس `docker-compose.production.yml` (لا ملف القبول) وعلى k8s: إجازة، وقرار تعيين، وبطاقة، وجهاز، وقضية، ومرجع أرشيف. ورسالة الأرشيف لا تضيع حين يتوقف HR.Core.
- [ ] **3. كل رقم صادر مرتبط بكيان أو بإبطال مسبَّب.** تقرير فجوات سجل القرارات يعطي صفر فجوات غير مفسّرة بعد سيناريو فشل متعمد.
- [ ] **4. الهيكل يتغير بقرار وفي موعده فقط.** نقل وحدة يمر بالاعتماد ومرجع القرار. والتغيير المستقبلي لا يظهر قبل نفاذه. والرجعي مرفوض. وas-of لتاريخ سابق ثابت بعد أي تعديل لاحق (AC-14 = Pass بأدلة).
- [ ] **5. المرجع الوطني محكوم ومتزامن.** الواجهة البرمجية تطبق قواعد الشاشة. وتعطيل درجة يحملها منصب واحد مرفوض. وتعديل وطني يصل إلى Payroll. وأدلة AC-13 وAC-EST-07..09 وAC-EST-13..18 كاملة.
