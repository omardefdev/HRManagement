# دراسة الهوية والأصول وتقنية المعلومات والدليل (IDA) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** خدمات `IDCard` (تصاميم البطاقات، طلباتها، البطاقات الصادرة، الإصدار والتجديد والاستبدال عند الفقد والإبطال عند الفصل، الطباعة، الأختام الرسمية، صلاحيات الدخول) و`AdminLogistics` (الأصول والعهدة وتقارير الفقد، المخزون، أوامر التجهيز، الاستلام، الصيانة، الموردون، مسار اعتماد العهدة) و`ITInfrastructure` (أجهزة المستخدمين، تذاكر الدعم، الحوادث الأمنية، النسخ الاحتياطي، حالة التزويد وإعادة المحاولة) و`AuthDirectory` (المستخدمون، `DirectoryInternalSync`، الأدوار، سياسات الصلاحيات، ومزامنة Keycloak). ومعها المعالجة الداخلية في كل خدمة لتسليمات الموظف: التعيين (Joiner)، النقل (Mover)، الفصل (Leaver)، بأحداثها وidempotency والفشل وإعادة المحاولة والتعويض. رحلة الفصل نفسها خارج النطاق (→ SEP).  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والإعدادات والأدلة في لقطة `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق: Done وReadyForSign-off وPass.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات، فلا يوجد .NET SDK في بيئة الدراسة. كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [FUN_005_JOURNEY_MATRIX.md](FUN_005_JOURNEY_MATRIX.md) · [FUN_005_EXCEPTION_PATHS.md](FUN_005_EXCEPTION_PATHS.md) · [FUN_005_RECONCILIATION_CHECKLIST.md](FUN_005_RECONCILIATION_CHECKLIST.md) · [FUN_005_UAT_PACK.md](FUN_005_UAT_PACK.md) · [FUN_005_VERIFICATION.md](FUN_005_VERIFICATION.md) · [FUN_005_RECEIPT_LINEAGE.md](FUN_005_RECEIPT_LINEAGE.md) · [ADR-FUN-005](ADR-FUN-005-identity-assets-it-closure.md) · [ADR-FUN-005-EXT](ADR-FUN-005-EXT.md) · [PLAN-12](acceptance-plans/PLAN-12-logistics-custody.md) · [04-P3 (FUN-005)](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [10 (IAM-005)](completion-backlog/10-NATIONAL-CONTROL-SUPPLEMENT.md) · [IAM_005_IDENTITY_LIFECYCLE.md](IAM_005_IDENTITY_LIFECYCLE.md) · [EVENT_PUBLISHER_INVENTORY.md](EVENT_PUBLISHER_INVENTORY.md) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md) · [plan33-acceptance-inventory.md](../reports/plan33-acceptance-inventory.md)

---

## 1. الخلاصة

1. **الكيانات والشاشات موجودة لكل مرحلة تقريباً، وبعضها صلب.** للعهدة حائز مفتوح واحد يحرسه فهرس فريد مشروط، وتقرير الفقد يحفظ لقطة قبل/بعد. نسب الأصل إلى سجل الاستلام إلزامي، والأدلة تُسجَّل عبر FILE-002. البطاقات تُبطَل في طرف الفصل، وفي الخدمات الأربع inbox يمنع تكرار معالجة الرسالة نفسها. لكن عبارة «Done» في `FUN_005_VERIFICATION.md:13` و`04-P3:209`، و«ReadyForSign-off» في `FUN_005_UAT_PACK.md:18`، وIAM-005 «Done»، لا تطابق ما يحدث بإعدادات الإنتاج.
2. **أربعة عوائق P0:**
   - **(أ)** الموظف المفصول يبقى قادراً على الدخول وبأدواره. Keycloak لا يُعطَّل: المزامنة معطّلة في الإنتاج. وحتى عند تفعيلها يكتب طرف الفصل علماً محلياً فقط، فتخرج شبكتا الأمان قبل التعطيل. والأدوار لا تُسحب أبداً (G-IDA-01).
   - **(ب)** حلقة اعتماد العهدة المميزة وفقدها لا تُغلق: لا شاشة للمعتمد، وحدث النتيجة يُنشر دون حفظ في الـ outbox، والعهدة العالقة تحجز الأصل بلا مخرج (G-IDA-02).
   - **(ج)** إصدار البطاقة من طلبها لا ينشر `ICardIssued`، فلا يكتمل بند `IdentityIssued` في قائمة التعيين، ولا تبدأ رحلة التعيين إلا بإصدار بطاقة ثانية يدوياً (G-IDA-03).
   - **(د)** التسليم بإثبات، والفقد، والتجديد، والإبطال العاجل، وإلغاء الطلب: كلها غير قابلة للتنفيذ من الواجهة (G-IDA-04).
3. **ثغرات سلامة وحوكمة (P1، 10 فجوات، والحادية عشرة فجوة التحقق في البند 5):**
   - تُغلق العهدة تلقائياً عند الفصل والنقل دون استلام فعلي، والتعويض يعيد فتح كل التاريخ.
   - إقرارات أطراف التعيين والنقل شكلية، و«التزويد» يقيس سجل دليل لا الحساب.
   - النقل لا يغيّر النطاق ولا الأدوار.
   - «المعتمد الثاني» عند منح الأدوار شكلي، والدور الافتراضي لكل معيَّن `hr-core`.
   - آلة حالة العهدة قابلة للتجاوز، وفي الإرجاع ثغرة IDOR.
   - بطاقات نشطة مكررة، وإمكان إعادة إسناد البطاقة إلى موظف آخر.
   - أمر التجهيز يُعدَّل بعد اعتماده، والاستلامات الجزئية يكتب بعضها فوق بعض.
   - العتبات المالية ثابتة في الشيفرة بلا عملة ولا عصر نقدي.
   - لكل وحدة دور واحد بلا فصل مهام.
4. **قدرات ناقصة (P2، 3 فجوات):**
   - في اللوجستيات: موقع الأصل والوحدة المالكة، وسند الصرف، ولجنة الشطب، والجرد، وعهدة الأختام.
   - في البطاقة: الطباعة وQR والربط مع أنظمة الدخول.
   - في تقنية المعلومات: تخصيص الأجهزة، وتوحيد سجلَّي الأجهزة، وساعة الحادثة.
5. **فجوة تحقق:**
   - AC-33 «Pass» بطلب GET واحد يقبل 403 أيضاً، وAC-32 يغطي GET فقط.
   - E2E الخاص بـ FUN-005 يفحص رفض الزائر المجهول فقط.
   - اختبار mass-assignment يتخطى ثلاث تجميعات ويب دون إنذار.
   - IAM-005 «Done» بناءً على اختبار يقرأ ملف JSON.
   - لم يُشغَّل مسار Keycloak الحقيقي في أي بيئة قبول.

**التقدير الإجمالي:** نحو 59–76 يوم عمل لمطوّر واحد، منها 19–25 يوماً للمرحلة P0 (IDA-A وIDA-B وIDA-C).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | طلب بطاقة عند التعيين | `EmployeeCreatedConsumer` في IDCard ينشئ `CardRequest` بحالة Pending، دون صفوف checklist | ⚠️ (G-IDA-03) |
| 2 | معالجة الطلب وإصدار البطاقة | `ProcessRequest`: بطاقة Active، دون `ICardIssued`، دون انتهاء، ورقم تسلسلي من GUID | ❌ (G-IDA-03، G-IDA-11) |
| 3 | الطباعة والتسليم | `MarkPrinted` يسجّل وقتاً فقط. `MarkDelivered` يرفض دائماً من الواجهة لغياب حقل الإثبات | ❌ (G-IDA-04، G-IDA-16) |
| 4 | الفقد والتجديد والإبطال العاجل | موجودة في الخادم بلا أزرار. `Renew` على Lost يتكرر ويولّد بطاقات نشطة | ❌ (G-IDA-04، G-IDA-11) |
| 5 | إبطال البطاقة عند الفصل | طرف الفصل يبطل Active/Lost، وشبكة الأمان `ISeparationCompleted` | ✅، لكن التعويض يعيد بطاقات مُبطلة لسبب آخر (G-IDA-06) |
| 6 | حساب الدخول عند التعيين | `AuthUser` محلي. Keycloak معطّل في الإنتاج، والفشل يُسجَّل في السجل فقط | ❌ (G-IDA-01، G-IDA-07) |
| 7 | تذكرة تزويد IT | تذكرة، ثم `DirectoryEntry` عبر S2S، ثم Failed/Retry | ⚠️ يقيس سجل الدليل لا الحساب (G-IDA-07) |
| 8 | تسليم عهدة عادية | `AssignToEmployee` مع فهرس فريد وحارس نطاق ودليل عند الطلب | ✅ مع ثغرات آلة الحالة (G-IDA-10) |
| 9 | تسليم عهدة مميزة أو عالية القيمة | Pending، ثم مسار HR.Core عبر HTTP، ثم حدث نتيجة | ❌ لا يُغلق (G-IDA-02) |
| 10 | إرجاع العهدة | `ReturnAssignment` مع دليل للمميز | ⚠️ IDOR بين الفروع (G-IDA-10) |
| 11 | الفقد والاسترداد | `MarkLost` مع لقطات قبل/بعد، ومسار اعتماد للمميز | ✅ للعادي، ❌ للمميز (G-IDA-02) |
| 12 | سلسلة التوريد | أمر، ثم اعتماد (الطالب ≠ المعتمد)، ثم تنفيذ، ثم استلام، ثم أصل | ⚠️ تعديل بعد الاعتماد واستلامات تكتب فوق بعضها (G-IDA-12) |
| 13 | المخزون | الكمية تُعدَّل بحرية، وتزيد بالاستلام فقط، ولا سند صرف | ⚠️ (G-IDA-12، G-IDA-15) |
| 14 | النقل (Mover) | العهدة: إرجاع تلقائي أو تجاهل الحدث. الدليل: `DirectoryEntry` فقط. النطاق والأدوار ثابتة | ❌ (G-IDA-05، G-IDA-08) |
| 15 | الفصل (Leaver) | البطاقات ✅. العهدة: إغلاق صامت. IT وAuth: أعلام محلية | ❌ (G-IDA-01، G-IDA-05) |
| 16 | التعويض عند إلغاء الفصل | يعيد كل التاريخ لا ما غيّرته الملحمة | ❌ (G-IDA-06) |
| 17 | الأدوار وسياسات الصلاحيات | منح بـ«معتمد ثانٍ» يختاره المانح. السياسات المحلية لا يستهلكها شيء | ⚠️ (G-IDA-09، G-IDA-14) |
| 18 | الأختام الرسمية وخطط التوزيع | الأختام CRUD مع حذف نهائي. لا خطة توزيع لوجستية: `DistributionPlan` الموجود يخص HR.Movements | ⚠️ (G-IDA-15) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **حائز واحد للعهدة:** فهرس فريد مشروط على `AssetId` حين `ReturnedAt IS NULL`، ورمز تزامن ([AdminLogisticsDbContext.cs:66-70](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs)). وقاعدة `CanAssign` ترفض الأصل المفقود أو المستبعد ([AssetAssignmentRules.cs:11-14](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetAssignmentRules.cs)).
- **الفقد:** طلب معلّق واحد لكل أصل بفهرس فريد ([AdminLogisticsDbContext.cs:99-101](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs))، ولقطات JSON قبل وبعد ([AssetLossStateCapture.cs](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetLossStateCapture.cs))، والاسترداد يتطلب تقريراً مؤكداً وملاحظات.
- **نسب التوريد:** الأصل الجديد يتطلب سجل استلام أو إدخالاً استثنائياً بسبب ([AssetOriginRules.cs](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetOriginRules.cs)). رقم العهدة من COD-001 وفريد لكل جهة ([AdminLogisticsDbContext.cs:35](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs)).
- **فصل المهام في أمر التجهيز:** الطالب لا يعتمد طلبه ([SupplyOrderWorkflowRules.cs:33-35](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/SupplyOrderWorkflowRules.cs)).
- **إنقاص المخزون ذري:** يتحقق من الرصيد تحت التزامن ([InventoryItemsController.cs:168-186](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/InventoryItemsController.cs)).
- **أدلة التسليم:** نص واضح أو ملف FILE-002، ورفض القيمة الافتراضية «موقّع» (`AssetHandoverRules`، `CardDeliveryRules`). وإيصال تسليم PDF بالاسم من lookup على الخادم (`AssetHandoverPdfService`).
- **حارس نطاق الفرع على إجراءات العهدة** (`AssetOrgScopeGuard`)، ودليل AC-32 الحي على منع القراءة من فرع شقيق.
- **idempotency على مستوى الرسالة:** `InboxConsumeFilter` مع جدول `IntegrationMessageReceipts` في الخدمات الأربع، ويلفّ كل مستهلك في معاملة.
- **إبطال البطاقة في طرف الفصل** لكل بطاقة Active أو Lost ([PartyAckConsumers.cs:11-32](../src/Services/IDCard/IDCard.Infrastructure/Consumers/PartyAckConsumers.cs)).
- **الخصوصية:** اسم مستخدم Keycloak مشتق من معرّف الموظف لا من الرقم الوطني ([EmployeeCreatedConsumer.cs:44-49](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeCreatedConsumer.cs))، ووصف تذكرة التزويد بلا رقم وطني.
- **التعطيل في Keycloak:** حين يُستدعى، يُنهي الجلسات أيضاً ([KeycloakUserProvisioningService.cs:125-131](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs)).
- **المزامنة الداخلية S2S** بسياسة خدمة صريحة ([DirectoryInternalSyncController.cs:11](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/DirectoryInternalSyncController.cs)). وشاشة النسخ الاحتياطي لا تنفّذ `pg_dump` داخل الويب، بل تقرأ كتالوجاً.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال أو الوصول · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-IDA-01 — الموظف المفصول يحتفظ بالدخول وبأدواره في Keycloak

- **الدليل:**
  - **طرف الفصل يكتب علماً محلياً فقط:** `u.IsActive = false` ([PartyAckConsumers.cs:12-21](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)) دون أي استدعاء لـ Keycloak.
  - **شبكة الأمان تخرج قبل التعطيل:** `SeparationCompletedConsumer` يخرج بـ `if (!user.IsActive) return;` ([SeparationCompletedConsumer.cs:87](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs)) قبل `TryDisableUserAsync` (:103). والملحمة لا تنشر `ISeparationCompleted` إلا بعد اكتمال كل الأطراف، ومنها Auth.Directory ([SeparationStateMachine.cs:44, 129, 263](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)). إذن يكون العلم المحلي `false` دائماً عند وصول الحدث.
  - **المستهلك الثاني يخرج بالشرط نفسه:** `EmployeeStatusChangedConsumer` يحوي `if (user.IsActive == !inactive) return;` ([EmployeeStatusChangedConsumer.cs:95](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeStatusChangedConsumer.cs)).
  - **يحتاج تحقق:** `ISeparationCompleted.SchemaVersion => 3` والمستهلك يقبل حتى 2 ([SeparationCompletedConsumer.cs:59](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs))، فقد يُتجاهل الحدث كله (انظر G-MOV-08 في دراسة FUN-003).
  - **المزامنة معطّلة في الإنتاج:** `"Enabled": false` في [appsettings.json:24](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json). لا يغيّرها `appsettings.Production.json`، ولا خدمة `auth-directory-web` في [docker-compose.production.yml:513-526](../docker-compose.production.yml) التي تمرّر السر فقط. فتعيد الخدمة `Disabled()` = `(Attempted=false, Success=true)` ([IKeycloakUserProvisioningService.cs:30](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/IKeycloakUserProvisioningService.cs)، [KeycloakUserProvisioningService.cs:38-39, 283-295](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs)). النتيجة: لا يُنشأ مستخدم Keycloak عند التعيين، ولا يُربط `KeycloakUserId`، ولا يُعطَّل شيء عند الفصل. وبيئة القبول (`ASPNETCORE_ENVIRONMENT=Acceptance`) لا تملك ملف إعداد يفعّلها.
  - **الأدوار لا تُسحب:** الخدمة تضيف أدواراً فقط (`HttpMethod.Post` إلى `role-mappings/realm`، [KeycloakUserProvisioningService.cs:174-175](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs))، ولا يوجد طلب حذف في الملف.
  - **لا فحص عند الدخول:** لا يفحص أي مكوّن `AuthUser.IsActive`، فلا مرجع له في `src/Gateway` ولا في `HRManagement.ServiceDefaults`.
  - **ادعاء IAM-005 غير مسنود:** الوثيقة تعد بتعطيل الدخول خلال ساعة ([IAM_005_IDENTITY_LIFECYCLE.md:33](IAM_005_IDENTITY_LIFECYCLE.md))، والبطاقة «Done» ([10-NATIONAL-CONTROL-SUPPLEMENT.md:28](completion-backlog/10-NATIONAL-CONTROL-SUPPLEMENT.md)). لكن `Iam005LifecycleTests` يقرأ ملف JSON فقط ([Iam005LifecycleTests.cs:50-60](../src/Tests/HRManagement.UnitTests/Iam005LifecycleTests.cs)). واختبار `SeparationCompletedConsumerTests` يبدأ من `IsActive = true` (:31)، فلا يغطي التسلسل الحقيقي.
- **الأثر:** الموظف المفصول يبقى قادراً على الدخول بالحساب نفسه وبكل أدواره، ومنهم موظف HR أو رواتب يحمل `hr-core` أو `payroll-approver`. يبقى كذلك حتى يعطّله مدير Keycloak يدوياً. هذا يخالف معيار FUN-005 («لا وصول بعد الفصل») وIAM-005.
- **الإصلاح المقترح:**
  - طرف الفصل في Auth.Directory ينفّذ في Keycloak: تعطيل، إنهاء الجلسات، حذف أدوار realm. ويسجّل `IdpDisabledAt`.
  - فشل Keycloak، أو تعطّل المزامنة في الإنتاج، يرمي استثناءً، فيصبح الطرف Faulted وتُفتح intervention. لا يُرسل ACK في هذه الحالة.
  - شبكتا الأمان تفحصان حالة IdP (`IdpDisabledAt == null`)، لا العلم المحلي.
  - تُفعَّل المزامنة في الإنتاج بقرار (D-IDA-5)، مع فحص إقلاع وhealth يرفض Production دونها. يسبق ذلك إصلاح الدور الافتراضي (G-IDA-09).
  - مهمة مصالحة يومية تكشف الحسابات المفعّلة لموظفين غير نشطين.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md)
- **التحقق المطلوب:**
  - **موجب:** على ملف شبيه بالإنتاج مع Keycloak حقيقي، يُفصل موظف بدور `hr-core`. خلال دقائق يتحقق التالي: `enabled=false` في Keycloak، رفض الجلسة المفتوحة، لا أدوار realm، و`IdpDisabledAt` مسجّل.
  - **سالب:** Keycloak متوقف أثناء الفصل ← الطرف Faulted، والملحمة في `AwaitingIntervention`، ولا `ISeparationCompleted`. بعد الاستئناف تكتمل.
  - **سالب:** Auth.Directory يقلع في Production والمزامنة معطّلة ← health أحمر.

#### G-IDA-02 — حلقة اعتماد العهدة المميزة وفقدها لا تُغلق في الإنتاج

- **الدليل:**
  - **المسار يُفعَّل لأي أصل ذي قيمة:** في الإنتاج لا مفتاح `PolicyEngine` لـ `Assets.Custody`، فيعمل العميل بالوضع Off ويعيد `PolicyStaticFallbackShim` ([PolicyEnforceClient.cs:29, 33-41](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)). الـ shim يطلب سير عمل لكل أصل مميز أو قيمته > 10,000 (:125)، ويصنّف الأصل مميزاً عند القيمة > 10,000، أو > 5,000 للأصل الثابت (:146).
  - **الطلب يُحفظ عالقاً إن فشل البدء:** `AssignToEmployee` يحفظ العهدة `PendingApproval` ويبدأ المسار في HR.Core عبر HTTP ([AssetsController.cs:422-464](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)، [WorkflowTriggerService.cs:44-71](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)). إن فشل البدء تُحفظ العهدة `PendingApproval` و`WorkflowInstanceId = null`، مع رسالة «تعذّر بدء سير الموافقة» ([AssetsController.cs:461-463](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)). لا إعادة محاولة ولا سحب. والأمر نفسه لتقرير الفقد (:634-644).
  - **لا شاشة للمعتمد:**
    - صندوق العمل لا يشمل `AssetAssignment` ولا `AssetLossReport` ([WorkflowInboxQueryService.cs:22-29](../src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs)).
    - رابط «عرض الموافقة» في صفحة الأصل يشير إلى `Approvals/Review?workflowInstanceId=` ([Assets/Details.cshtml:290](../src/Services/AdminLogistics/Admin.Logistics.Web/Views/Assets/Details.cshtml)). لكن `Review(Guid id)` يبحث في `ApprovalRequests` القديمة ([ApprovalsController.cs:78-84](../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs))، فيعيد 404.
    - يبقى طريق واحد: JSON API `POST api/workflow-instances/{id}/approve` ([WorkflowInstancesController.cs:104-115](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
  - **حدث النتيجة لا يُحفظ:** `ApproveAsync` يحفظ أولاً ثم ينشر ([WorkflowInstanceActionService.cs:117-120](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). فرع الأصول ينشر `IAssetAssignmentApproved`/`IAssetLossApproved` ويعود دون `SaveChangesAsync` (:177-186, 216-262)، والمتحكم يعيد `Ok`. في Bus Outbox لا تُكتب الرسالة إلا بحفظ لاحق ([EVENT_PUBLISHER_INVENTORY.md:9, 26](EVENT_PUBLISHER_INVENTORY.md)). معالجات التوظيف تحفظ بعد النشر ([RecruitmentRequisitionWorkflowHandler.cs:32](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionWorkflowHandler.cs))، ومعالجات الأصول لا تحفظ. **يحتاج تحقق** تشغيلي، والمتوقع أن الحدث لا يصل إلى Admin.Logistics.
  - **العهدة العالقة تحجز الأصل:** `ReturnAssignment` يرفض غير `Active` ([AssetsController.cs:502-503](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)). و`CancelAsset` يرفض لوجود عهدة مفتوحة (:710-714)، لأن `HasOpenAssignment` يعدّ `PendingApproval` مفتوحة ([AssetAssignmentRules.cs:8-9](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetAssignmentRules.cs)).
  - **الأدلة لا تغطي الحلقة:** دليل AC-11 الحي توقف عند حالة المسار في HR.Core (`acceptance-evidence/AC-11/after.json`: `Completed`) ولم يفحص العهدة. واختبار AC-33 التكاملي يحقن رسالة مصطنعة في المستهلك مباشرة ([Ac33AssetLossWorkflowTests.cs:38-63](../src/Tests/HRManagement.IntegrationTests/Ac33AssetLossWorkflowTests.cs)).
- **الأثر:** كل تسليم لعهدة مميزة أو عالية القيمة يبقى «بانتظار الموافقة» إلى الأبد. الأصل لا يُسلَّم ولا يُرجَع ولا يُلغى. وكل فقد لأصل مميز لا يُؤكَّد ولا يُسوّى. أي أن الأصول الأهم خارج النظام عملياً.
- **الإصلاح المقترح:**
  - حفظ بعد النشر (Save→Publish→Save)، أو معالج للأصول يحفظ كبقية المعالجات.
  - إدخال النوعين في صندوق العمل برابط عميق إلى صفحة الأصل، وتصحيح الرابط المكسور.
  - أدوار الخطوات بقرار مشترك مع WF (D-IDA-4).
  - زر «إعادة بدء المسار»، ومهمة مصالحة للعهد `PendingApproval` التي لا نسخة لها.
  - إجراء «سحب الطلب» يحرّر الأصل.
  - تمرير المبادر البشري إلى HR.Core بدل حساب الخدمة.
- **الخطة:** [PLAN-45](acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md)
- **التحقق المطلوب:**
  - **موجب (ملف شبيه بالإنتاج):** أصل قيمته فوق العتبة ← طلب تسليم ← يظهر في صندوق عمل المعتمدَين ← اعتمادان من الواجهة ← خلال دقيقة: العهدة `Active` والأصل `InUse`، وفي الـ outbox رسالة مُرسلة.
  - **موجب:** الرفض يجعل العهدة `Rejected` ويحرّر الأصل.
  - **سالب:** HR.Core متوقف عند التقديم ← Pending بلا نسخة ← المصالحة أو الزر تبدأ المسار لاحقاً.
  - **سالب:** فقد أصل مميز ثم رفض ← الأصل دون تغيير، ولقطة «قبل» فقط.

#### G-IDA-03 — إصدار البطاقة من طلبها لا يُكمل قائمة التعيين، فتتوقف رحلة التعيين

- **الدليل:**
  - **المعالجة لا تنشر الحدث:** `ProcessRequest` ينشئ بطاقة Active ويعلّم خطوة Issued، ولا ينشر `ICardIssued` ([CardRequestsController.cs:125-172](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)). الناشران الوحيدان هما `IssuedCards/Create` و`Renew` ([IssuedCardsController.cs:92, 310](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)).
  - **البند لا يكتمل إلا بالحدث:** `IdentityIssued` يكتمل في HR.Contracts عند `ICardIssued` فقط ([CardIssuedHireChecklistConsumer.cs:22-46](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/CardIssuedHireChecklistConsumer.cs)). و`MarkIdentityIssuedAsync` لا يستدعيه أي سطر ([HireCaseSupport.cs:71](../src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/HireCaseSupport.cs)). والمعالج لا يكمل يدوياً إلا الفحص الطبي واعتماد السيرة ([HireWizardController.cs:187, 262](../src/Services/HRContracts/HR.Contracts.Web/Controllers/HireWizardController.cs)).
  - **البند شرط لبدء الرحلة:** `IdentityIssued` بند أساسي في كل قائمة ([HireChecklistDefaults.cs:31](../src/Services/HRContracts/HR.Contracts.Domain/Services/HireChecklistDefaults.cs)). وبدء رحلة التعيين يشترط اكتمال كل البنود عدا الأرشفة ([HireWorkflowRules.cs:34-52](../src/Services/HRContracts/HR.Contracts.Domain/Services/HireWorkflowRules.cs)، [HireCaseHandlers.cs:80-81](../src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/HireCaseHandlers.cs)).
  - **الطلب التلقائي بلا قائمة:** الطلب المنشأ من `IEmployeeCreated` بلا صفوف checklist ([EmployeeCreatedConsumer.cs:31-38](../src/Services/IDCard/IDCard.Infrastructure/Consumers/EmployeeCreatedConsumer.cs))، خلافاً للطلب اليدوي ([CardRequestsController.cs:59-64](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)).
- **الأثر:** المسار الموثق (UAT U1: «طلب بطاقة ← Process ← إصدار») يوقف التعيين: لا تبدأ ملحمة التعيين، ولا الأرشفة، ولا تسجيل الديوان. الالتفاف الوحيد إصدار بطاقة ثانية يدوياً من `IssuedCards/Create`، فيصير للموظف بطاقتان نشطتان (G-IDA-11)، ويبقى الطلب الأصلي معلّقاً.
- **الإصلاح المقترح:**
  - `ProcessRequest` ينشر `ICardIssued` عبر الـ outbox (Save→Publish→Save)، ويولّد الرقم التسلسلي من COD-001، ويضبط تاريخ الانتهاء من التصميم.
  - صفوف checklist للطلبات التلقائية، وربط الطلب بالبطاقة.
  - إن قررت الجهة ألا تُصدر بطاقة، يُضاف إكمال يدوي لـ `IdentityIssued` بدليل.
- **التحقق المطلوب:**
  - **موجب:** تعيين ← طلب تلقائي ← معالجة ← خلال دقيقة يكتمل `IdentityIssued` وينجح `PublishHireCase`.
  - **سالب:** معالجة الطلب مرتين ← الثانية `BadRequest`، مع بطاقة واحدة وحدث واحد.

#### G-IDA-04 — التسليم والفقد والتجديد والإبطال العاجل وإلغاء الطلب غير قابلة للتنفيذ من الواجهة

- **الدليل:**
  - **التسليم يُرفض دائماً:** نموذج «تسليم» يرسل `deliveredTo` المخفي فقط ([IssuedCards/Details.cshtml:44](../src/Services/IDCard/IDCard.Web/Views/IssuedCards/Details.cshtml))، والخادم يشترط نص إيصال أو ملفاً ([IssuedCardsController.cs:176-180](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)).
  - **إجراءات بلا أزرار:** الصفحة لا تعرض إلا «طباعة» و«تسليم» و«إلغاء» (حين تكون البطاقة Active) و«تعديل» (Details.cshtml:43-53). أما `MarkLost` (:238) و`Renew` (:272) و`UrgentRevoke` (:223) و`MarkExpired` (:255) فلا أزرار لها.
  - **إلغاء الطلب مكسور:** صفحة الطلب ترسل إلى `Delete` ([CardRequests/Details.cshtml:20](../src/Services/IDCard/IDCard.Web/Views/CardRequests/Details.cshtml))، ولا يوجد إجراء بهذا الاسم في `CardRequestsController`. ولا زر لـ `Cancel` (:108).
  - **التصميم يُختار عشوائياً:** نموذج المعالجة لا يرسل `cardDesignId` (Details.cshtml:17)، فيُختار أول تصميم بلا ترتيب ([CardRequestsController.cs:141-142](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)).
- **الأثر:**
  - خطوة «Delivered» إلزامية في المصفوفة ([FUN_005_JOURNEY_MATRIX.md:13](FUN_005_JOURNEY_MATRIX.md)) ولا يمكن تسجيلها.
  - استبدال البطاقة المفقودة، والإبطال العاجل لبطاقة مسروقة، غير متاحين للمستخدم.
  - السيناريوهات U3 وU5 في حزمة UAT «الجاهزة للتوقيع» غير قابلة للتنفيذ.
- **الإصلاح المقترح:**
  - نموذج تسليم فيه المستلم ونص الإيصال أو رفع FILE-002.
  - أزرار للفقد والتجديد والإبطال العاجل والانتهاء، بحسب الحالة.
  - «إلغاء الطلب» بسبب بدل «حذف».
  - اختيار تصميم صريح.
- **التحقق المطلوب:**
  - **موجب (عبر البوابة بالواجهة):** تسليم بإيصال ينجح. فقد ثم تجديد يولّد بطاقة بديلة مرتبطة.
  - **سالب:** تسليم بلا إيصال يُرفض برسالة.
  - **سالب:** إلغاء طلب ثم معالجته ← رفض.

### 4.2 ثغرات السلامة والحوكمة (P1)

#### G-IDA-05 — العهدة تُغلق صامتة عند الفصل والنقل دون استلام فعلي

- **الدليل:**
  - **طرف الفصل:** يضبط `ReturnedAt` دون `Status`، ويضع كل أصل `InStorage` ([PartyAckConsumers.cs:13-30](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)). ويشمل ذلك العهد `PendingApproval`.
  - **مستهلك النقل:** يفعل الشيء نفسه ([TransferExecutedConsumer.cs:41-51](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/TransferExecutedConsumer.cs))، دون الدليل الذي يشترطه الإرجاع اليدوي للمميز ([AssetsController.cs:509](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)). **يحتاج تحقق:** المستهلك يقبل `SchemaVersion` حتى 2 (:26)، والعقد بالإصدار 3. فإما أنه يُغلق كل العهد، أو يتجاهل الحدث كله ومعه تحديث لقطة الوحدة (:58-70) (G-MOV-08).
  - **شبكة الأمان:** تضع الأصل `InStorage` حتى لو كان Lost أو Disposed ([SeparationCompletedConsumer.cs:71-75](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/SeparationCompletedConsumer.cs)).
  - **اعتماد لاحق ينشئ عهدة شبحية:** إن وصل لاحقاً اعتماد لعهدة كانت معلّقة، يفعّلها المستهلك رغم أن `ReturnedAt` مضبوط ([AssetAssignmentWorkflowConsumers.cs:26, 40-41](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/AssetAssignmentWorkflowConsumers.cs)). النتيجة أصل `InUse` بلا حائز ظاهر.
  - **الوثائق تجعله قاعدة:** «Transfer → Status=InStorage» ([FUN_005_RECONCILIATION_CHECKLIST.md:12](FUN_005_RECONCILIATION_CHECKLIST.md)، [FUN_005_UAT_PACK.md:12](FUN_005_UAT_PACK.md)).
- **الأثر:**
  - النظام يقول إن الأجهزة والسيارات في المستودع وهي في يد المفصول أو المنقول.
  - «براءة الذمة» تفقد معناها، ولا قائمة بما يجب استرداده، ولا أساس لتحميل القيمة.
  - العهدة نفسها تظهر `Active` في سجل العهد ومُعادة في الأصل.
- **الإصلاح المقترح:**
  - الفصل والنقل يحوّلان العهد المفتوحة إلى «مطلوب إرجاعها» بقائمة ومهلة، لا إلى «مُعادة».
  - الطرف يكتمل بدليل يسرد العهد المفتوحة، أو يُحجز، بحسب القرار D-IDA-1/D-IDA-2.
  - العهد المعلّقة تُلغى صراحةً.
  - الحالة والتاريخ متسقان دائماً، ولا لمس للأصل Lost أو Disposed.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (جزئياً: عند الفصل لا إرجاع آلي، بل بلاغ بالبنود المفتوحة وبراءة ذمة تُغلق بمسار المالك وبدليل). والنقل والعهد المعلّقة: الموجة 2
- **التحقق المطلوب:**
  - **موجب:** فصل موظف بعهدتين، إحداهما مميزة ← تظهر قائمة إرجاع، والأصلان يبقيان مع الموظف حتى استلام بدليل.
  - **سالب:** عهدة `PendingApproval` عند الفصل ← تُلغى، والاعتماد اللاحق لا يفعّلها.
  - **سالب:** أصل Lost ← حالته لا تتغير.

#### G-IDA-06 — التعويض يعيد كل التاريخ لا ما غيّرته الملحمة

- **الدليل:**
  - **Admin.Logistics:** يعيد فتح كل عهدة سبق إرجاعها لهذا الموظف، أياً كان تاريخها ([PartyAckConsumers.cs:34-46](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)). هذا يصطدم بالفهرس الفريد المشروط ([AdminLogisticsDbContext.cs:66-68](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs)) حين يحمل الأصل اليوم شخص آخر، فيفشل التعويض ويدخل التدخل. **يحتاج تحقق.**
  - **IDCard:** يعيد تفعيل كل بطاقة `Revoked && UrgentRevoke` ([PartyAckConsumers.cs:34-45](../src/Services/IDCard/IDCard.Infrastructure/Consumers/PartyAckConsumers.cs)). ويشمل ذلك بطاقة أُبطلت يدوياً لأنها مسروقة، إذ يضبط `UrgentRevoke` العلم نفسه ([IssuedCardsController.cs:230](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)).
  - **IT:** يعيد كل صلاحية سُحبت يوماً (`RevokedAt != null`) ([PartyAckConsumers.cs:36-41](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs))، ولا يعيد الأجهزة التي فصلها.
  - **متى يقع:** يُطلق التعويض عند إلغاء فصل في التدخل ([SeparationStateMachine.cs:297-316](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)).
- **الأثر:** إلغاء قرار فصل يعيد تفعيل بطاقات مسروقة وصلاحيات سُحبت لأسباب أخرى، وقد يعلّق الملحمة في حلقة تدخل.
- **الإصلاح المقترح:** كل تغيير يحمل معرّف الملحمة التي أحدثته (`RevokedBySeparationId`، `ReturnedBySeparationId`)، والتعويض يعكس هذه التغييرات فقط. ويُضاف اختبار تعويض لكل طرف.
- **الخطة:** [PLAN-63](acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md) (تعويض IDCard وIT وAdmin.Logistics محصور بعلامة الفصل)، و[PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (تعويض طرف Auth باللقطة)
- **التحقق المطلوب:**
  - **موجب:** فصل ثم إلغاء ← تعود فقط البطاقة والصلاحيات والعهد التي غيّرها الفصل.
  - **سالب:** بطاقة مُبطلة عاجلاً قبل الفصل ← تبقى مُبطلة.
  - **سالب:** عهدة قديمة أصلها مع موظف آخر ← لا تُمس، ولا Fault.

#### G-IDA-07 — إقرارات أطراف التعيين والنقل شكلية، والتزويد يقيس السجل لا الحساب

- **الدليل:**
  - **إقرارات بلا فحص:** طرفا IDCard وIT في ملحمة التعيين يقرّان دون أي فحص ([PartyAckConsumers.cs:57](../src/Services/IDCard/IDCard.Infrastructure/Consumers/PartyAckConsumers.cs)، [PartyAckConsumers.cs:52](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs)، [HireCaseStateMachine.cs:38-41](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/HireCaseStateMachine.cs)). وطرفا Auth.Directory وAdmin.Logistics في ملحمة النقل كذلك ([PartyAckConsumers.cs:37](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)، [PartyAckConsumers.cs:56](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)، [TransferSecondmentStateMachines.cs:46-47](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)).
  - **طرف Auth في التعيين يفحص السجل المحلي فقط:** وجود `AuthUser` نشط، لا `KeycloakUserId` ([PartyAckConsumers.cs:40-47](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - **«التزويد» في IT لا يمس الحساب:** هو إدراج `DirectoryEntry` (سجل دليل) في Auth.Directory ([DirectoryEntrySyncService.cs:20-50](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Services/DirectoryEntrySyncService.cs)). وعند غياب العنوان يُعاد `true` ([HttpDirectoryEntrySyncClient.cs:27-30](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Integration/HttpDirectoryEntrySyncClient.cs)).
  - **فشل Keycloak لا يُعاد:** فشل إنشاء مستخدم Keycloak يُسجَّل تحذيراً فقط ([EmployeeCreatedConsumer.cs:81-83](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)). وإعادة التسليم تخرج مبكراً لوجود `AuthUser` (:41)، ولا يصل الفشل إلى قائمة `FailedProvisioning`.
  - **«إعادة المحاولة» تعرض نجاحاً زائفاً:** `SyncFromKeycloak` يعرض «تمت مزامنة المستخدم مع Keycloak» والمزامنة معطّلة، لأن `Disabled()` نجاح ([AuthUsersController.cs:308-335](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs)). وهذا هو مسار «Auth Retry» الموثق ([FUN_005_RECONCILIATION_CHECKLIST.md:15](FUN_005_RECONCILIATION_CHECKLIST.md)).
  - **`RetryProvision` يُفسد السجل:** يرسل ملخص التذكرة («تجهيز حساب وأجهزة للموظف …») كاسم كامل، و`null` للوحدة ([SupportTicketsController.cs:153-158](../src/Services/ITInfrastructure/IT.Infra.Web/Controllers/SupportTicketsController.cs))، فيكتب فوق `DisplayName` في الدليل.
- **الأثر:** ملحمة التعيين تكتمل معلنةً أن البطاقة والـ IT والحساب جاهزة، ولا شيء منها جاهز. والفشل الحقيقي غير مرئي. معيار FUN-005 «فشل تزويد الحساب مرئي ويُعاد» غير متحقق.
- **الإصلاح المقترح:**
  - كل إقرار يحمل دليلاً حقيقياً (رقم البطاقة، `KeycloakUserId`، معرّف التذكرة)، أو يُعلن Faulted.
  - حالة تزويد على `AuthUser` (Pending/Succeeded/Failed) تظهر في قائمة الفشل، وإعادة المحاولة تعيد محاولة Keycloak فعلاً.
  - `RetryProvision` يأخذ الاسم والوحدة من lookup.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (جزئياً: طرف Auth في التعيين بـ `KeycloakUserId`، وحالة التزويد، وإعادة محاولة حقيقية). وإقرارات IDCard وIT و`RetryProvision` في IT: الموجة 2
- **التحقق المطلوب:**
  - **موجب:** تعيين مع Keycloak ← الإقرار يحمل `KeycloakUserId`.
  - **سالب:** Keycloak متوقف ← فشل ظاهر في القائمة، وإعادة المحاولة بعد عودته تنجح، والملحمة لا تكتمل قبل ذلك.

#### G-IDA-08 — النقل لا يغيّر نطاق الوصول ولا الأدوار

- **الدليل:**
  - **Auth.Directory لا يحدّث النطاق:** مستهلك النقل يحدّث `DirectoryEntry.PrimaryOrgUnitId` فقط ([TransferExecutedConsumer.cs:27-46](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/TransferExecutedConsumer.cs)). أما `AuthUser.ScopeOrgUnitId` وخاصية Keycloak `scope_org_unit_id` فتُضبطان عند الإنشاء فقط ([KeycloakUserProvisioningService.cs:77](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs)). وهذه الخاصية هي ما يحدد نطاق الفرع ([HrOrgScopeVisibility.cs:139, 159](../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs)).
  - **الأدوار لا تُسحب** عند تغيّر المنصب (G-IDA-01).
  - **التعيين الداخلي لا يصل:** `IInternalPlacementCompleted` يحمل الوحدة المصدر والهدف ([IInternalPlacementCompleted.cs:10-11](../src/Shared/HRManagement.MessageContracts/Events/IInternalPlacementCompleted.cs))، ولا يستهلكه Auth.Directory ولا Admin.Logistics. ولقطة وحدة الموظف في اللوجستيات تُحدَّث بالتعيين والنقل فقط ([TransferExecutedConsumer.cs:58-70](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/TransferExecutedConsumer.cs))، فينحرف نطاق العهدة.
  - **IAM-005 يعد بغير ذلك:** سحب أدوار الوحدة المصدر خلال 4 ساعات ([IAM_005_IDENTITY_LIFECYCLE.md:24](IAM_005_IDENTITY_LIFECYCLE.md)).
- **الأثر:** مدير فرع منقول يحتفظ بنطاق فرعه القديم وأدواره، فيرى بيانات موظفيه ويتصرف بها. وعهدة الموظف الذي عُيّن داخلياً تظهر للفرع الخطأ.
- **الإصلاح المقترح:**
  - مستهلكا النقل والتعيين الداخلي في Auth.Directory يحدّثان `ScopeOrgUnitId` وخاصية Keycloak.
  - سحب أدوار الوحدة المصدر بحسب سياسة، وإنهاء الجلسات.
  - استهلاك `IInternalPlacementCompleted` في اللوجستيات.
- **الخطة:** [PLAN-52](acceptance-plans/PLAN-52-iam-branch-scope-bola.md) (جزئياً: المنقول المقيّد يفقد `scope_org_unit_id` محلياً وفي Keycloak، ولقطة HR.Health تتبع النقل). وسحب الأدوار والتعيين الداخلي: الموجة 2
- **التحقق المطلوب:**
  - **موجب:** نقل `branch-manager` من فرع 1 إلى فرع 2 ← بعد الدخول التالي يرى فرع 2 فقط.
  - **سالب:** محاولة فتح موظف من فرع 1 ← 403.

#### G-IDA-09 — منح الأدوار: معتمد ثانٍ شكلي، ومقارنة معرّفات خاطئة، ودور افتراضي `hr-core`

- **الدليل:**
  - **فحص «لا يمنح لنفسه» لا يعمل:** `RoleAssignmentGuard` يقارن المانح بالهدف ([RoleAssignmentGuard.cs:41](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/RoleAssignmentGuard.cs)). لكن المانح هو `sub` من Keycloak ([AuthUsersController.cs:30-34, 75](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs))، والهدف `AuthUser.Id` محلي، بينما `KeycloakUserId` حقل منفصل ([AuthUser.cs:39](../src/Services/AuthDirectory/Auth.Directory.Domain/Entities/AuthUser.cs)). فلا يتساويان أبداً. **يحتاج تحقق.**
  - **«المعتمد الثاني» قيمة يختارها المانح:** هو حقل يرسله المانح نفسه من قائمة منسدلة (AuthUsersController.cs:102-130)، ولا يقوم صاحبه بأي فعل اعتماد. وفحص «مختلف عن المانح» يقارن فضاءي معرّفات مختلفين (:116). ومسار الدور الحساس لا يفحص رتبة المانح ([RoleAssignmentGuard.cs:62-78](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/RoleAssignmentGuard.cs)، مقابل :50).
  - **إعادة تسمية الدور تمنحه:** `RolesController.Edit` يغيّر اسم الدور ([AuthExtendedControllers.cs:36-47](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthExtendedControllers.cs))، والمزامنة تربط بالاسم ([KeycloakUserProvisioningService.cs:161](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs)، [AuthUsersController.cs:199-208](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs)). فتسمية دور محمول باسم `hr-admin` تمنحه لكل حامليه في المزامنة التالية، دون اعتماد مزدوج.
  - **الدور الافتراضي `hr-core`:** `DefaultRealmRoles: ["hr-core"]` ([appsettings.json:27-28](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json))، والمزامنة مفعّلة في Development ([appsettings.Development.json:10](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.Development.json)). فكل معيَّن في المكدّس المحلي يحصل على صلاحية كتابة HR. وتفعيل المزامنة في الإنتاج (إصلاح G-IDA-01) سيفعل الشيء نفسه.
  - **سياسات بلا مستهلك:** `PermissionPolicy` و`Role.Permissions` تُخزَّن ولا يستهلكها أي سطر (AuthExtendedControllers.cs:88-105).
- **الأثر:** مستخدم واحد بدور `auth-directory` يستطيع منح نفسه أو غيره `hr-admin`، وكل معيَّن جديد قد يحصل على صلاحيات HR.
- **الإصلاح المقترح:**
  - طلب منح معلّق يعتمده شخص ثانٍ بفعل مستقل.
  - مقارنة المعرّفات بـ `KeycloakUserId`، وفحص الرتبة في كل المسارات.
  - منع إعادة تسمية دور له حاملون، أو مزامنة الأسماء بمعرّف Keycloak.
  - دور افتراضي `hr-employee` مع فحص إعداد يرفض أي دور مميز.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (جزئياً: الدور الافتراضي `hr-employee` وفحص إقلاع يرفض غيره). والمنح المعلّق ومقارنة المعرّفات وإعادة التسمية: الموجة 2
- **التحقق المطلوب:**
  - **سالب:** منح المستخدم نفسه ← رفض.
  - **سالب:** منح `hr-admin` دون اعتماد الشخص الثاني ← يبقى معلّقاً.
  - **سالب:** إعادة تسمية دور محمول ← رفض.
  - **موجب:** معيَّن جديد ← `hr-employee` فقط.

#### G-IDA-10 — آلة حالة العهدة قابلة للتجاوز، وIDOR في إرجاع العهدة

- **الدليل:**
  - **IDOR في الإرجاع:** `ReturnAssignment` يحمّل العهدة بـ `assignmentId` ([AssetsController.cs:497](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs))، ويفحص النطاق على `assetId` القادم من الطلب (:499). ولا يتحقق أبداً من أن `assignment.AssetId == assetId`، ثم يضع الأصل `assetId` في المستودع (:508, 532). يستطيع مستخدم فرع أن يرسل أصلاً من فرعه وعهدة من فرع آخر، فيغلق عهدة الفرع الآخر ويغيّر حالة أصله. اختبار المعمارية يبحث عن نص الاستدعاء فقط ([LogisticsCustodyScopeTests.cs:29-35](../src/Tests/HRManagement.UnitTests/Architecture/LogisticsCustodyScopeTests.cs))، وAC-32 لا يرسل POST.
  - **تعديل الحالة بحرية:** `Edit` يقبل الحالة من قائمة حرة ([Assets/Edit.cshtml:40](../src/Services/AdminLogistics/Admin.Logistics.Web/Views/Assets/Edit.cshtml)، [AssetsController.cs:333](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)):
    - Lost ← InStorage دون استرداد.
    - InUse ← Disposed مع عهدة مفتوحة.
    - Disposed دون أي اعتماد.
  - **خفض التصنيف بتعديل القيمة:** تعديل القيمة يعيد التصنيف (:338). يُخفض الأصل تحت العتبة، فيُسلَّم دون اعتماد ولا دليل، ثم تُعاد القيمة.
  - **الإدخال الاستثنائي:** يأخذ الحالة من النموذج (:223)، فيمكن إنشاء أصل «قيد الاستخدام» بلا حائز.
  - **الصيانة:** تضع أي أصل غير InUse في الصيانة، ومنه المفقود والمستبعد ([AdminExtendedControllers.cs:635-636](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AdminExtendedControllers.cs))، ولا انتقال للخروج منها.
- **الأثر:** سجل العهدة يفقد قيمته الإثباتية، والتلاعب بين الفروع ممكن رغم «Pass» في AC-32.
- **الإصلاح المقترح:**
  - تحقق تطابق العهدة والأصل، ونطاق على العهدة نفسها.
  - إزالة الحالة من `Edit`، وانتقالات صريحة: بدء الصيانة وإنهاؤها، والاستبعاد بمسار (G-IDA-15).
  - تثبيت التصنيف بعد التسليم إلا بمراجعة.
- **التحقق المطلوب:**
  - **سالب:** POST من فرع 2 بأصل من فرعه وعهدة من فرع 1 ← 403 أو 400، ولا تغيير.
  - **سالب:** `Edit` بحالة Lost ← تُتجاهل أو تُرفض.
  - **موجب:** الإرجاع الصحيح ينجح.

#### G-IDA-11 — بطاقات نشطة مكررة، وإعادة إسناد البطاقة، ودورة حياة غير مكتملة

- **الدليل:**
  - **لا فحص لبطاقة نشطة قائمة:** لا قبل `ProcessRequest` ([CardRequestsController.cs:149-158](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs))، ولا قبل `Create` ولا `Renew` ([IssuedCardsController.cs:71, 272](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)). ولا فهرس فريد على الموظف أو الرقم التسلسلي ([IDCardDbContext.cs:34-52](../src/Services/IDCard/IDCard.Infrastructure/Data/IDCardDbContext.cs)).
  - **`Renew` على المفقودة يتكرر:** يُبطل الأصلية فقط إن كانت Active ([IssuedCardsController.cs:282-286](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs))، و`CanRenew` يقبل Lost مرة بعد مرة ([CardIssuanceWorkflow.cs:16-17](../src/Services/IDCard/IDCard.Domain/Services/CardIssuanceWorkflow.cs)). فكل نقرة تُنتج بطاقة نشطة جديدة.
  - **إعادة الإسناد:** `Edit` ينقل البطاقة إلى موظف آخر ([IssuedCardsController.cs:127](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)).
  - **الحالة من النموذج:** نموذج الإنشاء يعرض الحالة ([IssuedCards/Create.cshtml:13](../src/Services/IDCard/IDCard.Web/Views/IssuedCards/Create.cshtml))، والإجراء يربط الكيان مباشرة (:71).
  - **صيغتان للرقم وبطاقة بلا انتهاء:** `ProcessRequest` يولّد الرقم من GUID ([CardRequestsController.cs:155](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)) بدل COD-001 المستخدم في الإنشاء ([IssuedCardsController.cs:81](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs))، ولا يضبط تاريخ الانتهاء. فلا تنتهي البطاقة أبداً ([CardIssuanceWorkflow.cs:19-20](../src/Services/IDCard/IDCard.Domain/Services/CardIssuanceWorkflow.cs)).
  - **لا فحص للموظف:** IDCard لا يتحقق من وجود الموظف ولا من نشاطه، فيمكن إصدار بطاقة لموظف مفصول.
  - **القائمة والـ SLA غير مكتملين:** لا يُعلَّم من القائمة إلا `Requested` و`Issued` ([CardRequestsController.cs:64, 167](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)). و`IsRevokeSlaBreached` لا يستخدمه إلا الاختبار ([CardIssuanceWorkflow.cs:22-23](../src/Services/IDCard/IDCard.Domain/Services/CardIssuanceWorkflow.cs)). والإبطال بلا سبب ولا حدث (IssuedCardsController.cs:209-221). أما الوثيقة فتقول «CardChecklist Requested→Revoked» ([FUN_005_RECONCILIATION_CHECKLIST.md:5](FUN_005_RECONCILIATION_CHECKLIST.md)).
- **الأثر:** أكثر من بطاقة صالحة للشخص الواحد، وبطاقة A منسوبة إلى B، ولا أثر قابل للتدقيق لما بعد الإصدار.
- **الإصلاح المقترح:**
  - فهرس فريد لبطاقة نشطة واحدة لكل موظف، ولرقم تسلسلي لكل جهة.
  - `Renew` يُبطل المفقودة.
  - منع تغيير الموظف، ونموذج عرض بلا حالة.
  - COD-001 وتاريخ انتهاء في كل المسارات.
  - فحص نشاط الموظف عبر lookup.
  - تعليم Printed/Delivered/Revoked، وقائمة تجاوز SLA الإبطال.
- **التحقق المطلوب:**
  - **سالب:** تجديد بطاقة مفقودة مرتين ← الثانية مرفوضة، وبطاقة نشطة واحدة.
  - **سالب:** إصدار لموظف مفصول ← رفض.
  - **سالب:** `Edit` لموظف آخر ← رفض.

#### G-IDA-12 — سلسلة التوريد: تعديل بعد الاعتماد، واستلامات تكتب فوق بعضها، ومخزون بلا حركات

- **الدليل:**
  - **التعديل بعد الاعتماد:** `CanEditContent` يشمل `Approved` ([SupplyOrderWorkflowRules.cs:15-16](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/SupplyOrderWorkflowRules.cs)). فبعد الاعتماد يمكن تعديل البنود ([AdminExtendedControllers.cs:189](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AdminExtendedControllers.cs)) وإضافة بنود بأي كمية وكلفة (:213). والاعتماد نفسه بلا مبلغ ولا حد (:229-248).
  - **الاستلام يكتب فوق السابق:** الكمية المستلمة تُكتب فوق السابقة (`line.QtyReceived = qty`، [ReceiptInventoryService.cs:31](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Services/ReceiptInventoryService.cs)، [SupplyReceiptRules.cs:40](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/SupplyReceiptRules.cs))، بينما المخزون يتراكم (`item.Quantity += qty`، :38). ولا سقف مقابل الكمية المطلوبة. النتائج:
    - استلامان جزئيان (6 ثم 4 من 10) يتركان `QtyReceived = 4`، فلا يكتمل الأمر أبداً (SupplyReceiptRules.cs:11-16).
    - عدد الأصول المسموح بإنشائها يُحسب من 4 (:47-48).
    - إعادة الإرسال تُضاعف المخزون.
  - **تعديل الاستلام بلا تسوية:** تعديل سجل الاستلام يغيّر الأمر والصنف والكمية دون تسوية المخزون ([AdminExtendedControllers.cs:1006-1011](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AdminExtendedControllers.cs))، فينكسر نسب الأصل.
  - **المخزون بلا سجل حركات:** كمية المخزون تُعدَّل مباشرة بلا سبب ولا سجل حركات ([InventoryItemsController.cs:191](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/InventoryItemsController.cs))، والحذف نهائي (:209-213).
- **الأثر:** يمكن الحصول على اعتماد لطلب صغير ثم تضخيمه. والرصيد الدفتري لا يطابق الاستلامات. ولا أثر تدقيق لحركات المستودع.
- **الإصلاح المقترح:**
  - قفل الأمر بعد اعتماده، مع «تعديل يتطلب إعادة اعتماد».
  - استلامات تراكمية بسقف ومفتاح idempotency.
  - قفل سجل الاستلام بعد إنشاء أصول منه.
  - سجل حركات مخزون (استلام، صرف، تسوية بسبب ومعتمد) بدل تعديل الكمية.
- **التحقق المطلوب:**
  - **موجب:** استلامان جزئيان ← `QtyReceived = 10`، والأمر يكتمل، و10 خانات أصول.
  - **سالب:** إضافة بند بعد الاعتماد ← يعود الأمر إلى «بانتظار الاعتماد».
  - **سالب:** استلام فوق المطلوب ← رفض.

#### G-IDA-13 — العتبات المالية للأصل المميز ثابتة في الشيفرة بلا عملة ولا عصر نقدي

- **الدليل:**
  - **القيم الثابتة:** 10,000 و5,000 مكتوبة في shim العميل ([PolicyEnforceClient.cs:125, 146](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs))، وفي fallback الخادم ([PolicyStaticFallback.cs:55](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyStaticFallback.cs))، وفي القواعد المبذورة ([PolicyEngineSeeder.cs:81-95, 114](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs)). والفئة «أصول ثابتة» نص حرفي في الشيفرة ([AssetsController.cs:752, 767](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)).
  - **القيمة بلا عملة:** `Asset.Value` رقم `decimal?` بلا رمز عملة ولا عصر ([Asset.cs:31](../src/Services/AdminLogistics/Admin.Logistics.Domain/Entities/Asset.cs))، ومثله `SupplyOrderLine.UnitCost`.
  - **الواقع النقدي:** الليرة الجديدة = 100 قديمة، وهي الافتراضية منذ 31-7-2026 ([12-SYRIA-BASELINE-AND-SOURCES.md:24](completion-backlog/12-SYRIA-BASELINE-AND-SOURCES.md)، [13-SYRIA-PRODUCTION-CARDS.md:52](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md)).
  - **لا مصدر قانوني:** لا ذكر للعتبات في [legal-source-binding.md](syr/legal-source-binding.md).
- **الأثر:** الأصل نفسه مميز إن سُجّل بالليرة القديمة، وعادي إن سُجّل بالجديدة. وبالتالي قواعد الدليل والاعتماد وموافقة الفقد اعتباطية وغير قابلة للدفاع أمام الرقابة المالية.
- **الإصلاح المقترح:**
  - العتبات قيم مضبوطة لكل جهة بمصدر قانوني (D-IDA-3)، دون افتراضي في الإنتاج.
  - `CurrencyCode` و`CurrencyEra` على القيم، مع ترحيل القائم.
  - إعادة التصنيف بمراجعة صريحة.
- **التحقق المطلوب:**
  - **موجب:** أصل 2,000 ليرة جديدة وعتبة الجهة 1,500 ← مميز.
  - **سالب:** قيمة بلا عملة ← رفض.
  - **سالب:** الإنتاج دون عتبات مضبوطة ← health أحمر.

#### G-IDA-14 — دور واحد لكل وحدة، فلا فصل مهام في الإجراءات الحساسة

- **الدليل:**
  - **الأرقام:** في الخدمات الأربع 86 إجراء POST (IDCard 25، Admin.Logistics 28، IT 25، Auth 8). واحد فقط منها بسياسة صريحة ([DirectoryInternalSyncController.cs:11](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/DirectoryInternalSyncController.cs)). الباقي يعتمد على مرشح الوحدة: `admin` أو دور الوحدة الوحيد ([HrManagementAuthorizationExtensions.cs:194-200](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)؛ IDCard Program.cs:19، Admin.Logistics Program.cs:21، IT Program.cs:18، Auth Program.cs:19).
  - **IDCard:** المستخدم نفسه يطلب البطاقة ويعالجها ويعيد إسنادها ويبطلها. التعليق يتحدث عن «four-eyes comparison at approval time» ([CardRequestsController.cs:97-98](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs))، ولا يوجد أي فحص.
  - **Admin.Logistics:** المستخدم نفسه:
    - ينشئ أصلاً «قديماً/هبة» بلا استلام ([AssetsController.cs:210-258](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)).
    - يسلّمه، ويعلن فقد الأصل غير المميز دون اعتماد (:646-653).
    - يستردّه (:658-691)، ويلغيه (:695-720).
  - **قاعدة SoD غير مستعملة:** قاعدة `AssetAssignment` (منشئ `admin-logistics`، مدقق `hr-management-approver`) في [SodMatrixDefaults.cs:134-137](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs) لا تستعملها Admin.Logistics.
- **الأثر:** موظف لوجستيات واحد يستطيع إدخال أصل «هبة» ثم تسليمه ثم إعلان فقده ثم إلغاءه دون شاهد ثانٍ. وموظف بطاقات واحد يصدر بطاقة لأي شخص.
- **الإصلاح المقترح:**
  - قائمة إجراءات حساسة لكل وحدة بقرار (D-IDA-8).
  - أدوار فرعية للمنفّذ والمعتمد، وسياسات صريحة.
  - قاعدة «المنفّذ ≠ المعتمد» على الخادم.
- **الخطة:** [PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md) (جزئياً: سياسة `{Module}.Approve` صريحة على الإجراءات الحساسة في IDCard وIT وAdmin.Logistics وAuth.Directory)، و[PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) (جزئياً: إصدار البطاقة بعد اعتماد غير طالبها). والأدوار الفرعية و«المنفّذ ≠ المعتمد»: الموجة 2
- **التحقق المطلوب:**
  - **سالب:** معالج الطلب هو طالبه ← رفض.
  - **سالب:** فقد أصل أو إلغاؤه بلا معتمد ثانٍ ← يبقى معلّقاً.
  - **موجب:** معتمد مختلف ينجح.

### 4.3 قدرات ناقصة (P2)

#### G-IDA-15 — قدرات اللوجستيات الحكومية الأساسية غير موجودة

- **الدليل:**
  - **لا وحدة مالكة ولا موقع:** الأصل بلا وحدة مالكة ولا موقع. الأصل غير المسلَّم يُمنع عن أي مستخدم فرع ([AssetOrgScopeGuard.cs:41-57](../src/Services/AdminLogistics/Admin.Logistics.Web/Helpers/AssetOrgScopeGuard.cs))، والفهرس يعرض للفرع الأصول المسلَّمة لموظفيه فقط ([AssetsController.cs:97-108](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)). فلا يرى أمين مستودع الفرع مستودعه.
  - **لا سند صرف:** لا «سند صرف/مذكرة إخراج» للمواد الاستهلاكية. المخزون يزيد بالاستلام فقط ([ReceiptInventoryService.cs:38](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Services/ReceiptInventoryService.cs)) ويُنقص بالتعديل اليدوي.
  - **لا لجنة شطب:** لا مسار «لجنة شطب/إتلاف». الاستبعاد يتم بتعديل الحالة (G-IDA-10).
  - **لا جرد:** لا جرد دوري ولا تسوية فروق.
  - **الأختام بلا عهدة:** الأختام الرسمية سجلات بلا حائز ولا تسليم ولا سجل استخدام، وتُحذف نهائياً ([IDCardExtendedControllers.cs:146-163](../src/Services/IDCard/IDCard.Web/Controllers/IDCardExtendedControllers.cs)).
  - **لا خطة توزيع لوجستية:** «خطط التوزيع» المذكورة في النطاق غير موجودة في اللوجستيات. `DistributionPlan` الوحيد خطة توزيع موظفين في HR.Movements ([DistributionPlan.cs](../src/Services/HRMovements/HR.Movements.Domain/Entities/DistributionPlan.cs)).
- **الأثر:** الجهة لا تستطيع تشغيل مستودع فرع، ولا صرف قرطاسية لموظف، ولا شطب أصل تالف بقرار لجنة، ولا جرداً سنوياً. وهذه عمليات يومية في أي إدارة حكومية.
- **الإصلاح المقترح:** بقرار نطاق (D-IDA-7):
  - `OwnerOrgUnitId` و`Location` للأصل، مع نطاق عليهما.
  - سند صرف مرتبط بسجل الحركات.
  - مسار شطب بلجنة واعتماد.
  - جرد بقائمة وفروق معتمدة.
  - عهدة الأختام بمسار العهدة نفسه.
- **التحقق المطلوب:**
  - **موجب:** أمين فرع يرى مستودعه ويصرف 10 وحدات، فينقص الرصيد بحركة موقعة.
  - **موجب:** شطب أصل يمر بلجنة.
  - **سالب:** أمين فرع آخر ← 403.

#### G-IDA-16 — البطاقة بلا طباعة ولا تحقق، وصلاحيات الدخول نصوص بلا تكامل

- **الدليل:**
  - **الطباعة طابع زمني:** «الطباعة» تسجّل وقتاً فقط ([IssuedCardsController.cs:138-158](../src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs)). لا QuestPDF في IDCard، ولا صورة على البطاقة، ولا رمز QR ولا نقطة تحقق.
  - **صلاحيات الدخول ومجموعاتها:** `AccessPermission` و`CardPermissionSet` نصوص حرة. لا يستعمل المجموعات أي سطر خارج CRUD، ولا تُصدَّر إلى نظام دخول.
  - **لا أثر للإبطال على الدخول:** لا حدث إبطال (لا عقد `ICardRevoked`)، ولا انتهاء آلي، و`MarkExpired` يدوي (:255).
  - **الإيقاف لا يمس البطاقة:** إيقاف الموظف (`IEmployeeStatusChanged`) لا يمسها، إذ يستهلكه Auth.Directory وحده.
- **الأثر:** البطاقة الصادرة لا تُطبع من النظام. وإبطالها لا يصل إلى البوابات الفعلية، فيبقى الدخول المادي ممكناً.
- **الإصلاح المقترح:**
  - قالب PDF بالصورة ورمز QR يشير إلى نقطة تحقق عامة محدودة.
  - حدث إبطال، وتصدير قائمة الإبطال إلى أنظمة الدخول.
  - مهمة انتهاء، ورد فعل على الإيقاف. كل ذلك بحسب D-IDA-6.
- **التحقق المطلوب:**
  - **موجب:** طباعة PDF عربي برمز QR.
  - **سالب:** مسح QR لبطاقة مُبطلة ← «غير صالحة».

#### G-IDA-17 — تقنية المعلومات: لا تخصيص أجهزة، وسجلان للأجهزة، وحوادث بلا ساعة

- **الدليل:**
  - **لا تخصيص أجهزة:** التعيين ينشئ تذكرة فقط ([EmployeeCreatedConsumer.cs:43-56](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)).
  - **سجلان للأجهزة:** `UserDevice` في IT و`Asset` في اللوجستيات، بلا ربط. والفصل يعالج كلاً منهما وحده ([PartyAckConsumers.cs:22-26](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs)، [PartyAckConsumers.cs:13-30](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - **الجهاز بعد الفصل:** فك الربط (`AssignedTo = null`) يُبقي الحالة `InUse`، بلا مهمة استرداد ولا مسح بيانات.
  - **سجل الصلاحيات التقنية:** `UserId` حقل GUID حر بلا منتقٍ ولا ربط بـ Keycloak، فالسحب عند الفصل علامة محلية.
  - **الحوادث الأمنية:** بلا ساعة إخطار ولا تصعيد، وتُحذف نهائياً ([SecurityIncidentsController.cs:77-87](../src/Services/ITInfrastructure/IT.Infra.Web/Controllers/SecurityIncidentsController.cs)).
  - **لا فحوص دورية:** لا خدمة خلفية في الخدمات الأربع لفحص الحسابات اليتيمة أو الخاملة التي يعد بها IAM-005.
- **الأثر:** الموظف الجديد لا يحصل على جهاز عبر النظام. والجهاز المسلَّم يظهر في سجلين قد يتناقضان. والحادثة الأمنية بلا أثر قابل للتدقيق.
- **الإصلاح المقترح:**
  - طلب جهاز ضمن التعيين.
  - ربط `UserDevice` بـ `Asset` أو توحيدهما.
  - حالة «بانتظار الاسترداد» عند الفصل.
  - ساعة حادثة وحذف ناعم.
  - مهمة فحص الحسابات (مع G-IDA-01).
- **التحقق المطلوب:**
  - **موجب:** تعيين ← طلب جهاز ← تسليم ← الجهاز يظهر عهدةً للموظف في السجلين.
  - **سالب:** حذف حادثة ← رفض.

### 4.4 فجوات التحقق والتوثيق

#### G-IDA-18 — ادعاءات Done وPass غير مدعومة بأدلة تمارس المسار

- **الدليل:**
  - **FUN-005:** «Done» و«ReadyForSign-off» ([FUN_005_VERIFICATION.md:13](FUN_005_VERIFICATION.md)، [FUN_005_UAT_PACK.md:18](FUN_005_UAT_PACK.md)، [04-P3:209](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md)). سندها اختبارات وحدة لقواعد ثابتة (`Fun005IdentityAssetsItTests`)، وE2E يفحص رفض الطلب المجهول فقط ([Fun005IdentityAssetsItE2ETests.cs:12-29](../src/Tests/HRManagement.E2E/Fun005IdentityAssetsItE2ETests.cs)).
  - **AC-33:** يفتح صفحة الأصل ويقبل 200 أو 403 ([Ac33AssetLossLiveTests.cs:31](../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs))، ويكتب ملاحظة ثابتة ثم `Pass` (:36-44). الدليل طلب GET واحد بلا فحص قاعدة. وقد رصده جرد PLAN-33 ([plan33-acceptance-inventory.md:123](../reports/plan33-acceptance-inventory.md))، ومع ذلك يبقى `Pass` في [summary.md](../acceptance-evidence/summary.md).
  - **AC-32:** يرسل GET فقط ([Ac32LogisticsScopeLiveTests.cs:25-46](../src/Tests/HRManagement.AcceptanceTests/Ac32LogisticsScopeLiveTests.cs))، رغم أن PLAN-12 سمّى إجراءات POST ([PLAN-12:12, 17](acceptance-plans/PLAN-12-logistics-custody.md)). فلم يكتشف IDOR الإرجاع (G-IDA-10).
  - **AC-11:** لا يفحص جانب اللوجستيات (G-IDA-02).
  - **اختبار mass-assignment يتخطى ثلاث تجميعات:** يحاول تحميل `IDCard.Web` و`IT.Infra.Web` و`Auth.Directory.Web` ويتخطاها بصمت ([MassAssignmentArchitectureTests.cs:43-58](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs))، لأن مشروع الاختبار لا يشير إليها ([HRManagement.UnitTests.csproj:30-54](../src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj)). وفيها إجراءات تربط كيانات مباشرة، مثل `IssuedCardsController.Create(IssuedCard)` (:71) و`SupportTicketsController.Create(ITSupportTicket)` (:41) و`RolesController.Create(Role)` (:18).
  - **مسار Keycloak الحقيقي لم يُمارَس:** لا بيئة قبول تفعّل المزامنة (G-IDA-01)، وIAM-005 «Done» باختبار JSON.
  - **وثائق مخالفة للشيفرة:**
    - `FUN_005_RECONCILIATION_CHECKLIST.md:5, 13, 15` (القائمة كاملة؛ التزويد؛ إعادة المحاولة).
    - `FUN_005_JOURNEY_MATRIX.md:26` (التصنيف من محرك السياسات، وهو في الإنتاج shim ثابت).
    - `ADR-FUN-005-EXT.md:22` («IsPrivileged أو Value > 10_000»، بينما `AssetApprovalRules.RequiresApproval` = `IsPrivileged` فقط، [AssetApprovalRules.cs:8](../src/Services/AdminLogistics/Admin.Logistics.Domain/Services/AssetApprovalRules.cs)).
    - `ADR-FUN-005-EXT.md:43` يقرّ بأن العهدة «تبقى معلّقة» عند سوء الإعداد.
- **الأثر:** يبدو المجال مُغلقاً وجاهزاً للتوقيع، بينما أربعة مسارات أساسية لا تعمل في الإنتاج.
- **الإصلاح المقترح:**
  - حزمة قبول `AC-IDA-*` بـ `EvidenceRecorder` و`[RequiredAcceptance]`، وفحوص قاعدة لكل سيناريو.
  - إعادة كتابة AC-32 بطلبات POST وحالة IDOR، وAC-33 بفقد واعتماد ولقطة فعلية.
  - ملف قبول شبيه بالإنتاج مع Keycloak ومزامنة مفعّلة.
  - تصحيح الوثائق وحالة البطاقات (FUN-005، IAM-005).
- **التحقق المطلوب:** كل سيناريو موجب وسالب في §6 له مجلد أدلة كامل بعناصره الثمانية، ولا اختبار ينجح على 403 أو 404 أو دون بيانات.

### 4.5 تقاطعات مع الدراسات الأخرى

- **→ WF:** دور الخطوة الأولى في `ASSET-ASSIGN-APPROVAL` غير محسوم:
  - الحزمة الحالية: `hr-core` ثم `hr-management-approver` ([WorkflowPackSeeder.cs:23-25](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).
  - البذرة القديمة غير المستدعاة: `admin-logistics` ([AssetAssignmentWorkflowSeeder.cs:24](../src/Services/HRCore/HR.Core.Infrastructure/Data/AssetAssignmentWorkflowSeeder.cs)).
  - مصفوفة SoD: `admin-logistics` منشئاً و`hr-management-approver` مدققاً ([SodMatrixDefaults.cs:134-137](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs)).
  - ونوعا الأصول خارج `EnforceStepRoleEntityTypes` ([appsettings.Production.json:262-270](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)).
- **→ WF:** خطوة `ASSET-LOSS-APPROVAL` تتطلب `admin-logistics` ([WorkflowPackSeeder.cs:26-28](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs))، وهذا الدور لا يصل إلى HR.Core. والمبادر يُسجَّل حساب الخدمة ([WorkflowInstancesController.cs:89](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs))، فيقارن فحص «المبادر ≠ المعتمد» بحساب آلي.
- **→ WF:** تعريفان يتيمان: `ID-CARD-ISSUANCE` بدور `security` غير موجود، و`IT-TICKET` بدور `it-support` ([WorkflowPackSeeder.cs:35, 47](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)). كلاهما في قائمة استثناء [WorkflowDefinitionsHaveHandlersTests.cs:19-22](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs).
- **→ OPS:** إقرارات الأطراف تُنشر من `ConsumeContext` ([PartyAckConsumerBase.cs:30-38](../src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs))، وهذه الخدمات لا تهيّئ إلا bus outbox ([MassTransitExtensions.cs:112-120](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)). أما IT وAuth وAdmin.Logistics فلا outbox لها أصلاً (IDCard وحده: Program.cs:44).
- **→ OPS:** في `deploy/k8s` لا تحصل Admin.Logistics وIT على `Integration__HrCoreBaseUrl` ولا `Services__AuthDirectory__BaseUrl` ([configmap-app.yaml](../deploy/k8s/base/configmap-app.yaml)). فتبقى القيم الافتراضية `localhost` ([appsettings.json:36](../src/Services/AdminLogistics/Admin.Logistics.Web/appsettings.json)، [appsettings.json:32](../src/Services/ITInfrastructure/IT.Infra.Web/appsettings.json)). يحتاج تحقق أيّ مسار نشر هو المعتمد.
- **→ OPS:** الحادثة الأمنية بلا ساعة إخطار، بينما SYR-016 يعلن «Unified incident clock» منجزاً ([SYR_016_CYBERSECURITY.md:55](SYR_016_CYBERSECURITY.md)). ومسار كتالوج النسخ ([BackupJobsController.cs:71-72](../src/Services/ITInfrastructure/IT.Infra.Web/Controllers/BackupJobsController.cs)) غير مركّب في `it-infra-web` ([docker-compose.production.yml:451-471](../docker-compose.production.yml)).
- **→ IAM:** لا نطاق فرع في IDCard وIT وAuth.Directory، ولا في متحكمات اللوجستيات غير العهدة (أوامر التجهيز، الاستلام، المخزون، الصيانة). و85 إجراء POST من 86 بلا سياسة صريحة (G-IDA-14). يلزم اختبار معماري مماثل لـ `HrCorePostActionsHaveExplicitPolicyTests`.
- **→ IAM:** IAM-005 «Done» ([10-NATIONAL-CONTROL-SUPPLEMENT.md:28](completion-backlog/10-NATIONAL-CONTROL-SUPPLEMENT.md)) واختباره يقرأ JSON ([Iam005LifecycleTests.cs:50-60](../src/Tests/HRManagement.UnitTests/Iam005LifecycleTests.cs)).
- **→ QA:** `MassAssignmentArchitectureTests` يتخطى ثلاث تجميعات ويب ([MassAssignmentArchitectureTests.cs:43-58](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs)). وAC-33 فارغ ويُعدّ Pass. و`LogisticsCustodyScopeTests` نصي ([:29-35](../src/Tests/HRManagement.UnitTests/Architecture/LogisticsCustodyScopeTests.cs)).
- **→ QA / FUN-003:** `ISeparationCompleted` و`ITransferExecuted` بالإصدار 3، ومستهلكوهما في الخدمات الأربع يقبلون حتى 2. منها [IDCard SeparationCompletedConsumer.cs:17](../src/Services/IDCard/IDCard.Infrastructure/Consumers/SeparationCompletedConsumer.cs) و[IT SeparationCompletedConsumer.cs:27](../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/SeparationCompletedConsumer.cs). شبكات الأمان قد تُتجاهل كلها؛ يحتاج تحقق، كما في G-MOV-08.
- **→ POL:** shim العميل ([PolicyEnforceClient.cs:115-176](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)) نسخة منحرفة عن fallback الخادم. الخادم يرفض أصلاً بلا مصدر ([PolicyStaticFallback.cs:36-46](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyStaticFallback.cs))، والعميل لا يرفض. والنطاقات `Assets.Custody` و`IDCard.Lifecycle` و`IT.Tickets` تعمل Off في الإنتاج.
- **→ SEP:** قرار «براءة الذمة» (D-IDA-1) يحدد هل يُحجز إكمال الفصل أو التسوية المالية بالعهد المفتوحة. ونشر `ISeparationCompleted` بعد كل الأطراف ([SeparationStateMachine.cs:129, 263](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs)) يجعل شبكات الأمان لاحقة دائماً.

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-IDA-1 | هل يُشترط إرجاع العهدة فعلياً (براءة ذمة) قبل إكمال الفصل؟ | لا يُحجز الفصل. يكتمل طرف اللوجستيات بقائمة العهد المفتوحة، وتُحجز براءة الذمة والتسوية النهائية حتى الإرجاع أو تحميل القيمة بقرار | IDA-D، SEP |
| D-IDA-2 | مصير العهدة عند النقل | لا إرجاع تلقائي. مهمة تسليم للوحدة المصدر بمهلة (مثلاً 7 أيام)، والمميزة بدليل | IDA-D |
| D-IDA-3 | عتبات «الأصل المميز» واعتماد التسليم والفقد وعملتها | قيم لكل جهة بالليرة الجديدة من مرجع مالي معتمد، مربوطة بالمصدر القانوني، ودون افتراضي في الإنتاج | IDA-B، IDA-E |
| D-IDA-4 | من يعتمد تسليم العهدة المميزة وفقدها؟ (مع WF) | الخطوة 1 مدير اللوجستيات عبر صندوق العمل، والخطوة 2 `hr-management-approver` للفقد ذي الأثر المالي | IDA-B |
| D-IDA-5 | تفعيل مزامنة Keycloak في الإنتاج، وSLA التعطيل، والدور الافتراضي | تفعيل إلزامي، وتعطيل الدخول خلال ساعة أو أقل، والدور الافتراضي `hr-employee` فقط | IDA-A |
| D-IDA-6 | نطاق البطاقة في v1 (طباعة، QR، ربط أنظمة الدخول، رسم بدل فاقد) | PDF ورمز QR للتحقق في v1. الربط بأنظمة الدخول بملف تصدير لاحقاً، والرسوم خارج v1 | IDA-I |
| D-IDA-7 | نطاق اللوجستيات في v1 (سند صرف، لجنة شطب، جرد، عهدة الأختام) | سند الصرف ولجنة الشطب والجرد السنوي في v1. الأختام بمسار العهدة نفسه | IDA-H |
| D-IDA-8 | فصل المهام داخل الوحدات (أدوار فرعية) | منفّذ ومعتمد لكل وحدة في الإجراءات المسماة في G-IDA-14 | IDA-F |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   IDA-A (Keycloak: الفصل والتعيين) ║ IDA-B (اعتماد العهدة المميزة) ║ IDA-C (رحلة البطاقة)
الموجة 2 (P1)   IDA-D (العهدة في الفصل والنقل والتعويض) ║ IDA-E (آلة حالة العهدة والتوريد والعتبات)
الموجة 3 (P1)   IDA-F (التنقل والأدوار وفصل المهام)
الموجة 4 (P2)   IDA-H (قدرات اللوجستيات) ║ IDA-I (البطاقة والأجهزة والحوادث) — بقرار
مستمر           IDA-G (القبول الحي وتصحيح الوثائق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|---------|---------|----------|-----------|-------------------|
| IDA-A | تعطيل الوصول عند الفصل وتزويد الحساب في Keycloak | G-IDA-01، G-IDA-07 (جزء Auth وIT)، جزء G-IDA-09 (الدور الافتراضي) | P0 | D-IDA-5 | 7–9 |
| IDA-B | إغلاق حلقة اعتماد العهدة المميزة والفقد | G-IDA-02 | P0 | D-IDA-4، دراسة WF | 6–8 |
| IDA-C | رحلة البطاقة من الطلب إلى التسليم والاستبدال | G-IDA-03، G-IDA-04، G-IDA-11 | P0/P1 | — | 6–8 |
| IDA-D | العهدة والصلاحيات في الفصل والنقل والتعويض | G-IDA-05، G-IDA-06 | P1 | D-IDA-1، D-IDA-2، IDA-B | 6–8 |
| IDA-E | آلة حالة العهدة وسلسلة التوريد والعتبات المالية | G-IDA-10، G-IDA-12، G-IDA-13 | P1 | D-IDA-3 | 7–9 |
| IDA-F | التنقل وحوكمة الأدوار وفصل المهام | G-IDA-08، G-IDA-09، G-IDA-14 | P1 | IDA-A، D-IDA-8 | 7–9 |
| IDA-G | القبول الحي وتصحيح الوثائق | G-IDA-18 | P1، ثم مستمر | كل الخطط | 5–6 |
| IDA-H | قدرات اللوجستيات الحكومية | G-IDA-15 | P2 | D-IDA-7، IDA-E | 8–10 |
| IDA-I | البطاقة المطبوعة والأجهزة والحوادث | G-IDA-16، G-IDA-17 | P2 | D-IDA-6، IDA-C | 7–9 |
| **المجموع** | | | | | **≈ 59–76** |

**IDA-A — تعطيل الوصول عند الفصل وتزويد الحساب في Keycloak.**
- طرف الفصل في Auth.Directory يعطّل المستخدم في Keycloak، وينهي الجلسات، ويحذف أدوار realm، ويسجّل `IdpDisabledAt`. عند الفشل يُعلن Faulted.
- شبكتا الأمان تفحصان حالة IdP لا العلم المحلي.
- التعيين: حالة تزويد على `AuthUser`، وإعادة محاولة حقيقية، وطرف التعيين يشترط `KeycloakUserId`.
- `DefaultRealmRoles = hr-employee`، وفحص إعداد يمنع الأدوار المميزة، وفحص إقلاع يمنع الإنتاج دون مزامنة.
- مهمة مصالحة يومية للحسابات اليتيمة.
- السيناريوهات:
  - AC-IDA-Aa: الفصل يعطّل الدخول وينهي الجلسة.
  - AC-IDA-Ab: Keycloak متوقف ← تدخل، ثم استئناف.
  - AC-IDA-Ac: التعيين ينشئ حساباً بدور `hr-employee` فقط.
  - AC-IDA-Ad: فشل التزويد يظهر ويُعاد.
  - AC-IDA-Ae: الإنتاج بلا مزامنة ← health أحمر.

**IDA-B — إغلاق حلقة اعتماد العهدة المميزة والفقد.**
- حفظ بعد نشر نتائج الأصول.
- `AssetAssignment` و`AssetLossReport` في صندوق العمل، برابط إلى صفحة الأصل، وتصحيح الرابط المكسور.
- أدوار الخطوات وفرضها بالتنسيق مع WF.
- تمرير المبادر البشري.
- «سحب الطلب» و«إعادة بدء المسار»، ومهمة مصالحة للمعلّق بلا نسخة.
- تسجيل هوية معتمد التسوية بدل `SourceSystem` ([AssetLossWorkflowConsumers.cs:44-45](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/AssetLossWorkflowConsumers.cs)).
- السيناريوهات:
  - AC-IDA-Ba: تسليم مميز باعتمادين من الواجهة ← `Active`.
  - AC-IDA-Bb: الرفض يحرّر الأصل.
  - AC-IDA-Bc: HR.Core متوقف ← المصالحة تبدأ المسار.
  - AC-IDA-Bd: فقد مميز ← اعتماد ← Lost مع لقطة «بعد» واسم المعتمد.
  - AC-IDA-Be: سحب الطلب.
  - AC-IDA-Bf: المنشئ لا يعتمد.

**IDA-C — رحلة البطاقة من الطلب إلى التسليم والاستبدال.**
- `ProcessRequest` ينشر `ICardIssued` عبر الـ outbox، برقم COD-001 وتاريخ انتهاء وتصميم صريح.
- checklist للطلبات التلقائية، وتعليم كل خطوات القائمة.
- نماذج الواجهة للتسليم والفقد والتجديد والإبطال العاجل والإلغاء.
- بطاقة نشطة واحدة لكل موظف بفهرس فريد، ورقم تسلسلي فريد.
- منع تغيير الموظف، ونموذج عرض للإنشاء.
- فحص نشاط الموظف، وقائمة تجاوز SLA الإبطال.
- السيناريوهات:
  - AC-IDA-Ca: تعيين ← طلب ← معالجة ← `IdentityIssued` ← بدء ملحمة التعيين.
  - AC-IDA-Cb: تسليم بلا إيصال يُرفض، وبإيصال ينجح.
  - AC-IDA-Cc: فقد ← تجديد ← بطاقة نشطة واحدة.
  - AC-IDA-Cd: تجديد مزدوج يُرفض.
  - AC-IDA-Ce: إصدار لمفصول يُرفض.

**IDA-D — العهدة والصلاحيات في الفصل والنقل والتعويض.**
- الفصل والنقل ينشئان «طلبات إرجاع» بدل الإغلاق الصامت، وتُلغى العهد المعلّقة.
- اتساق `Status` و`ReturnedAt`، وعدم لمس الأصل Lost أو Disposed.
- كل تغيير يحمل معرّف الملحمة، والتعويض يعكس هذه التغييرات فقط، في Admin.Logistics وIDCard وIT.
- قبول عقود الإصدار 3 بعد مراجعة الحقول.
- حالة «بانتظار الاسترداد» لأجهزة IT.
- السيناريوهات:
  - AC-IDA-Da: الفصل يُنتج قائمة إرجاع، والأصول لا تتغير.
  - AC-IDA-Db: النقل يُنتج مهمة تسليم.
  - AC-IDA-Dc: إلغاء الفصل يعيد ما غيّره فقط.
  - AC-IDA-Dd: بطاقة مسروقة قبل الفصل تبقى مُبطلة.
  - AC-IDA-De: عهدة معلّقة عند الفصل لا تُفعَّل لاحقاً.

**IDA-E — آلة حالة العهدة وسلسلة التوريد والعتبات المالية.**
- تحقق تطابق العهدة والأصل في الإرجاع.
- انتقالات صريحة بدل تعديل الحالة، والصيانة بدخول وخروج، وتثبيت التصنيف بعد التسليم.
- قفل أمر التجهيز بعد الاعتماد، واستلامات تراكمية بسقف وidempotency، وقفل سجل الاستلام.
- سجل حركات المخزون.
- عتبات مضبوطة لكل جهة بعملة وعصر نقدي، وترحيل القيم.
- السيناريوهات:
  - AC-IDA-Ea: IDOR الإرجاع ← رفض.
  - AC-IDA-Eb: `Edit` لا يغيّر الحالة.
  - AC-IDA-Ec: استلامان جزئيان يكتملان.
  - AC-IDA-Ed: بند بعد الاعتماد يعيد الاعتماد.
  - AC-IDA-Ee: عتبة الجهة بالليرة الجديدة تصنّف الأصل.

**IDA-F — التنقل وحوكمة الأدوار وفصل المهام.**
- مستهلكا النقل والتعيين الداخلي يحدّثان النطاق في `AuthUser` وKeycloak، ويسحبان أدوار الوحدة المصدر، وينهيان الجلسات.
- حذف الأدوار في Keycloak.
- منح الدور الحساس بطلب يعتمده شخص ثانٍ بفعل مستقل، ومقارنة المعرّفات بـ `KeycloakUserId`، وفحص الرتبة دائماً.
- منع إعادة تسمية دور له حاملون.
- أدوار فرعية للمنفّذ والمعتمد، وسياسات صريحة في الوحدات الأربع.
- السيناريوهات:
  - AC-IDA-Fa: مدير فرع منقول لا يرى فرعه القديم.
  - AC-IDA-Fb: منح الدور للنفس يُرفض.
  - AC-IDA-Fc: `hr-admin` يبقى معلّقاً حتى يعتمده الثاني.
  - AC-IDA-Fd: معالج طلب البطاقة ليس طالبه.
  - AC-IDA-Fe: إلغاء أصل يحتاج معتمداً ثانياً.

**IDA-G — القبول الحي وتصحيح الوثائق.**
- حزمة `AC-IDA-*` بـ `EvidenceRecorder` و`[RequiredAcceptance]` وفحوص قاعدة.
- AC-32 بطلبات POST وحالة IDOR، وAC-33 بفقد واعتماد ولقطة فعلية.
- ملف قبول شبيه بالإنتاج فيه Keycloak بمزامنة مفعّلة.
- إشارة مشروع الاختبار إلى التجميعات الثلاث (→ QA).
- تصحيح `FUN_005_*` وADR وحالة FUN-005 وIAM-005.
- السيناريوهات: AC-IDA-Ga (لا اختبار ينجح على 403 أو 404)، وAC-IDA-Gb (رحلة JML كاملة، انظر §7).

**IDA-H — قدرات اللوجستيات الحكومية.**
- `OwnerOrgUnitId` و`Location` مع النطاق.
- سند صرف مرتبط بسجل الحركات.
- لجنة شطب، وجرد سنوي بتسوية فروق، وعهدة الأختام.
- السيناريوهات: AC-IDA-Ha (صرف ينقص الرصيد بحركة)، وAC-IDA-Hb (شطب بلجنة)، وAC-IDA-Hc (جرد بفروق معتمدة)، وAC-IDA-Hd (أمين فرع آخر ← 403).

**IDA-I — البطاقة المطبوعة والأجهزة والحوادث.**
- قالب PDF بالصورة ورمز QR، ونقطة تحقق، وتصدير قائمة الإبطال.
- رد فعل على الإيقاف.
- طلب جهاز ضمن التعيين، وربط `UserDevice` بالأصل.
- ساعة الحادثة وحذف ناعم.
- السيناريوهات: AC-IDA-Ia (طباعة PDF عربي)، وAC-IDA-Ib (QR لبطاقة مُبطلة ← غير صالحة)، وAC-IDA-Ic (جهاز التعيين في السجلين)، وAC-IDA-Id (حذف حادثة ← رفض).

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والأدلة. أكبر مصادر عدم اليقين ثلاثة: نتيجة تفعيل Keycloak الحقيقي في بيئة القبول، وسلوك `SchemaVersion` الفعلي، وقرارات D-IDA-1 وD-IDA-3 وD-IDA-7.

---

## 7. معيار «اكتمال الهوية والأصول وتقنية المعلومات» المقترح

يُعدّ المجال مكتملاً لإصدار v1 حين تتحقق البنود الأربعة الآتية:

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-IDA-01…G-IDA-14 وG-IDA-18)، أو يُؤجَّل ما يُؤجَّل بقرار موثّق.
- [ ] **2. تنجح رحلة JML كاملة على ملف شبيه بالإنتاج** (`IsLabScale=false`، النطاقات الثلاثة Off، Keycloak حقيقي بمزامنة مفعّلة):
  - تعيين، ثم طلب بطاقة ومعالجته، ثم اكتمال `IdentityIssued` وبدء ملحمة التعيين.
  - تسليم البطاقة بإيصال، ثم حساب Keycloak بدور `hr-employee` فقط.
  - تسليم أصل مميز باعتمادين من صندوق العمل.
  - نقل: يتغيّر النطاق، وتنشأ مهمة تسليم العهدة.
  - فصل: رفض الدخول خلال ساعة أو أقل، وإبطال البطاقة، وقائمة عهد مفتوحة.
  - إلغاء فصل تجريبي: يعكس ما غيّره فقط.
- [ ] **3. توجد أدلة `acceptance-evidence/AC-IDA-*`** لكل سيناريو موجب وسالب في §6، مع فحوص قاعدة. ويُعاد تشغيل AC-32 (بطلبات POST وحالة IDOR) وAC-33 (فقد واعتماد ولقطة) حيّاً، ولا اختبار ينجح على 403 أو 404 أو دون بيانات.
- [ ] **4. تُصحَّح الوثائق لتطابق الأدلة:** `FUN_005_VERIFICATION.md` و`FUN_005_UAT_PACK.md` و`FUN_005_RECONCILIATION_CHECKLIST.md` و`FUN_005_JOURNEY_MATRIX.md` وADR-FUN-005/EXT وحالة FUN-005 في `04-P3` وحالة IAM-005 في `10-NATIONAL-CONTROL-SUPPLEMENT.md`.
