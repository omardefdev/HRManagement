# PLAN-62 — قطع الوصول عند الفصل وتزويد الهوية وإخلاء حقيقي للعهدة (SEP-C)

**Scope:** SEP / FUN-002 — [دراسة الفجوات](../FUN_002_SEPARATION_GAP_STUDY.md): G-SEP-03، G-SEP-06، وجزء من G-SEP-13 (طرف HR.Health، و`SchemaVersion`). ويحتوي من [دراسة FUN-005](../FUN_005_IDENTITY_ASSETS_IT_GAP_STUDY.md): G-IDA-01 (التعطيل والتزويد في Keycloak)، وجزء Auth من G-IDA-07، والدور الافتراضي من G-IDA-09. ومن [دراسة IAM](../IAM_001_SCOPE_GAP_STUDY.md): جزء «المغادر» من G-IAM-11  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-39 (realm إنتاجي قابل لإعادة الإنتاج، ومنه عميل `directory-sync`)، PLAN-60 (أنماط بنود القائمة ومستهلك الإقرار)  
**Related cards:** FUN-002، FUN-005، IAM-005 (`IAM_005_IDENTITY_LIFECYCLE.md`)، DOM-001، PLAN-12 (العهدة والدليل)

## Goal

1. **قطع الوصول فعلياً:** عند تنفيذ الفصل يُعطَّل المستخدم في Keycloak، وتُنهى جلساته العادية وغير المتصلة، وتُسحب أدواره. ويُسجَّل الدليل. الفشل يوقف الرحلة في تدخل بشري، ولا نجاح صامت.
2. **التزويد عند التعيين:** يُنشأ مستخدم Keycloak مربوطاً بـ `KeycloakUserId` بدور `hr-employee` وحده. والفشل ظاهر وقابل لإعادة المحاولة حقاً.
3. **إخلاء حقيقي:** لا «إرجاع» آلي للعهدة أو الأجهزة. الأطراف تُبلغ عن البنود المفتوحة، وتُغلق بالمسارات القائمة بدليل. وحالة براءة الذمة ظاهرة على سجل الفصل.
4. **أطراف كاملة:** HR.Health طرف إلزامي يلغي التأمين. وحدث `ISeparationCompleted` يصل فعلاً إلى مستهلكيه.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-IDA-5 مزامنة Keycloak في الإنتاج والدور الافتراضي | مفعّلة إلزامياً في `Production`، والدور الافتراضي `hr-employee` فقط. والصحة حمراء إن تعطلت | §1، §2 |
| D-SEP-5 / D-IDA-1 هل تمنع العهدة غير المعادة اكتمال الفصل؟ | لا. الفصل (الحالة والوصول والراتب) يكتمل في تاريخه. البنود المفتوحة تُبقي «براءة الذمة» مفتوحة، وتحجز صرف التسوية (PLAN-64) حتى الإرجاع أو قرار فقد/تحميل | §3 |
| مهلة إنهاء الجلسات | فوري عند تنفيذ الطرف. ومهمة مصالحة يومية تعيد التعطيل لأي انحراف | §1 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-62-sep-access-cutoff-clearance` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test … --filter "PartyAck\|SeparationCompleted\|Keycloak\|Iam005\|Fun005\|Sep060"` | أخضر أو موثق في `reports/plan62-baseline.md` |
| 0.3 | تحقق PLAN-39 | عميل `directory-sync` في realm الإنتاج والقبول بأدوار `realm-management`: `manage-users`، `view-users`، `query-users` | موجود؛ وإلا يُضاف إلى القالب الإنتاجي في هذه الخطة |
| 0.4 | جرد | على القبول: `AuthUsers` بلا `KeycloakUserId`، والمستخدمون غير النشطين محلياً والمفعّلون في Keycloak، و`AssetAssignments` المُعلَّمة `ReturnedAt` بلا `Status=Returned` | الأرقام في التقرير |

---

## In scope

### 1. قطع الوصول في Keycloak عند الفصل (G-SEP-03، G-IDA-01، G-IAM-11 المغادر)

**المشكلة:**
- طرف الدليل يعطّل علماً محلياً فقط ([Auth PartyAckConsumers.cs:12-22](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).
- شبكتا الأمان تخرجان قبل Keycloak: [SeparationCompletedConsumer.cs:87-89](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs) (قبل `:103`)، و[EmployeeStatusChangedConsumer.cs:95-97](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeStatusChangedConsumer.cs) (قبل `:113`).
- المزامنة معطلة افتراضياً ([appsettings.json:23-29](../../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json))، و`Disabled()` يُعد نجاحاً ([IKeycloakUserProvisioningService.cs:30](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/IKeycloakUserProvisioningService.cs)).
- الأدوار لا تُسحب (الخدمة تضيف فقط، [KeycloakUserProvisioningService.cs:174-175](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs)).
- لا فحص عند الدخول ([KeycloakExtensions.cs:99-106](../../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs)).
- التعويض يعيد تفعيل كل حساب معطّل للموظف ([Auth PartyAckConsumers.cs:24-34](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| حقول `AuthUser` | `Auth.Directory.Domain/Entities/AuthUser.cs` + ترحيل `SEP062_IdentityLifecycle` | `IdpProvisioningStatus` (`Pending/Succeeded/Failed`)، `IdpProvisioningError`، `IdpProvisionedAt`، `IdpDisabledAt`، `IdpDisabledBySeparationId`، `IdpRemovedRolesJson` |
| توسيع `IKeycloakUserProvisioningService` | `Auth.Directory.Infrastructure/Keycloak/` | `FindUserIdByUsernameAsync`، `GetRealmRolesAsync`، `RemoveRealmRolesAsync`، `LogoutAsync` (عام؛ اليوم `TryRevokeUserSessionsAsync` خاص `:223-245`)، `RevokeOfflineSessionsAsync` (حذف الموافقات لكل عميل)، `IsUserEnabledAsync`. والنتيجة تميّز `Disabled` عن `Succeeded` صراحة (`Outcome` بدل علمين) |
| `IdentityLeaverService` | `Auth.Directory.Infrastructure/Services/IdentityLeaverService.cs` | `CutOffAsync(employeeId, separationRecordId)` لكل `AuthUser` للموظف: ربط `KeycloakUserId` بالبحث إن غاب ← لقطة الأدوار ← سحبها (عدا `default-roles-*`) ← `enabled=false` ← `logout` ← إلغاء الجلسات غير المتصلة ← حفظ الحقول و`IsActive=false`. idempotent: `IdpDisabledAt` مضبوط وKeycloak معطّل ← لا شيء |
| `SeparationPartyAckConsumer` (Auth) | الملف القائم | التنفيذ = `CutOffAsync`. والدليل `auth:idp-disabled:{n}`. أي فشل أو مزامنة معطلة ← استثناء ← `Faulted` (القاعدة في `PartyAckConsumerBase`) |
| التعويض (Auth) | الملف نفسه | يعيد تفعيل **فقط** من `IdpDisabledBySeparationId == id`، ويستعيد الأدوار من اللقطة، ويمسح الحقول. الحساب المعطّل لسبب آخر يبقى معطلاً |
| شبكتا الأمان | `SeparationCompletedConsumer.cs`، `EmployeeStatusChangedConsumer.cs` | الشرط `IdpDisabledAt == null` لا العلم المحلي، ثم `CutOffAsync`. وإعادة التفعيل في `EmployeeStatusChanged` تبقى معطّلة (إعادة التعيين ← PLAN-63) |
| `IdentityLeaverReconciliationJob` | `Auth.Directory.Infrastructure/Services/` + تسجيل في `Program.cs` | يومياً (و`POST api/auth-directory/admin/leaver-reconcile` بسياسة جديدة `AuthDirectory.LeaverReconcile` = `hr-admin` أو `auth-directory`): المستخدمون `IsActive=false` بـ `IdpDisabledAt=null`، أو `IdpDisabledAt` مضبوط وKeycloak مفعّل ← إعادة التعطيل. عدّاد `identity_leaver_drift_total`، وقائمة في شاشة المستخدمين «حسابات منفصلين تحتاج معالجة» |

**القواعد:**

1. لا يُقر الطرف إلا بعد تأكيد Keycloak: `GET user` يعيد `enabled=false`، وجلسات = 0.
2. موظف بلا حساب محلي ولا مستخدم Keycloak باسم `u_{employeeId:N}` ← إقرار `auth:no-account`.
3. أي خطأ HTTP، أو مهلة، أو `Disabled` مع `Keycloak:DirectorySync:Required=true`، أو 401 من عميل الإدارة ← `Faulted` برسالة عربية تذكر السبب.
4. لا قراءة لـ `AuthUser.IsActive` في البوابة ولا الخدمات. المصدر هو Keycloak. وإنهاء الجلسات يغلق الوصول عند أول تحديث للرمز. مدد الجلسة والرمز يملكها IAM (G-IAM-11، الموجة 2).

### 2. التزويد عند التعيين والدور الافتراضي (G-IDA-01 التزويد، G-IDA-07 جزء Auth، G-IDA-09 الدور الافتراضي)

**المشكلة:**
- `EmployeeCreatedConsumer` يخرج مبكراً إن وُجد `AuthUser` ([EmployeeCreatedConsumer.cs:41](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeCreatedConsumer.cs))، فلا تُعاد المحاولة أبداً.
- فشل Keycloak يُسجَّل تحذيراً فقط (`:83`). ورد 409 يُعد نجاحاً بلا معرّف، فلا يُربط `KeycloakUserId`.
- `SyncFromKeycloak` يعرض «تمت مزامنة المستخدم» والمزامنة معطلة ([AuthUsersController.cs:269-335](../../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs)).
- طرف التعيين يفحص السجل المحلي فقط ([Auth PartyAckConsumers.cs:40-52](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).
- الدور الافتراضي `hr-core` ([appsettings.json:27-28](../../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `IdentityProvisioningService` | `Auth.Directory.Infrastructure/Services/IdentityProvisioningService.cs` | `EnsureAsync(authUser)`: إنشاء أو ربط (409 ← بحث بالاسم)، ثم إسناد `DefaultRealmRoles`، ثم ضبط الحالة والخطأ. الخصائص بأسماء مُخطِّط الرمز (`employee_id` كما صحّحها PLAN-50، و`tenant_id`، و`scope_org_unit_id`) |
| `EmployeeCreatedConsumer` | الملف القائم | يُنشئ `AuthUser` بحالة `Pending` ثم `EnsureAsync`. المحاولة الثانية تتابع إن لم تكن `Succeeded` (لا خروج مبكر). الفشل ← `Failed` محفوظ، ثم استثناء لإعادة المحاولة وفق سياسة MassTransit |
| `RetryProvision` / `SyncFromKeycloak` | `AuthUsersController.cs` | تستدعي `EnsureAsync` فعلاً. و`Disabled` ← رسالة «المزامنة معطلة» لا نجاح |
| طرف التعيين (Auth) | `Auth PartyAckConsumers.cs:40-52` | يشترط `IdpProvisioningStatus=Succeeded` و`KeycloakUserId ≠ null`، والدليل `auth:kc:{KeycloakUserId}` |
| الدور الافتراضي | `appsettings.json` | `DefaultRealmRoles: ["hr-employee"]`، وتحقق إقلاع: كل دور فيها ضمن `Keycloak:DirectorySync:AllowedDefaultRoles` (افتراضياً `["hr-employee"]`)، وإلا فشل الإقلاع |
| تفعيل الإنتاج | `Auth.Directory.Web/appsettings.Production.json`، و`docker-compose.acceptance-prodlike.yml` لملف القبول | `Keycloak:DirectorySync:Enabled=true` و`Keycloak:DirectorySync:Required=true` و`AdminClientId=directory-sync`. والسر من البيئة كما اليوم |
| `KeycloakDirectorySyncHealthCheck` | `Auth.Directory.Infrastructure/Health/` | مع `Required=true`: معطّلة أو فشل رمز العميل ← `Unhealthy` (يظهر في `/health/ready`) |

**القواعد:** قائمة «فشل التزويد» تعرض `Failed` مع الخطأ. وإعادة المحاولة الناجحة تنقل الحالة إلى `Succeeded`.

### 3. إخلاء حقيقي للعهدة والأجهزة وبراءة الذمة (G-SEP-06)

**المشكلة:**
- طرف Admin.Logistics «يُرجع» كل عهدة مفتوحة آلياً بلا دليل، ويحوّل الأصل إلى `InStorage` ([Admin.Logistics PartyAckConsumers.cs:13-32](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)). بينما الإرجاع اليدوي للمميز يشترط دليلاً ([AssetsController.cs:509](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)).
- المستهلك اللاحق يفعل الشيء نفسه ([Admin.Logistics SeparationCompletedConsumer.cs:63-75](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/SeparationCompletedConsumer.cs)).
- طرف IT يفك ربط الأجهزة ([IT PartyAckConsumers.cs:22-26](../../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs)، [IT SeparationCompletedConsumer.cs:50](../../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/SeparationCompletedConsumer.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ISeparationClearanceItemsReported` / `ISeparationClearanceItemClosed` | `HRManagement.MessageContracts/Events/` | البلاغ: `SeparationRecordId`، `Service`، `Items[]` (`ItemType`، `ItemRef`، `Description`، `IsPrivileged`). والإغلاق: `ItemRef`، `Resolution` (`Returned`/`Lost`/`WrittenOff`)، `EvidenceRef`، `ClosedByUserId`، `LiabilityAmount?` |
| طرف Admin.Logistics | الملف القائم | لا `ReturnedAt` ولا تغيير حالة أصل. يضبط `AssetAssignment.PendingSeparationRecordId`، وينشر البلاغ، ثم يُقر `admin-logistics:open={n}` (أو `:clear`) |
| مستهلك اكتمال Admin.Logistics | `SeparationCompletedConsumer.cs` | يُحذف الإرجاع الآلي. يصبح فحصاً: عهدة مفتوحة بلا `PendingSeparationRecordId` ← تُعلَّم وتُبلَّغ |
| إغلاق بند العهدة | `AssetsController.ReturnAssignment` (`:477`)، ومستهلك اعتماد الفقد (`AssetLossWorkflowConsumers`) | عهدة بـ `PendingSeparationRecordId` ← بعد الإرجاع بالدليل (قاعدة `:509` نفسها) يُنشر الإغلاق `Returned`. وبعد اعتماد الفقد يُنشر `Lost` مع مرجع البلاغ والقيمة الدفترية |
| طرف IT | الملف القائم + `IT SeparationCompletedConsumer.cs` | سحب الصلاحيات يبقى. الأجهزة لا يُفك ربطها: `UserDevice.PendingSeparationRecordId` + بلاغ |
| `UserDevicesController.ReturnFromLeaver` | `IT.Infra.Web/Controllers/UserDevicesController.cs` | POST بسياسة صريحة `[Authorize(Policy = ModuleItInfra)]` على الإجراء، بدليل إلزامي (مرجع أو ملف): `AssignedTo=null` وإغلاق `Returned` |
| `SeparationClearanceItem` | `HR.Contracts.Domain/Entities/` + ترحيل `SEP062_ClearanceItems` | صف لكل بند: الخدمة، النوع، المرجع، الوصف، الحالة، الدليل، الإغلاق. فهرس فريد `(SeparationRecordId, Service, ItemRef)` |
| `SeparationClearanceConsumer` | `HR.Contracts.Infrastructure/Consumers/` | يحفظ البلاغ والإغلاق (idempotent). ويحسب `SeparationRecord.ClearanceStatus` (`NotStarted/Open/Closed`)، وينشر `ISeparationClearanceStatusChanged` عبر Outbox (تستهلكه PLAN-64) |
| تعديل قاعدة PLAN-60 | `SeparationPartyAckChecklistConsumer` | بندا `AssetsReturned` و`ItAccessRevoked` يكتملان عند إغلاق كل بنود خدمتهما (لا عند الإقرار وحده). والإقرار مع بنود مفتوحة يعرض «بنود مفتوحة: n» |
| الواجهة | `SeparationRecords/Details.cshtml` | جدول «براءة الذمة»: البند، الخدمة، الحالة، الدليل، رابط إلى شاشة الخدمة المالكة. HR يقرأ ولا يغلق |

**القواعد:**

1. لا يغيّر أي مستهلك فصل حالة أصل أو جهاز أو تاريخ إرجاعه. الإغلاق فقط بمسار المالك وبدليل.
2. `ClearanceStatus=Closed` حين تُغلق كل البنود، أو حين لا بنود في كل الخدمات بعد إقرارها.
3. الفصل يكتمل (`Completed`) ولو بقيت البراءة مفتوحة، وفق D-SEP-5.
4. HR لا يملك إغلاق بند ولا استثناءه. الاستثناء = قرار فقد أو شطب في الخدمة المالكة.

### 4. HR.Health طرفاً إلزامياً، وإصدار الحدث (G-SEP-13 جزئياً)

**المشكلة:**
- `MandatoryParties` بلا HR.Health ([SeparationStateMachine.cs:39-49](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs))، ولا قسم `Sagas` في أي إعداد. فمستهلك التأمين لا يُستدعى ([HR.Health PartyAckConsumers.cs:25](../../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs)).
- `ISeparationCompleted.SchemaVersion => 3` ([ISeparationCompleted.cs:22](../../src/Shared/HRManagement.MessageContracts/Events/ISeparationCompleted.cs)). والـ saga لا تضبطه ([SeparationStateMachine.cs:263-276](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)). وأربعة مستهلكين يقبلون حتى 2. والاختبارات ترسل 1 ([PeripheralConsumerIntegrationTests.cs:81](../../src/Tests/HRManagement.IntegrationTests/PeripheralConsumerIntegrationTests.cs)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| الأطراف | `SeparationSagaOptions` + `HR.Contracts.Web/appsettings.json` (`Sagas:Separation:MandatoryParties`) | إضافة `ProcessParties.Health` صراحة في الشيفرة والإعداد |
| بند `InsuranceClosed` | `SeparationStep` (يُضاف في النهاية) + القوالب | `PartyAck` باسم `HR.Health` |
| الإصدار | `SeparationStateMachine.PublishFinalCompleted` | `SchemaVersion = 3` صراحة |
| المستهلكون | Admin.Logistics `:51`، IDCard `:17`، IT.Infra `:27`، Auth.Directory `:59` | `supportedMax: 3` |
| اختبار عقد حقيقي | `IntegrationTests/Sep062SeparationCompletedVersionTests.cs` | تشغيل الـ saga الحقيقية في `ITestHarness` حتى `ISeparationCompleted`، ثم قراءة `SchemaVersion` عند مستهلك فعلي، وتأكيد عدم التخطي. ويُحسم بند «يحتاج تحقق» في الدراسة |
| تصحيح الاختبارات القائمة | `PeripheralConsumerIntegrationTests.cs` | `SchemaVersion = 3` كما يرسله المنتج |

---

## Out of scope

| البند | الخطة |
|-------|-------|
| تعويض أطراف IDCard وIT وAdmin.Logistics وPayroll وHR.Core المحصور بعلامة، وإعادة التفعيل عند إعادة التعيين | PLAN-63 |
| مدد جلسة البوابة و`offline_access` و`employee_id` خاصية مُدارة (بقية G-IAM-11) | IAM (الموجة 2) |
| منح الأدوار بمعتمد ثانٍ حقيقي وإعادة تسمية الأدوار (بقية G-IDA-09)، وإقرارات IDCard/IT في التعيين (بقية G-IDA-07) | IDA (الموجة 2) |
| حجز صرف التسوية حتى البراءة، وخصم قيمة الفقد | PLAN-64 |
| شهادة براءة الذمة PDF | الموجة 2 (G-SEP-16) |
| realm الإنتاج نفسه وأسرار العملاء | PLAN-39 |

## Acceptance scenarios

على الملف الشبيه بالإنتاج مع Keycloak القبول (المزامنة مفعّلة). الشخصيات: `a-leaver` (`hr-employee`، له حساب Keycloak، مسند له حاسوب مميز وجهاز IT وبطاقة تأمين)، `a-contracts-checker`، `a-logistics-maker`، `a-it`، `a-directory-maker`، ومستخدم قبول ثانٍ `a-leaver-sec` (جديد) معطّل أمنياً قبل الاختبار. والمغادر نسخة جديدة من القالب لكل سيناريو بـ `AcceptanceLeaverFixture` (PLAN-60)، تُوسَّع هنا بالعهدة والجهاز والتأمين، وحسابها يُزوَّد بمسار التعيين (§2) وتُضبط كلمة مروره في الاختبار عبر واجهة إدارة Keycloak بعميل `directory-sync`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-SEP-62a | تنفيذ فصل `a-leaver` (مسار PLAN-60) ← خلال دقيقتين: Keycloak `enabled=false`، لا أدوار realm غير الافتراضية، الجلسات وغير المتصلة = 0، `IdpDisabledAt` مضبوط. ودخول `a-leaver` بكلمة مروره يُرفض. والكعكة القديمة تُرفض عند أول تحديث للرمز (الزمن مسجّل في الدليل) | — |
| AC-SEP-62b | بعد إعادة تشغيل Keycloak واستئناف التدخل ← يكتمل الطرف | Keycloak متوقف أثناء التنفيذ ← طرف Auth `Faulted` ← `AwaitingIntervention`، ولا `ISeparationCompleted` |
| AC-SEP-62c | — | إعادة إنشاء `auth-directory-web` بـ `Keycloak__DirectorySync__Enabled=false` (و`Required=true` من الملف الشبيه) ← `/health/ready` غير سليم، وطرف الفصل `Faulted` |
| AC-SEP-62d | تعيين موظف جديد ← مستخدم Keycloak مربوط بـ `KeycloakUserId` وبدور `hr-employee` وحده، وطرف التعيين يُقر `auth:kc:<id>` | Keycloak متوقف عند التعيين ← `Failed` ظاهر في القائمة، وsaga التعيين لا تكتمل. ثم إعادة المحاولة بعد العودة ← `Succeeded` |
| AC-SEP-62e | بعد التنفيذ: براءة الذمة `Open` ببندين (حاسوب، جهاز). `a-logistics-maker` يُرجع الحاسوب بدليل، و`a-it` يستلم الجهاز بدليل ← البراءة `Closed` و`AssetsReturned`/`ItAccessRevoked` مكتملان | الأصل يبقى `InUse` وغير مُرجع بعد التنفيذ. و`a-contracts-checker` لا يجد أداة لإغلاق البند. وإرجاع المميز بلا دليل ← رفض |
| AC-SEP-62f | بطاقة تأمين `a-leaver` ملغاة بعلامة الفصل، و`InsuranceClosed` مكتمل | — |
| AC-SEP-62g | إلغاء التنفيذ عبر التدخل (طرف Auth وحده محل الفحص هنا) ← `a-leaver` يعود مفعّلاً بأدواره | `a-leaver-sec` (المعطّل أمنياً) يبقى معطّلاً بعد التعويض |
| AC-SEP-62h | مهمة المصالحة: مستخدم أُعيد تفعيله يدوياً في Keycloak بعد الفصل ← يُعطَّل مجدداً ويزيد العدّاد | — |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Sep062|IdentityLeaver|IdentityProvisioning|KeycloakDirectorySync|ClearanceItem"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Sep062|PeripheralConsumer|Int003|Fun002"
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcSep62"
# 62b: أوقف حاوية Keycloak في مشروع hrm-acceptance أثناء التنفيذ (docker compose -p hrm-acceptance stop keycloak)،
#      ثم شغّلها (start keycloak) واستأنف عبر POST api/process-interventions/{id}/resume
```

**اختبارات جديدة:**
- **Unit:**
  - `Sep062IdentityLeaverServiceTests`: محاكاة HTTP لـ Keycloak، تشمل 401 و409 و`Disabled` في Production والتطابق.
  - `Sep062ProvisioningTests`.
  - `Sep062DefaultRoleValidationTests`.
  - `Sep062ClearanceStatusTests`.
- **Integration على PostgreSQL** (`PostgreSqlFixture`):
  - `Sep062ClearancePostgresTests`: بلاغان متزامنان للبند نفسه ← صف واحد. وإغلاق قبل البلاغ (ترتيب معكوس) ← حالة صحيحة.
  - `Sep062SeparationCompletedVersionTests`: الـ saga الحقيقية في الـ harness.

## Definition of done

- لا مسار يُقر طرف Auth في الفصل دون تأكيد Keycloak.
- لا مستهلك يُرجع عهدة أو يفك جهازاً آلياً.
- HR.Health طرف إلزامي، والمستهلكون الأربعة يعالجون `ISeparationCompleted` كما تنشره الـ saga.
- `Production` (و`Required=true`) لا يقلع سليماً بالمزامنة معطلة، والدور الافتراضي `hr-employee`.
- العمل في commits محلية: لا دفع (PLAN-35 للمالك)، ولا أسرار (سر `directory-sync` من البيئة فقط) ولا `global.json`.
- AC-SEP-62a..h = Pass، ومجلد `acceptance-evidence/AC-SEP-62/`.
- سطور «الحالة» في الدراسات محدّثة: G-SEP-03، وG-SEP-06، وG-SEP-13 (الجزء المنفّذ)، وG-IDA-01، وجزء G-IDA-07/09، وجزء G-IAM-11.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md` |
| Auth.Directory | `IdentityLeaverService.cs`، `IdentityProvisioningService.cs`، `IdentityLeaverReconciliationJob.cs`، `KeycloakDirectorySyncHealthCheck.cs`، توسيع `IKeycloakUserProvisioningService`، المستهلكون، `AuthUsersController`، ترحيل `SEP062_IdentityLifecycle`، `appsettings*.json` |
| Admin.Logistics / IT | الأطراف والمستهلكان، `ReturnAssignment` وإغلاق الفقد، `UserDevicesController.ReturnFromLeaver`، ترحيلا `PendingSeparationRecordId` |
| HR.Contracts | `SeparationClearanceItem`، `SeparationClearanceConsumer`، `ClearanceStatus`، `InsuranceClosed`، الأطراف، الواجهة، ترحيل `SEP062_ClearanceItems` |
| Contracts | `ISeparationClearanceItemsReported`، `ISeparationClearanceItemClosed`، `ISeparationClearanceStatusChanged`، وصفوفها في `docs/EVENT_PUBLISHER_INVENTORY.md` (شرط R4 في حارس PLAN-40؛ والنشر من المستهلكين عبر outbox المستهلك) |
| Compose | `docker-compose.acceptance-prodlike.yml` (`Keycloak__DirectorySync__Enabled=true`) |
| Tests | أعلاه، و`AcSep62AccessClearanceTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، والشخصية `a-leaver-sec` في `personas.json` و`AcceptancePersonaCatalog.cs` (+1 على العدد وقت التنفيذ)، وتوسيع `AcceptanceLeaverFixture` بالعهدة والجهاز والتأمين |
| Scripts | `scripts/sep062-validate.ps1`، `scripts/sep062-identity-drift-report.ps1` |
| Docs | `IAM_005_IDENTITY_LIFECYCLE.md` (التعطيل الفعلي والمصالحة)، `KEYCLOAK_AND_SECRETS.md` (التفعيل الإلزامي)، `FUN_005_RECONCILIATION_CHECKLIST.md` (إعادة المحاولة الحقيقية)، `FUN_002_RECONCILIATION_CHECKLIST.md` (براءة الذمة)، `PROCESS_SAGAS.md` (أطراف الفصل الثمانية)، وسطور «الحالة» |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تفعيل المزامنة يكشف حسابات قديمة بلا `KeycloakUserId` | المصالحة تربط بالاسم `u_{employeeId:N}`. والباقي في تقرير `sep062-identity-drift-report.ps1` |
| سحب الأدوار يمحو أدواراً مُسندة يدوياً لا يعرفها الدليل | اللقطة تحفظ كل الأدوار المسحوبة، والتعويض يعيدها حرفياً |
| بيئة القبول تفقد شخصيات إن طُبقت المصالحة على مستخدمين معطّلين قصداً (`negative-disabled`) | المصالحة لا تمس إلا `AuthUser` مربوطاً بموظف، وشخصيات القبول المعطلة قصداً في قائمة استثناء بالإعداد |
| أحداث البراءة تصل قبل إنشاء السجل في HR.Contracts | المستهلك ينشئ الصف عند أول حدث أياً كان ترتيبه، ويُكمل الحالة لاحقاً |

## Effort estimate

9–10 أيام عمل: قطع الوصول والتعويض والمصالحة (3)، التزويد والدور والصحة (1.5)، البراءة عبر ثلاث خدمات (2.5)، HR.Health والإصدار (0.5)، القبول مع Keycloak الحقيقي والتوثيق (2).
