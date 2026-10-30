# PLAN-52 — نطاق الفرع في كل ما يصله المستخدم المقيّد، واختبار BOLA بالانعكاس (IAM-D)

**Scope:** IAM — [دراسة الفجوات](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-04 (ومعه جزء «المنقول يحتفظ بنطاقه» من G-IAM-11)  
**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-37 (الملف الشبيه بالإنتاج ولقطات الوحدات في الخدمات)، PLAN-51 (مشروع `HRManagement.ArchitectureTests` وسياسات الكتابة الصريحة)؛ ويبني على PLAN-34 (نطاق القوائم الاثنتي عشرة `Index(employeeId)`) وعلى PLAN-43 (تغييرات `WorkflowInstancesController`)  
**Related cards:** IAM-002 (نطاق الوحدة)، PLAN-25 D10 (`RecruitmentOrgScopeFilter`)، AC-01..06، HCM-002 (→ IAM: أرصدة الإجازة والتصدير)، D-IAM-3

## Goal

- مدير الفرع (`branch-manager` + `hr-core`، وفي اللوجستيات `admin-logistics`) لا يقرأ ولا يكتب سجلاً لفرع آخر في أي شاشة يصلها اليوم: HR.Core كلها، ومعالج التعيين في HR.Contracts، وAdmin.Logistics.
- السجل خارج النطاق يعامَل كأنه غير موجود: 404 للتفاصيل وPOST، ولا يظهر في القوائم والتصدير، ولا يتغير شيء في القاعدة.
- آلية واحدة عامة (مرشح ومسجّل مفاتيح نطاق) بدل فحوص يدوية متفرقة، واختبار انعكاسي يفشل إن ظهر متحكم بلا موقف نطاق معلن.
- المستخدم المقيّد الذي يُنقل يفقد نطاقه القديم (fail-closed) حتى يُعيَّن له نطاق جديد.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-IAM-3 — نموذج النطاق | يبقى مربوطاً بدور `branch-manager` كما اليوم ([HrOrgScopeVisibility.cs:143-147](../../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs)). جعله خاصية مستقلة تقيّد أي دور في الموجة 2 (IAM-H) | الآلية العامة هنا تعمل مع أي نموذج، لأنها تقرأ `OrgAccessScope` لا الدور |
| D-52-1 — سجل بلا مفتاح نطاق | مركزي: لا يراه المقيّد ولا يكتبه (كما فعل PLAN-25 للطلبات بلا شاغر) | سجلات قديمة بلا وحدة تختفي من شاشات الفروع |
| D-52-2 — كيانات بلا عمود وحدة (`DiwanCorrespondence`، `FieldInspectionVisit`) | يضاف `OrgUnitId`. يُختم عند الإنشاء من الموظف المرتبط، وإلا من نطاق المنشئ المقيّد، وإلا يختاره المركزي. والصفوف القديمة: من الموظف المرتبط إن وُجد، وإلا تبقى فارغة (مركزية) | ترحيل بيانات صغير |
| D-52-3 — البيانات المرجعية في اللوجستيات (`InventoryItems`، `Vendors`) والمناوبات (`Shifts`) | قراءة للجميع، وكتابة للمركزي فقط (المقيّد ← 403) | لا يعدّل مدير فرع المخزون المركزي أو الموردين |
| D-52-4 — نقل مستخدم مقيّد | fail-closed: يُحذف `scope_org_unit_id` من حسابه (محلياً، وفي Keycloak حين تعمل المزامنة)، فيصير نطاقه `InvalidClaim` ← 403 حتى يعيّن مسؤول الدليل نطاقاً جديداً. لا يُنزع دور `branch-manager` آلياً، لأن نزعه وحده يجعل حامل `hr-core` غير مقيّد | المدير المنقول يتوقف حتى يُعاد تعيينه، ولا يحتفظ بفرعه القديم |
| D-52-5 — رمز الاستجابة خارج النطاق | 404 للتفاصيل وPOST بمعرّف، و403 حين يكون النطاق `Denied` أو المتحكم مركزياً، كما في `RecruitmentOrgScopeFilter` | نمط واحد في كل الخدمات |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-52-iam-branch-scope-bola` | — |
| 0.2 | البناء والأساس | `dotnet build HRManagement.slnx`؛ `dotnet test src/Tests/HRManagement.ArchitectureTests`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Bola\|FullyQualifiedName~OrgScope\|FullyQualifiedName~Hcm025"` | أخضر (BOLA v1 أخضر منذ PLAN-34)، أو قائمة في `reports/plan52-baseline.md` |
| 0.3 | مصفوفة الوصول الفعلي | لكل شخصية مقيّدة (`br1-mgr`، `br2-mgr`، `a-homs-mgr`…، `b-branch-manager`): الخدمات والمتحكمات التي تصلها بعد PLAN-50/51 (البوابة + الفلتر + السمات)، مولّدة من `GatewayPathAuthorizationMiddleware.TryGetPolicy/TryGetFineGrainedPolicy` وسياسات الإجراءات | الجدول في `reports/plan52-baseline.md`. ما يصله المقيّد = «سطح فعلي»، وما لا يصله = «كامن» |
| 0.4 | ما أنجزه PLAN-34 | قائمة المتحكمات الاثنتي عشرة وما أُضيف إليها | لا يُعاد عملها هنا |

---

## In scope

### 1. آلية عامة: مسجّل مفاتيح النطاق ومرشح الإجراءات

**المشكلة:** النطاق اليوم فحوص يدوية في بعض الإجراءات، و`RecruitmentOrgScopeFilter` خاص بالتوظيف ([RecruitmentOrgScopeFilter.cs:33-60](../../src/Services/HRCore/HR.Core.Web/Services/RecruitmentOrgScopeFilter.cs)). و`OrgScopeRequirement` لا تستعمله أي سياسة، ومعالجه مسجّل في خدمتين فقط ([OrgScopeAuthorizationHandler.cs:11-22](../../src/Shared/HRManagement.ServiceDefaults/OrgScopeAuthorizationHandler.cs)، [HR.Core.Web/Program.cs:267](../../src/Services/HRCore/HR.Core.Web/Program.cs)، [HR.Movements.Web/Program.cs:68](../../src/Services/HRMovements/HR.Movements.Web/Program.cs))، مع أن IAM-002 تستشهد به.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `OrgScopeKeyRegistry` | `src/Shared/HRManagement.ServiceDefaults/OrgScope/OrgScopeKeyRegistry.cs` | لكل نوع كيان: تعبير يعطي وحدته التنظيمية (`OrgUnitId` مباشرة، أو عبر الموظف ← لقطة `EmployeeOrgUnitSnapshot`/`Employee.PrimaryOrgUnitId`، أو عبر كيان أب). ولكل اسم معامل أو خاصية (`id`، `rosterId`، `employeeId`، `transactionId`…) النوع الذي يحدده، لكل متحكم |
| `[OrgScoped(typeof(TEntity))]` / `[OrgScopeCentralOnly]` / `[OrgScopeNotApplicable("reason")]` | `src/Shared/HRManagement.ServiceDefaults/OrgScope/` | موقف النطاق المعلن لكل متحكم. `[OrgScopeId(typeof(T))]` على إجراء يغيّر نوع `id` |
| `OrgScopeActionFilter<TDbContext>` | ↑ | للمقيّد فقط (`MustFilter`): كل معرّف في معاملات الإجراء أو خصائص النموذج المرسل يُحلّ عبر المسجّل. خارج النطاق أو بلا مفتاح (D-52-1) ← 404. `Denied` أو `[OrgScopeCentralOnly]` ← 403. غير المقيّد لا يُستعلم عنه |
| `WhereInOrgScope<T>(registry, scope)` | `OrgScopeControllerExtensions.cs` | امتداد عام للقوائم والتصدير يستعمل التعبير نفسه |
| `{Service}OrgScopeKeys.Configure(registry)` | في HR.Core وHR.Contracts وAdmin.Logistics | تسجيل الكيانات في البنود 2–4. نمط ثابت يستدعيه `Program.cs` والاختبار المعماري |
| حذف `OrgScopeRequirement` ومعالجه | `OrgScopeAuthorizationHandler.cs` وسطرا التسجيل | لا كود ميت يُستشهد به دليلاً |

**القواعد:**
1. `RecruitmentOrgScopeFilter` يبقى للتوظيف، ويُضاف إليه نوعا `CitizenApplication` و`InternalApplication` (البند 2). لا يُعاد بناؤه على الآلية العامة في هذه الخطة.
2. الفحص قبل أي قراءة للكيان في الإجراء، وفي POST قبل أي تغيير.
3. القوائم لا يرشحها الفلتر: كل `Index`/تصدير في متحكم `[OrgScoped]` يستعمل `WhereInOrgScope`، ويثبته اختبار التكامل.

### 2. HR.Core: إغلاق ما يصله مدير الفرع

**المشكلة:** ([§2.3](../IAM_001_SCOPE_GAP_STUDY.md) في الدراسة)
- `DiwanCorrespondences.Index/Create` بلا نطاق ([OrgBranchControllers.cs:336-360](../../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs))، والكيان بلا وحدة.
- `HrStatisticReports` ([OrgBranchControllers.cs:14-37](../../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)) و`FieldInspectionVisits` (`:289-300`، والكيان بلا وحدة).
- `DailyAttendanceRecords.Create` لا يفحص `vm.EmployeeId` ([OrgBranchControllers.cs:126-145](../../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
- `Rosters.AddAssignment(employeeId)` ([RostersController.cs:104-106](../../src/Services/HRCore/HR.Core.Web/Controllers/RostersController.cs)) وبقية المناوبات.
- `SuccessionPlans` ([SuccessionPlansController.cs:22-36](../../src/Services/HRCore/HR.Core.Web/Controllers/SuccessionPlansController.cs)) و`KeyPositions`.
- `api/workflow-instances` يعيد كل النسخ مع `ContextJson` ([WorkflowInstancesController.cs:25-46, 134-142](../../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs))، والنسخة بلا وحدة.
- متابعات PLAN-25: `PromoteFromCitizen/PromoteFromInternal` ([RecruitmentApplicationsController.cs:627, 656](../../src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentApplicationsController.cs)) و`TalentMarketplace` ([TalentMarketplaceController.cs:16-163](../../src/Services/HRCore/HR.Core.Web/Controllers/TalentMarketplaceController.cs))، وقائمة الطلبات المنسدلة عند الإنشاء ([plan25-final-report.md:198-207](../../reports/plan25-final-report.md)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `[OrgScoped]` + مفاتيح | `OrgBranchControllers.cs` (الأربعة)، `RostersController`، `SuccessionPlansController`، `KeyPositionsController` | `DiwanCorrespondence`/`FieldInspectionVisit` ← `OrgUnitId` الجديد؛ `HrStatisticReport`/`ShiftRoster`/`KeyPosition` ← `OrgUnitId`؛ `DailyAttendanceRecord` ← الموظف؛ `SuccessionPlan` ← `KeyPosition`؛ `employeeId` في `AddAssignment`/`AddCandidate` ← الموظف. و`Edit` في الحضور يفحص الموظف الجديد أيضاً |
| `ShiftsController` | ↑ | `[OrgScopeNotApplicable]` للقراءة، وكتابة مركزية (D-52-3) |
| ترحيل | `HCM052_OrgScopeKeys` | `OrgUnitId` على `DiwanCorrespondences` و`FieldInspectionVisits` (D-52-2)، وملء `WorkflowInstances.ScopeOrgUnitId` القائم من وحدة الكيان (بالمنطق الذي يبني `WorkflowInboxEntityContext.ScopeOrgUnitId`). العمود نفسه أضافه PLAN-43 (§2، يُملأ للأنواع الخارجية من الطلب)، فلا يُضاف ثانية |
| `WorkflowInstance.ScopeOrgUnitId` (عمود PLAN-43) | `WorkflowStarter` وقت البدء | يُختم من وحدة الكيان للأنواع الداخلية أيضاً. و`List/Get/History` ترشّح به (فارغ = مركزي) |
| `RecruitmentOrgScopeFilter` | `NamedIds` | `citizenApplicationId` ← وحدة الشاغر المستهدف، و`internalApplicationId`/`vacancyId` ← وحدة الشاغر الداخلي. `TalentMarketplaceController` يحمل السمة بنوع `InternalVacancy`. والقائمة المنسدلة للطلبات ترشّح بالنطاق |

**القواعد:**
1. بعد هذا البند، كل متحكم في HR.Core.Web يحمل واحدة من السمات الثلاث، أو `RecruitmentOrgScope`، أو `[OrgScopeNotApplicable]` بسبب (بيانات مرجعية، خدمة ذاتية بالملكية، S2S).
2. لا تتغير السلوكيات المقيّدة أصلاً (الموظفون، السجلات، المستندات، التوظيف).

### 3. HR.Contracts: معالج التعيين، وما ذكرته HCM-002

**المشكلة:**
- `HireWizard.Index` بلا نطاق ([HireWizardController.cs:44](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/HireWizardController.cs))، والخطوات تعمل بمعرّف المعاملة دون فحص (`:112, 151-152, 161-162, 186-187, 210, 218-219, 245-246, 283-284, 309, 320-321`). الفحص فقط في `Continue(employeeId)` (`:93-99`). ومدير الفرع يصل المعالج لأن `HireView/HireExecute` تقبلان `hr-core` ([HrManagementAuthorizationExtensions.cs:153-162](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- كامنة (لا يصلها مقيّد اليوم) لكن رخيصة الإغلاق: `Contracts.Details` ([ContractsController.cs:51](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/ContractsController.cs))، و`LeaveBalances.Create` ([LeaveBalancesController.cs:89-91](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs))، وتصدير الإجازات ([ExportController.cs:24-41](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/ExportController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `[OrgScoped(typeof(ServiceTransaction))]` + `[OrgScopeId]` | `HireWizardController` | `transactionId` ← `ServiceTransaction.EmployeeId` ← لقطة الموظف في `ContractsDbContext`. `Index` يرشّح بـ `WhereInOrgScope` |
| مفاتيح | `ContractsOrgScopeKeys` | `Contract`، `LeaveBalance`، `LeaveRequest` ← الموظف. `Contracts.Details`، `LeaveBalances.Create(vm.EmployeeId)`، `ExportController.LeaveRequestsCsv` وقالب المهمة غير المتزامنة |

**القواعد:**
1. لا تغيير على سياسات الأدوار في المعالج (PLAN-50/51). هنا النطاق فقط.
2. المعاملة لموظف بلا لقطة وحدة ← مركزية (D-52-1).

### 4. Admin.Logistics: التوريد والاستلام والصيانة والمخزون

**المشكلة:** الأصول والعهد مقيدة ([AssetsController.cs:173-176](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)، [AdminExtendedControllers.cs:432-433](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AdminExtendedControllers.cs)). أما `SupplyOrders` (`:20-60`) و`MaintenanceRecords` (`:527`) و`ReceiptRecords` (`:739`) و`Vendors` (`:308`) و`InventoryItems` ([InventoryItemsController.cs](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/InventoryItemsController.cs)) فبلا نطاق، ويصلها `br1-mgr` و`br2-mgr`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| مفاتيح | `AdminLogisticsOrgScopeKeys` | `SupplyOrder` ← `OrgUnitId`؛ `ReceiptRecord` ← `SupplyOrder`؛ `MaintenanceRecord` ← `Asset` (المنطق نفسه في `ForbidIfAssetOutOfScopeAsync`) |
| سمات | `AdminExtendedControllers.cs`، `InventoryItemsController.cs` | `[OrgScoped]` للثلاثة، و`[OrgScopeNotApplicable]` + كتابة مركزية للمخزون والموردين (D-52-3) |

### 5. المنقول يفقد نطاقه، ولقطة HR.Health تتبع النقل

**المشكلة:** `TransferExecutedConsumer` في Auth.Directory يحدّث `DirectoryEntry` فقط ([TransferExecutedConsumer.cs:27-46](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/TransferExecutedConsumer.cs))، فيحتفظ المدير المنقول بمطالبة نطاقه. وHR.Health تبني لقطات الموظفين عند الإنشاء فقط، بلا مستهلك للنقل.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `TransferExecutedConsumer` (Auth.Directory) | الملف نفسه | إن كان للموظف `AuthUser` بـ `ScopeOrgUnitId`: يُفرَّغ (D-52-4)، ويُكتب في التدقيق، ويُستدعى `KeycloakUserProvisioningService` لحذف الخاصية `scope_org_unit_id` حين `DirectorySync:Enabled=true`. وحين تكون معطلة يُسجَّل تحذير بعدّاد `directory_scope_sync_skipped_total` |
| `TryClearScopeAttributeAsync` | `KeycloakUserProvisioningService.cs` | دالة جديدة بنمط `TrySyncUserInfoAsync` |
| `TransferExecutedConsumer` (HR.Health) | `HR.Health.Infrastructure/Consumers/` | يحدّث `EmployeeOrgUnitSnapshot.PrimaryOrgUnitId`، بنمط مستهلك HR.Contracts |

**القواعد:**
1. المستهلكان متساويا القوة (idempotent) على `TransferRequestId`.
2. الأثر الحي في Keycloak يحتاج المزامنة التي يفعّلها PLAN-62؛ هنا المنطق والاختبار بخادم Keycloak وهمي.

### 6. اختبار BOLA v2 بالانعكاس

**المشكلة:** `BolaArchitectureTests` يمسح النص بتعبير نمطي: يتخطى أي إجراء يسبقه `[HttpPost` خلال 400 حرف، ويتجاهل `id` و`transactionId`، ويقبل `GetEmployeeId` علامة حارس ([BolaArchitectureTests.cs:21-35, 52-61](../../src/Tests/HRManagement.UnitTests/Architecture/BolaArchitectureTests.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `OrgScopeCoverageTests` | `src/Tests/HRManagement.ArchitectureTests/` | لكل متحكم في تجميعات Web (من `WebAssemblyCatalog` في PLAN-51): (1) موقف نطاق معلن (إحدى السمات الثلاث أو `RecruitmentOrgScope`) أو صف في `tests/architecture/org-scope-allowlist.txt` بسبب. (2) في متحكم `[OrgScoped]`: كل معامل أو خاصية نموذج `Guid`/`Guid?` اسمها `id` أو تنتهي بـ `Id`، في **GET وPOST معاً**، لها مفتاح في مسجّل الخدمة أو `[NotOrgScopeKey("reason")]` |
| قائمة السقف | `tests/architecture/org-scope-allowlist.txt` | الخدمات الكامنة (Payroll، HR.Movements، HR.Health، HR.Training، IDCard، IT.Infra، Records.Archive، Compliance، Dev.Analytics، Auth.Directory) بسبب «كامن: لا دور مقيّد يصل الوحدة — D-IAM-3/IAM-H». HR.Core وHR.Contracts وAdmin.Logistics بلا صفوف |
| حذف v1 | `BolaArchitectureTests.cs` | يُحذف بعد أن يمر v2: المخالفات الاثنتا عشرة التي أغلقها PLAN-34 داخلة في v2 (صفوفها في الخدمات الكامنة بأسبابها، أو مغطاة) |

**القواعد:**
1. الاختبار يستدعي `{Service}OrgScopeKeys.Configure` فعلياً، فلا يُكتفى بوجود علامة نصية.
2. قائمة السقف لا تكبر، وكل صف يُراجع في PLAN-65.

---

## Out of scope

- النطاق كخاصية مستقلة لأي دور، ودور MSS، وتقييد موظفي الرواتب والعقود بالمحافظة ← الموجة 2: **IAM-H** (D-IAM-3، G-IAM-14). حتى ذلك تبقى الخدمات الكامنة في قائمة السقف.
- نطاق `EmployeePayProfiles.Create` ومعاملة تغيير الراتب ← **PLAN-53**. ونطاق بقية شاشات الرواتب والحركات (G-MOV-10) ← حين تصلها أدوار مقيّدة (الموجة 2).
- تفعيل مزامنة Keycloak وإنهاء جلسات المغادر ← **PLAN-62** (وجلسة المنقول: الموجة 2، IAM-G). وإصلاح اسم الخاصية `employee_id` نفّذه **PLAN-50**. وAC-06 الحي بتبدّل النطاق خلال دورة رمز واحدة يتحقق منه **PLAN-65**.
- إعادة تشغيل AC-01..06 حيّاً بعناصر الدليل الثمانية على SHA الحالي وتصحيح بطاقات IAM-002 ← **PLAN-65** (G-IAM-17). هذه الخطة تضيف سيناريوهاتها وتحدّث `IAM_002_ORG_SCOPE.md` لما تغيّر فقط.
- الرقم الوطني ومدير الفرع ← الموجة 2: **IAM-F** (G-IAM-10).

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، بالفئة `AcIam52BranchScopeTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-IAM-52")`). البيانات الأجنبية ينشئها `br2-mgr` (ALEP) أو `central-a` داخل الاختبار، ويجربها `br1-mgr` (RIF).

| ID | Positive | Negative |
|----|----------|----------|
| AC-IAM-52a | `br1-mgr` يرى في قوائم الديوان والإحصاءات والتفتيش والحضور والمناوبات والتعاقب سجلات RIF، وينشئ حضوراً ومناوبة لموظف RIF | سجل ALEP: التفاصيل ← 404، وPOST تعديل/حذف/إضافة بمعرّفه ← 404، ولا يظهر في القائمة ولا في التصدير. `AddAssignment` بموظف ALEP على مناوبة RIF ← 404 بلا صف. وفحص القاعدة قبل وبعد متطابق |
| AC-IAM-52b | `br1-mgr` يتابع معاملة تعيين لموظف RIF حتى خطوته المسموحة | معاملة لموظف ALEP: `Step2` و`IssueAppointmentDecision` و`Step3Execute` بمعرّفها ← 404، ولا تظهر في `HireWizard/Index`، ولا تتغير حالتها |
| AC-IAM-52c | `br1-mgr` يرى أوامر توريد RIF واستلامها وصيانة أصول RIF | أمر توريد ALEP ← 404. إنشاء صنف مخزون أو مورد ← 403 (D-52-3) |
| AC-IAM-52d | `central-a` يرى كل نسخ المسارات في `api/workflow-instances` | `br1-mgr`: القائمة بلا نسخ ALEP ولا نسخ مركزية، و`Get`/`History` لنسخة ALEP ← 404 |
| AC-IAM-52e | — | `br1-mgr` ← `PromoteFromInternal` لطلب داخلي على شاغر ALEP ← 404. وتفاصيل شاغر داخلي ALEP في `TalentMarketplace` ← 404 |

وخارج القبول الحي:
- اختبار تكامل بالمضيف داخل العملية لكل من HR.Core وHR.Contracts وAdmin.Logistics (فرعان ومقيّد): لكل متحكم `[OrgScoped]` قائمة وتفاصيل وPOST بمعرّف أجنبي، مولّدة من مسجّل المفاتيح.
- اختبار تكامل على PostgreSQL (Testcontainers) `Iam052OrgScopeMigrationPostgreSqlTests`: ترحيل `HCM052` يملأ `ScopeOrgUnitId` للنسخ القائمة ووحدة الديوان من الموظف المرتبط، ويترك الباقي فارغاً.
- اختبار وحدة لمستهلك النقل في Auth.Directory بخادم Keycloak وهمي (`WireMock` أو `HttpMessageHandler` وهمي): المزامنة مفعّلة ← طلب حذف الخاصية؛ معطلة ← تحذير وعدّاد، والنطاق المحلي فارغ في الحالتين.

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.ArchitectureTests --filter "FullyQualifiedName~OrgScopeCoverage"
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Iam052|FullyQualifiedName~OrgScope|FullyQualifiedName~Hcm025"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Iam052|FullyQualifiedName~Hcm025"
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
# قبول على الملف الشبيه بالإنتاج (أوامر PLAN-37)، ثم:
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --no-deps --force-recreate hr-core-web hr-contracts-web admin-logistics-web auth-directory-web hr-health-web
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcIam52|FullyQualifiedName~Ac02|FullyQualifiedName~Ac03|FullyQualifiedName~Ac32|FullyQualifiedName~AcHcm25"
pwsh scripts/iam052-validate.ps1
```

## Definition of done

- كل متحكم في HR.Core.Web وHR.Contracts.Web وAdmin.Logistics.Web يعلن موقف نطاقه، و`OrgScopeCoverageTests` أخضر بقائمة سقف للخدمات الكامنة فقط.
- لا قائمة ولا تفاصيل ولا تصدير ولا POST بمعرّف يتجاوز نطاق المقيّد في السطح الفعلي.
- `OrgScopeRequirement` محذوف، و`BolaArchitectureTests` v1 محذوف بعد أن غطاه v2.
- نقل المستخدم المقيّد يفرّغ نطاقه، ولقطة HR.Health تتبع النقل.
- AC-IAM-52a..e = Pass في `acceptance-evidence/AC-IAM-52/` بعناصر الدليل الثمانية، وانحدار AC-02 وAC-03 وAC-32 وAC-HCM-25 أخضر.
- سطر «الحالة» لـ G-IAM-04 (وجزء النقل من G-IAM-11) محدّث في الدراسة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-52-iam-branch-scope-bola.md` |
| ServiceDefaults | `OrgScope/OrgScopeKeyRegistry.cs`، `OrgScope/OrgScopeAttributes.cs`، `OrgScope/OrgScopeActionFilter.cs`، تعديل `OrgScopeControllerExtensions.cs`، حذف `OrgScopeAuthorizationHandler.cs` |
| HR.Core | `HrCoreOrgScopeKeys.cs`، `OrgBranchControllers.cs`، `RostersController.cs`، `SuccessionPlansController.cs`، `KeyPositionsController.cs`، `ShiftsController.cs`، `WorkflowInstancesController.cs`، `WorkflowStarter`، `RecruitmentOrgScopeFilter.cs`، `TalentMarketplaceController.cs`، الكيانات `DiwanCorrespondence`/`FieldInspectionVisit`/`WorkflowInstance` |
| HR.Contracts | `ContractsOrgScopeKeys.cs`، `HireWizardController.cs`، `ContractsController.cs`، `LeaveBalancesController.cs`، `ExportController.cs` |
| Admin.Logistics | `AdminLogisticsOrgScopeKeys.cs`، `AdminExtendedControllers.cs`، `InventoryItemsController.cs` |
| Identity / Health | `Auth.Directory.Infrastructure/Consumers/TransferExecutedConsumer.cs`، `KeycloakUserProvisioningService.cs`، `HR.Health.Infrastructure/Consumers/TransferExecutedConsumer.cs` |
| Migration | `HCM052_OrgScopeKeys` (HR.Core) |
| Tests | `OrgScopeCoverageTests.cs` و`tests/architecture/org-scope-allowlist.txt`؛ حذف `BolaArchitectureTests.cs`؛ `Iam052ScopeFilterTests.cs`، `Iam052DirectoryTransferConsumerTests.cs` (وحدة)؛ `Iam052HrCoreScopeHttpTests.cs`، `Iam052ContractsScopeHttpTests.cs`، `Iam052LogisticsScopeHttpTests.cs`، `Iam052OrgScopeMigrationPostgreSqlTests.cs` (تكامل) |
| Acceptance | `AcIam52BranchScopeTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)؛ تحديث `scenarios` للشخصيتين `br1-mgr` و`br2-mgr` |
| Scripts | `scripts/iam052-validate.ps1` |
| Docs | `docs/IAM_002_ORG_SCOPE.md` (الآلية والمسجّل، وحذف ذكر `OrgScopeRequirement`)، `docs/HIRE_RBAC_MATRIX.md` (نطاق المعالج)، `docs/IAM_005_IDENTITY_LIFECYCLE.md` (النقل fail-closed)، سطر «الحالة» في `docs/IAM_001_SCOPE_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تعبيرات المسجّل لا تُترجم إلى SQL في PostgreSQL | الاختبار المعماري يستدعي كل تعبير على `DbContext` حقيقي في اختبار التكامل (SQLite وPostgreSQL)، لا في الذاكرة |
| سجلات بلا وحدة تختفي فجأة من شاشات الفروع (D-52-1) | `scripts/iam052-validate.ps1` يعدّها قبل الترحيل وبعده لكل كيان، ويُرفق العدد بالدليل |
| المدير المنقول يتوقف حتى يُعاد تعيينه (D-52-4) | مقصود (fail-closed). يُذكر في `IAM_005_IDENTITY_LIFECYCLE.md` مع خطوة إعادة التعيين |
| تعارض مع PLAN-43 في `WorkflowInstancesController` ومع PLAN-34 في قوائم العقود والرواتب | المرحلة 0.4 تسجل ما أنجزه PLAN-34. ويُعاد قراءة المتحكم بعد PLAN-43 |
| حجم الخطة | المرشح العام يجعل كل متحكم بضعة أسطر. والخدمات الكامنة لا تُعدَّل هنا، بل تُسجَّل في قائمة السقف |

## Effort estimate

9–10 أيام عمل: المرحلة 0 والمصفوفة (0.5)، الآلية العامة (2)، HR.Core مع الترحيل ونسخ المسارات والتوظيف (2.5)، HR.Contracts (1)، Admin.Logistics (0.5)، المستهلكان (0.5)، BOLA v2 (1)، القبول والتكامل والوثائق (1.5).
