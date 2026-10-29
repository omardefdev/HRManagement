# PLAN-51 — سياسة صريحة لكل إجراء، وتضييق المسارات والسياسات الواسعة (IAM-E)

**Scope:** IAM — [دراسة الفجوات](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-05، G-IAM-06، G-IAM-08، G-IAM-09؛ والملاحظة الجديدة 1 في تقرير التحقق المستقل للدراسة (`/ui/ess` يتجاوز قواعد البوابة الدقيقة لمن يحمل `hr-employee` مع دور في HR.Core)  
**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-50 (سمة `ModulePolicyOverride`، وجدول استثناءات البوابة، وسياسة `Gateway.ApprovalReach`)  
**Related cards:** IAM-004 (S2S)، SEC-003، EVD-003، ONB-001، PLAN-25 (سقف HR.Core)، FUN-005 G-IDA-14، HCM-006/HCM-002 (POST بلا سياسة)، D-IAM-4، D-IAM-7

## Goal

- لا يصل `hr-employee` عبر `/ui/ess` أو عبر أي مسار فيه `/Ess` إلا إلى متحكمات الخدمة الذاتية المسماة، **ولو حمل معه دور قراءة أو اعتماد في HR.Core**.
- لا تُفتح نقطة `/api/internal/*` لمستخدم تفاعلي، ولا يبحث `hr-employee` أو مصحح الامتحان في دليل موظفي الجهة.
- سجل تدقيق البوابة لمن يملكه فقط، ولكل جهة صفوفها.
- كل إجراء كتابة في كل الخدمات محمي في الخدمة نفسها بسياسة كتابة على الأقل، ولو تجاوز الطلب البوابة. وفي HR.Core وHR.Contracts بسياسة صريحة على كل إجراء.
- اختبار معماري واحد لكل تجميعات Web، وقوائم سقف لكل خدمة لا تكبر. وقائمتا HR.Core وHR.Contracts فارغتان.
- حساب الخدمة بنطاق `hr-core.workflow` يبدأ مساراً ويولّد رمزاً ويستعلم السياسة، ولا يكتب في سجل الموظفين.
- إنشاء الجهات لـ `platform-admin` وحده، وبلا قراءة ملفات من القرص عبر HTTP.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-51-1 — مصير مسار `/ui/ess` | يبقى، لأن الدليل يذكره ([USER_GUIDE_AR.md:22](../../docs/USER_GUIDE_AR.md))، لكنه يقبل فقط `EssPortal` و`EssTalent` والملفات الثابتة (GET). أي مسار آخر تحته ← 404 من البوابة | القائمة في `Gateway:Ess:AllowedControllers` لكل مقطع: `ess` و`hr-core` = `EssPortal`، `EssTalent`؛ `payroll` = `Ess`. إضافة `hr-training`/`EssLearning` (G-IAM-15) تكون بسطر إعداد في الموجة 2 |
| D-IAM-7 — من يقرأ سجل تدقيق البوابة | `platform-admin` لكل الجهات؛ `auditor` و`compliance` لجهتهم فقط (`Gateway:Audit:Roles`) | `hr-admin` لم يعد يرى السجل |
| D-IAM-4 — صلاحيات `hr-admin` | يُستبعد `hr-admin` من `HrHealth.Clinical` و`Payroll.Post` و`Payroll.Reverse` عبر `Authorization:AdminExcludedPolicies` في Production وStaging والملف الشبيه بالإنتاج. Development وDemo بلا استبعاد. تفكيك الدور و«كسر الزجاج» بـ MFA في الموجة 2 (IAM-G) | لا تغيير في بيئة التطوير. في الإنتاج يحتاج الترحيل والعكس والسريري أدوارها المتخصصة |
| D-51-2 — أدوار الكتابة والاعتماد لكل وحدة | سياستان لكل وحدة: `{Module}.Write` و`{Module}.Approve`. الأدوار من `Authorization:Policies:{name}:Roles`. الافتراضي = السلوك الحالي (دور الوحدة)، عدا HR.Core (`HrCore.Write` = Admin، `hr-core`) وHR.Contracts (`HrContracts.Write` = Admin، `hr-contracts`) | لا يتغير سلوك الوحدات ذات الدور الواحد. فصل القارئ عن الكاتب فيها يصير إعداداً حين تُضاف أدوارها (G-IDA-14، الموجة 2) |
| D-51-3 — `HrCore.Lookup` | لكل مستخدم تفاعلي يحمل أي دور موظفين (`Authorization:NonStaffRoles` من PLAN-50 تستثني `hr-employee` و`hr-exam-marker` و`applicant`) | لا يبحث الموظف ولا المصحح في الدليل. منتقيات الوحدات الأخرى تبقى تعمل لموظفيها |
| D-51-4 — نطاقات S2S الضيقة (`hr-core.workflow.start`، `hr-core.codes.generate`) | لا نطاقات جديدة في هذه الخطة. بدلاً منها يُقبل `hr-core.workflow` على ثلاث نقاط فقط (بدء المسار، توليد الرمز، استعلام السياسة)، وتُزال حسابات الخدمة من `HrCore.Write` و`HrCore.BranchOperation`. تسمية النطاقات في realm وcompose تتبع مصفوفة PLAN-37 | لا تغيير في realm ولا في compose |
| D-51-5 — من ينشر الأدلة (`Service.EvidenceIngest`) | قائمة عملاء من `Evidence:IngestClients`، وافتراضياً فارغة (لا أحد). لا مستدعي في المستودع اليوم | من يحتاج النشر يُضاف بالإعداد |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-51-iam-explicit-policy-every-action` | — |
| 0.2 | البناء | `dotnet build HRManagement.slnx` | 0 أخطاء |
| 0.3 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Architecture\|FullyQualifiedName~GatewayPath\|FullyQualifiedName~Hcm028Authorization\|FullyQualifiedName~Hcm031Marker\|FullyQualifiedName~Iam050"` | أخضر، أو قائمة في `reports/plan51-baseline.md` |
| 0.4 | جرد الكتابة | مسح انعكاسي لكل تجميعات Web: كل POST/PUT/PATCH/DELETE بلا `[Authorize(Policy)]` على الإجراء أو الصنف ولا `[AllowAnonymous]`، لكل خدمة | يطابق §2.2 في الدراسة (336). الجدول في `reports/plan51-baseline.md`، وهو بذرة قوائم السقف |
| 0.5 | جرد S2S | لكل عميل HTTP بين الخدمات (`CodeGenerationClient`، `PolicyEnforceClient`، `WorkflowTriggerService`، `HrCoreLookupClient`، عملاء الأنظمة والحجز والرفع): النقطة في HR.Core وسياستها والنطاق المطلوب | قائمة نقاط HR.Core التي تحتاج `hr-core.workflow` اليوم. المتوقع ثلاث؛ أي زيادة تُضاف إلى البند 7 |
| 0.6 | جرد الواجهة | كل استدعاء من واجهات يصلها `hr-employee` أو `hr-exam-marker` لـ `/api/lookup` أو `/api/internal` أو ملفات HR.Core الثابتة | قائمة؛ المتوقع: الملفات الثابتة فقط |
| 0.7 | جرد الاستثناء بالاسم | كل متحكم في كل الخدمات يطابق قائمة الأسماء في [HrModuleAuthorizationFilter.cs:27-46](../../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs) | الجدول؛ كل صف سيحمل `[ModulePolicyOverride]` أو سياسة `Service.*` |

---

## In scope

### 1. قصر الخدمة الذاتية على متحكماتها (G-IAM-05، ملاحظة التحقق 1)

**المشكلة:**
- المسار `/ui/ess/{**catch-all}` يذهب إلى HR.Core كله ([Gateway appsettings.json:338-342](../../src/Gateway/HRManagement.Gateway/appsettings.json))، والبوابة تنزع البادئة وتوقّع الإثبات ([GatewayAccessTokenTransform.cs:28-41](../../src/Shared/HRManagement.ServiceDefaults/GatewayAccessTokenTransform.cs)).
- البوابة تختار قاعدة الخدمة الذاتية قبل القواعد الدقيقة ([GatewayPathAuthorizationMiddleware.cs:71-75](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). والقاعدة تطابق أي مسار **يحوي** `/Ess` تحت `hr-core` و`payroll` و`ess` (`:240-258`)، فتعطيه `Module.Ess` وحدها لكل طريقة.
- النتيجة: من يحمل `hr-employee` مع `hr-core-supervisory` أو `hr-core-executive` أو `hr-management-approver` أو `branch-manager` يرسل عبر `/ui/ess/...` POST إلى الإجراءات الـ 42 بلا سياسة في HR.Core، ويصل المعتمد إلى إجراءات `HrCore.Approve` و`Delegations`. لا تفحص الخدمة إلا فلتر الوحدة وسمات الإجراء. والثغرة كامنة لأن لا شخصية تجمع الدورين، لكن موظفي الإنتاج يحملون `hr-employee` عادة.
- ومن مسار `/ui/ess` أيضاً: `GET /ui/ess/api/internal/employees/{id}/lifecycle-flags` يعيد `legalHold` وسببه لأي موظف ([EmployeeLifecycleInternalController.cs:15-30](../../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleInternalController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `GatewayEssOptions` | `src/Shared/HRManagement.ServiceDefaults/GatewayEssOptions.cs` (`Gateway:Ess`) | `AllowedControllers` لكل مقطع (D-51-1)، و`StaticPrefixes` (`css/`، `js/`، `lib/`، `fonts/`، `images/`، `_content/`، `favicon.ico`) |
| `TryGetEssPolicy` | `GatewayPathAuthorizationMiddleware.cs:240-258` | يطابق **المقطع الأول** بعد `/ui/{segment}/` أو `/api/{segment}/` تماماً (بلا `Contains`). مطابقة ← `Module.Ess`. لا مطابقة ← تُطبَّق القواعد الدقيقة كالمعتاد |
| رفض بقية `/ui/ess` | ↑ | على المقطع `ess`: مسار ليس متحكماً مسموحاً ولا ملفاً ثابتاً (GET) ← 404 قبل التمرير، ويُسجَّل في تدقيق البوابة بقرار `Denied:EssNotExposed` |
| اختبار | `GatewayEssRoutingTests` في `src/Tests/HRManagement.UnitTests/` | جدول حالات: `/ui/ess/EssPortal` ← Ess؛ `/ui/ess/Shifts/Create` ← 404؛ `/ui/hr-core/Shifts/Create/Ess` ← `HrCore.Write`؛ `/ui/hr-core/EssPortal/Leave` ← Ess؛ `/ui/payroll/Ess/Payslips` ← Ess؛ `/ui/payroll/PayrollRuns/Index/Essam` ← `Module.Payroll`؛ `/ui/ess/api/internal/employees/{id}/lifecycle-flags` ← 404 |

**القواعد:**
1. لا مطابقة جزئية لـ `/Ess` في أي مكان من البوابة.
2. القرار للمقطع `ess` نهائي في البوابة: ما ليس في القائمة لا يُمرَّر.
3. متحكمات الخدمة الذاتية تبقى بسياستها `Module.Ess` في الخدمة ([EssPortalController.cs:15-17](../../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs))، والملكية بـ `employee_id` كما هي.

### 2. فصل نقاط الخدمات عن المستخدم التفاعلي، وتضييق `HrCore.Lookup` (G-IAM-05)

**المشكلة:**
- `Service.HrCoreLookup` تقبل «Interactive UI users» ([ServiceAuthorizationExtensions.cs:74-82](../../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs))، وهي سياسة `ServiceRegimeInternalController` و`EmployeeLifecycleInternalController` ([ServiceRegimeInternalController.cs:9-10](../../src/Services/HRCore/HR.Core.Web/Controllers/ServiceRegimeInternalController.cs)، [EmployeeLifecycleInternalController.cs:11-12](../../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleInternalController.cs)). والفلتر يتخطى كل سياسة `Service.*` ([HrModuleAuthorizationFilter.cs:22-25](../../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)).
- `HrCore.Lookup` = أي مستخدم مسجّل ([HrManagementAuthorizationExtensions.cs:120-123](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، على `LookupApiController` و`EmployeesApiController` ([LookupApiController.cs:17-19, 37-57](../../src/Services/HRCore/HR.Core.Web/Controllers/LookupApiController.cs)). والبوابة تستعمل السياسة نفسها للملفات الثابتة ([GatewayPathAuthorizationMiddleware.cs:170-177](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `Service.HrCoreLookup` | `ServiceAuthorizationExtensions.cs` | نقل PLAN-42 المتحكمين الداخليين إلى `ServicePolicies.HrCoreInternalS2S`، فلم يبقَ لها مستعمل. تُحذف السياسة، وإن وجد الجرد 0.5 مستعملاً يُحذف فرع المستخدم التفاعلي منها فقط |
| `HrCore.Lookup` | `HrManagementAuthorizationExtensions.cs` | مستخدم تفاعلي بدور موظفين (D-51-3)، أو حساب خدمة بـ `hr-core.lookup` |
| `Gateway.Authenticated` | ↑ + `TryGetFineGrainedPolicy` | للملفات الثابتة في `hr-core` بدل `HrCore.Lookup`، فتبقى صفحات الخدمة الذاتية والمصحح منسّقة |

**القواعد:**
1. لا نقطة تحت `/api/internal/` تقبل مستخدماً تفاعلياً. اختبار في البند 6 يفرض ذلك على كل التجميعات (المسار يبدأ بـ `api/internal` ← سياسة `Service.*` فقط).
2. فشل استدعاء `/api/lookup` من صفحة يصلها الموظف (إن وجده الجرد 0.6) يُصلح بنقطة ملكية في متحكم الخدمة الذاتية، لا بتوسيع السياسة.

### 3. سجل تدقيق البوابة لمن يملكه ولكل جهة صفوفها (G-IAM-06)

**المشكلة:** الفلتر العام للبوابة `Gateway.Dashboard` = أي مستخدم مسجّل ([Gateway Program.cs:104-106](../../src/Gateway/HRManagement.Gateway/Program.cs)). و`Home/Operations` و`Home/Audit` و`/ops/audit-search` بلا سياسة أخرى ([HomeController.cs:193-276](../../src/Gateway/HRManagement.Gateway/Controllers/HomeController.cs)). والاستعلام بلا مرشح جهة ([GatewayAuditQueryService.cs:35](../../src/Gateway/HRManagement.Gateway/Services/GatewayAuditQueryService.cs)) ويعيد `UserName` و`Path` (`:79-80`)، مع أن الكيان يحمل `TenantId` ([GatewayAuditEntry.cs:22](../../src/Gateway/HRManagement.Gateway/Models/GatewayAuditEntry.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| سياسة `Gateway.Audit` | `HrManagementAuthorizationExtensions.cs` | الأدوار من `Gateway:Audit:Roles` (D-IAM-7) |
| `HomeController` | `Operations`، `Audit`، `AuditSearch` | `[Authorize(Policy = Gateway.Audit)]` |
| `GatewayAuditQueryService` | `Services/GatewayAuditQueryService.cs` | مرشح إلزامي `TenantId == tenant_id` المستدعي، إلا لـ `platform-admin`. بلا `tenant_id` صالح ← لا صفوف |
| واجهة | قائمة البوابة | رابط «التدقيق» لمن يملك السياسة فقط |

**القواعد:**
1. المرشح في الخدمة لا في الواجهة، ويُختبر على SQLite وعلى PostgreSQL.
2. الصفوف بلا `TenantId` (طلبات بلا رمز) لـ `platform-admin` فقط.

### 4. السمة بدل قائمة الأسماء، وسياسة كتابة افتراضية في الخدمة (G-IAM-08)

**المشكلة:**
- الاستثناء من فلتر الوحدة بأسماء متحكمات يسري على كل الخدمات ([HrModuleAuthorizationFilter.cs:27-46](../../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)).
- 336 إجراء كتابة بلا سياسة صريحة، يحرسها فلتر الوحدة وقاعدة البوابة فقط (§2.2). وفي HR.Core يقبل الفلتر الإجراءات الـ 42 المجمّدة لكل أدوار `Module.HrCore`، ومنها أدوار القراءة ([HrManagementAuthorizationExtensions.cs:13-22](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). وفي HR.Contracts تقبل الخدمة `hr-management-approver` في الوحدة، والكتابة محمية في البوابة وحدها ([GatewayPathAuthorizationMiddleware.cs:222-226](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)، [HrManagementAuthorizationExtensions.cs:28-31](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `HrModuleAuthorizationFilter` | الملف نفسه | (1) تُحذف قائمة الأسماء. كل متحكم كان فيها يحمل `[ModulePolicyOverride(policy)]` (من PLAN-50) أو سياسة `Service.*`، حسب الجرد 0.7. (2) طلب POST/PUT/PATCH/DELETE على إجراء بلا سياسة صريحة على الإجراء أو الصنف ← يُقيَّم بسياسة الكتابة للوحدة بعد سياسة الوحدة |
| `AddHrModuleAuthorizeFilter(modulePolicy, writePolicy)` | `HrManagementAuthorizationExtensions.cs:203-208` | توقيع جديد. كل `Program.cs` يمرّر `{Module}.Write` |
| `AddModulePolicies` | ↑ | يولّد `{Module}.Write` و`{Module}.Approve` لكل وحدة من `Authorization:Policies` (D-51-2) بمساعد واحد `RolesPolicy(name, defaults)` يطبّق أيضاً `AdminExcludedPolicies` (D-IAM-4) |
| HR.Core | الإجراءات الـ 42 في [hrcore-post-policy-allowlist.txt](../../tests/architecture/hrcore-post-policy-allowlist.txt) | `HrCore.Write` صريحاً لكل منها (والحذف أيضاً: لا تغيير على `hr-core`)، و`Signatures.Verify` ← `HrCore.ReadOnly`، و`ExportJobs.Cancel` ← `HrCore.ReadOnly` مع فحص المالك الحالي. يُفرَّغ الملف ثم يُحذف |
| HR.Contracts | المتحكمات الـ 20 عدا `HireWizard` | `HrContracts.Write` على الأصناف للكتابة، و`HrContracts.Approve` الحالية على إجراءات الاعتماد، و`Module.HrContracts` صريحاً للقراءة. `HireWizard` كما هو |
| الإجراءات الحساسة في الخدمات الأخرى | حسب الجرد 0.4 | `{Module}.Approve` صريحاً على: `IssuedCards.Revoke/UrgentRevoke`، `AccessPermissions.*`، `OfficialSealRecords.*` (IDCard)؛ `ArchiveItems.ReleaseLegalHold/RecordDisposal/UpdateRetention` (Archive)؛ `SystemPermissionRecords.Create/Revoke`، `BackupJobs.Run/Delete` (IT)؛ `Roles.Edit/Delete`، `AuthUsers.Edit` (Directory)؛ `TrainingPlans.Approve`، `TrainingNominations.Approve/Reject`، `MissionFiles.Approve` (Training)؛ `DistributionPlans.Approve` (Movements)؛ `DisciplinaryActions.DecideAppeal`، `Complaints.Resolve` (Compliance)؛ `ReadModels.Rebuild`، `EtlJobs.Run` (Analytics)؛ `Assets.MarkLost/CancelAsset` (Logistics) |

**القواعد:**
1. سياسة الكتابة الافتراضية في الفلتر حماية دنيا لا تغني عن السمة: الاختبار في البند 6 يطلب السمة أو صفاً في قائمة السقف.
2. لا يتغير سلوك أي دور في الوحدات ذات الدور الواحد (الافتراضي = دور الوحدة). المتغير الفعلي: أدوار القراءة في HR.Core و`hr-management-approver` في HR.Contracts لا تكتب في الخدمة ولو تجاوزت البوابة.
3. إجراءات الاعتماد التي أدخلها PLAN-50 تبقى بسياساتها، و`ModulePolicyOverride` لا يُستعمل لإجراء كتابة.
4. فصل «المنشئ ≠ المعتمد» في اعتمادات التدريب والتوزيع والتأديب ليس هنا (G-IAM-07، الموجة 2: IAM-F). هنا السياسة فقط.

### 5. تضييق `hr-core.workflow` و`Onboarding` و`EvidenceIngest` (G-IAM-09)

**المشكلة:**
- `hr-core.workflow` يعطي `HrCore.Write` كاملة و`Module.HrCore` و`HrCore.BranchOperation` ([HrManagementAuthorizationExtensions.cs:20-22, 73-77, 96-97](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). وحاجة حامليه بدء مسار وتوليد رمز واستعلام سياسة فقط ([WorkflowInstancesController.cs:48-50](../../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)، [CodeGenerationController.cs:20-21](../../src/Services/HRCore/HR.Core.Web/Controllers/CodeGenerationController.cs)، [PoliciesController.cs:27-28](../../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)).
- `OnboardingController` بسياسة `HrCore.Approve`، فيصله كل `hr-core` في أي جهة ([OnboardingController.cs:8-34](../../src/Services/HRCore/HR.Core.Web/Controllers/OnboardingController.cs))، والبوابة كذلك ([GatewayPathAuthorizationMiddleware.cs:182-183](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). و`apply-file` يقرأ مساراً حراً ([AgencyManifestLoader.cs:12-18](../../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyManifestLoader.cs)).
- `Service.EvidenceIngest` بلا قائمة عملاء ([ServiceAuthorizationExtensions.cs:113-115](../../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `HrCore.Write`، `HrCore.BranchOperation` | `HrManagementAuthorizationExtensions.cs` | بلا فرع حساب الخدمة. (`WorkflowStepAction` بلا حسابات خدمة منذ PLAN-43) |
| النقاط الثلاث لـ `hr-core.workflow` (تُستهلك ولا يُعاد بناؤها) | — | البدء ← `WorkflowExternalStart` على `POST api/workflow-instances` (PLAN-43)، وتوليد الرمز ← `ServicePolicies.CodeGenerate` على `api/internal/codes/generate` (PLAN-42)، والإنفاذ ← `ServicePolicies.PolicyEnforceS2S` على `api/internal/policies/enforce` (PLAN-42). هنا يُتحقق فقط أنها وحدها تقبل `hr-core.workflow` بعد حذف الفرع. وأي نقطة أخرى وجدها الجرد 0.5 تأخذ سياسة خدمة صريحة بالنمط نفسه، ومعها قاعدة بوابتها |
| مصفوفة التوصيل | `deploy/wiring/service-wiring.json` (PLAN-37) | تحديث `serverPolicy` لكل `calls[]` كان يمر بـ `HrCore.Write`/`HrCore.BranchOperation`، فيبقى `ProductionWiringMatrixTests` أخضر |
| `Platform.AgencyOnboarding` | ↑ + `OnboardingController` + `TryGetFineGrainedPolicy` | `platform-admin` فقط، في الخدمة والبوابة |
| حذف `apply-file` | `OnboardingController.cs:24-34` | يبقى `AgencyManifestLoader` للسكربتات والبذور فقط |
| `Service.EvidenceIngest` | `ServiceAuthorizationExtensions.cs` | `IsAllowedClient` من `Evidence:IngestClients` (D-51-5) |

**القواعد:**
1. نقاط القراءة لحساب الخدمة (`Module.HrCore`، `HrCore.ReadOnly` بنطاق lookup) لا تتغير هنا.
2. `PLAN-42` يملك محتوى طلب `policies/enforce` (الفاعل والجهة والوضع من الخادم). هنا تتغير السياسة على النقطة فقط.

### 6. مشروع الاختبارات المعمارية لكل التجميعات (G-IAM-08)

**المشكلة:** السقف واختباره لـ HR.Core وحده ([HrCorePostActionsHaveExplicitPolicyTests.cs:36-58](../../src/Tests/HRManagement.UnitTests/Architecture/HrCorePostActionsHaveExplicitPolicyTests.cs)). و`MassAssignmentArchitectureTests` يمسح التجميعات المحمّلة ويتخطى الباقي بصمت ([MassAssignmentArchitectureTests.cs:40-59](../../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| مشروع | `src/Tests/HRManagement.ArchitectureTests/HRManagement.ArchitectureTests.csproj` (+ `HRManagement.slnx`، و`ci.yml` بجانب خطوة الوحدة) | يشير إلى تجميعات Web الأربع عشرة والبوابة، كما يفعل مشروع التكامل مع ثمانٍ منها |
| `WebAssemblyCatalog` | ↑ | القائمة الصريحة للتجميعات الخمس عشرة. `AllWebAssembliesLoadTests` يفشل إن غابت واحدة |
| `WriteActionsHaveExplicitPolicyTests` | ↑ | لكل تجميعة: كل POST/PUT/PATCH/DELETE يحمل سياسة صريحة (إجراء أو صنف) أو `[AllowAnonymous]`، أو صف في `tests/architecture/write-policy-allowlist/{service}.txt` بسبب. قائمة لا تكبر: إجراء محمي ما زال في القائمة ← فشل. HR.Core وHR.Contracts فارغتان. Payroll مجمّدة كما هي (يفرّغها PLAN-53) |
| `SensitiveActionsHaveApprovalPolicyTests` | ↑ | الإجراءات التي تطابق أسماؤها `Approve\|Reject\|Revoke\|UrgentRevoke\|Release*\|Decide*\|Post\|Reverse\|Finalize\|Publish*\|Grant*\|Rebuild\|Run` تحمل سياسة صريحة ليست سياسة الوحدة ولا `{Module}.Write`، أو صفاً في `sensitive-actions-allowlist.txt` بسبب |
| `InternalRoutesAreServiceOnlyTests` | ↑ | نقل `S2s042InternalRoutesAreServiceOnlyTests` (PLAN-42) إلى المشروع بقاعدته: كل مسار يبدأ بـ `api/internal` سياسته `ServicePolicies.*`. لا اختبار ثانٍ |
| `ModuleOverridesAreDeclaredTests` | ↑ | كل `[ModulePolicyOverride]` يذكر سياسة معرّفة، ولا يقع على إجراء كتابة إلا إن كانت السياسة سياسة اعتماد |
| نقل | `HrCorePostActionsHaveExplicitPolicyTests` وملفه | يُحذفان: الاختبار العام يغطيهما بقائمة HR.Core الفارغة، مع شرط PLAN-25 «لا متحكم توظيف في القائمة» |

**القواعد:**
1. التجميعة التي لا تُحمَّل تُفشل الاختبار، لا تُتخطى.
2. كل صف في قوائم السقف يحمل سبباً، ويُراجع في PLAN-65.

---

## Out of scope

- اعتماد المعتمد عبر البوابة واستثناءات `HrCore.Approve` والتفويض ← **PLAN-50** (تستهلكها هذه الخطة).
- سياسات الرواتب الست عشرة وتفريغ قائمة Payroll ← **PLAN-53**.
- عناوين الخدمات ونطاقاتها وأسرارها في compose وk8s وrealm (ومنها أسماء نطاقات ضيقة جديدة) ← **PLAN-37**. السر لكل خدمة والإثبات غير القابل للتزوير ← **PLAN-42** (G-IAM-13). وجمهور الرمز لكل وحدة (بدل قبول `hr-gateway-client` في كل الوحدات) ← الموجة 2: **IAM-G**.
- نطاق الفرع وBOLA ← **PLAN-52**.
- فصل «المنشئ ≠ المعتمد» في التدريب والتوزيع والتأديب، ومنح الأدوار الحساسة بمعتمد ثانٍ، والرقم الوطني وView Models ← الموجة 2: **IAM-F** (G-IAM-07، G-IAM-10). أما علم `forceSameActorForLab` فحذفه **PLAN-47** (G-POL-06).
- تفكيك `hr-admin` إلى دورين و«كسر الزجاج» بـ MFA، والأدوار الجديدة لفصل القارئ عن الكاتب في الوحدات ← الموجة 2: **IAM-G** وFUN-005 (G-IDA-14).
- الخدمة الذاتية للتعلّم (`/ui/hr-training/EssLearning`) وقسيمة الراتب ← الموجة 2: **IAM-H** (G-IAM-15)؛ هنا الإعداد فقط.

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، بالفئة `AcIam51ExplicitPolicyTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-IAM-51")`). شخصيتان جديدتان في الجهة A بموظف: `a-ess-supervisory` (`hr-employee` + `hr-core-supervisory`) و`a-ess-approver` (`hr-employee` + `hr-management-approver`). وسيناريوهات حساب الخدمة تستعمل مساعد PLAN-42 `AcceptanceHarness.CreateServiceClientAsync(clientId, scope)` (client credentials على realm القبول)، الذي يقرأ السر من `ACCEPTANCE_SVC_<CLIENT>_SECRET` ولا يكتبه في الدليل ولا في المستودع. لا يُعاد تعريفه هنا.

| ID | Positive | Negative |
|----|----------|----------|
| AC-IAM-51a (ملاحظة التحقق 1) | الشخصيتان و`employee-a`: `GET /ui/ess/EssPortal` و`/ui/hr-core/EssPortal` ← 200 بصفحة منسّقة (الملفات الثابتة 200) | `a-ess-supervisory`: POST `/ui/ess/Shifts/Create` ← 404، وPOST `/ui/hr-core/Shifts/Create/Ess` ← 403، ولا صف `Shift` جديد. `a-ess-approver`: POST `/ui/ess/Delegations/Create` و`/ui/ess/EmployeeCorrections/Approve/{id}` ← 404 بلا تغيير. والطريق المشروع للمعتمد (`/ui/hr-core/EmployeeCorrections/Approve/{id}`، PLAN-50) يبقى يعمل |
| AC-IAM-51b | `svc-hr-contracts` بنطاق lookup ← `lifecycle-flags` ← 200 | `employee-a` ← `/ui/ess/api/internal/employees/{زميل}/lifecycle-flags` ← 404، و`/api/hr-core/api/internal/...` ← 403. `employee-a` و`a-exam-marker` ← `/api/hr-core/api/lookup/employees?q=` ← 403. `a-payroll-poster` ← المنتقي نفسه ← 200 |
| AC-IAM-51c | `b-auditor` ← `/Home/Audit` ← صفوف الجهة B فقط. `platform-admin` ← صفوف الجهتين | `employee-a` و`central-a` ← `/Home/Audit` و`/ops/audit-search` ← 403 |
| AC-IAM-51d | `central-a` ينشئ `Shift` (200) | `a-supervisory-reader` وPOST `Shifts/Create` ← 403 من البوابة. و`a-management-approver` وPOST إلى `Contracts/Create` في HR.Contracts ← 403 |
| AC-IAM-51e | `svc-admin-logistics` يبدأ مسار عهدة (`POST api/workflow-instances`) ← 200 | `svc-admin-logistics` ← POST `/Employees/Create` ← 403، ولا موظف جديد |
| AC-IAM-51f | `platform-admin` ← `onboarding/agencies/apply?dryRun=true` ← 200 مع تدقيق (AC-07 أخضر) | `central-a` ← النقطة نفسها ← 403. `apply-file` ← 404 |

وخارج القبول الحي:
- اختبار تكامل بالمضيف داخل العملية (نمط [Hcm025ScopeAndPolicyTestHost.cs](../../src/Tests/HRManagement.IntegrationTests/Hcm025ScopeAndPolicyTestHost.cs)، أي مباشرة إلى الخدمة دون البوابة): `hr-employee,hr-core-supervisory` وPOST إلى كل إجراء من الإجراءات الـ 42 السابقة ← 403 (مولَّد من القائمة القديمة)، ومستخدم تفاعلي على `api/internal/*` ← 403.
- اختبار تكامل على PostgreSQL (Testcontainers) لمرشح الجهة في `GatewayAuditQueryService` بصفوف ثلاث جهات وصفوف بلا جهة.

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.ArchitectureTests
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Iam051|FullyQualifiedName~GatewayEss|FullyQualifiedName~GatewayPath|FullyQualifiedName~Iam050|FullyQualifiedName~Hcm028|FullyQualifiedName~Hcm031"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Iam051|FullyQualifiedName~Hcm025|FullyQualifiedName~GatewayAuth"
# قبول على الملف الشبيه بالإنتاج (أوامر PLAN-37)، بعد إضافة الشخصيتين:
pwsh scripts/prepare-keycloak-import.ps1
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --force-recreate
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcIam51|FullyQualifiedName~AcIam50|FullyQualifiedName~Ac07|FullyQualifiedName~AcHcm25|FullyQualifiedName~AcHcm28|FullyQualifiedName~AcHcm31"
pwsh scripts/iam051-validate.ps1
```

## Definition of done

- لا مطابقة جزئية لـ `/Ess` في البوابة، و`/ui/ess` لا يمرّر إلا المتحكمات المسماة والملفات الثابتة.
- لا نقطة `api/internal` تقبل مستخدماً تفاعلياً، و`HrCore.Lookup` لأدوار الموظفين وحسابات الخدمة فقط.
- سجل تدقيق البوابة بسياسة ومرشح جهة.
- لا قائمة أسماء في فلتر الوحدة، وكل كتابة بلا سمة تمر بسياسة الكتابة في الخدمة.
- `HRManagement.ArchitectureTests` أخضر: قائمتا HR.Core وHR.Contracts فارغتان، والقوائم الأخرى مجمّدة بأسباب.
- `hr-core.workflow` مقبول على النقاط الثلاث فقط، و`Onboarding` لـ `platform-admin`، و`apply-file` محذوف.
- AC-IAM-51a..f = Pass في `acceptance-evidence/AC-IAM-51/`، وانحدار AC-07 وAC-HCM-25/28/31 وAC-IAM-50 أخضر.
- أسطر «الحالة» لـ G-IAM-05 وG-IAM-06 وG-IAM-08 وG-IAM-09 محدّثة في الدراسة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md` |
| ServiceDefaults | `GatewayEssOptions.cs`، تعديل `GatewayPathAuthorizationMiddleware.cs` و`HrModuleAuthorizationFilter.cs` و`HrManagementAuthorizationExtensions.cs` و`ServiceAuthorizationExtensions.cs`، ثوابت السياسات الجديدة في `HrManagementRoles.cs` |
| Gateway | `HomeController.cs`، `GatewayAuditQueryService.cs`، `appsettings.json` (`Gateway:Ess`، `Gateway:Audit`) |
| Services | `Program.cs` في الخدمات الأربع عشرة (سياسة الكتابة للفلتر)، الإجراءات الـ 42 في HR.Core، متحكمات HR.Contracts، الإجراءات الحساسة في البند 4، `OnboardingController.cs`، `WorkflowInstancesController.cs`، `CodeGenerationController.cs`، `PoliciesController.cs` |
| Config | `Authorization:Policies`، `Authorization:AdminExcludedPolicies` في `appsettings.Production.json`/`Staging` للخدمات المعنية ومرآتها في `docker-compose.acceptance-prodlike.yml`؛ `Evidence:IngestClients` |
| Tests | مشروع `src/Tests/HRManagement.ArchitectureTests/` واختباراته الخمسة، وقوائم `tests/architecture/write-policy-allowlist/*.txt` و`sensitive-actions-allowlist.txt`؛ `GatewayEssRoutingTests.cs`؛ `Iam051DirectServiceWriteTests.cs` و`Iam051GatewayAuditPostgreSqlTests.cs` (تكامل)؛ حذف `HrCorePostActionsHaveExplicitPolicyTests.cs` و`hrcore-post-policy-allowlist.txt` |
| Acceptance | `AcIam51ExplicitPolicyTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)؛ الشخصيتان في `testdata/acceptance/personas.json` و`AcceptancePersonaCatalog.cs`، وتحديث العدد في [prepare-keycloak-import.ps1:137-138, 269](../../scripts/prepare-keycloak-import.ps1) (أو مولّد realm الذي يغلّفه منذ PLAN-39) و`AcceptanceV2CatalogTests.cs:86-102` و`AcceptanceIdentityPopulationTests.cs:12` (+2 على القيمة القائمة وقت التنفيذ) |
| CI | `HRManagement.slnx`، خطوة `dotnet test src/Tests/HRManagement.ArchitectureTests` في `.github/workflows/ci.yml` |
| Scripts | `scripts/iam051-validate.ps1` |
| Docs | `docs/GATEWAY_RUNBOOK.md` (ESS والتدقيق)، `docs/SERVICE_TRUST_INVENTORY.md` (أين يُقبل `hr-core.workflow`)، `docs/ESS_001_SERVICE_CENTER.md`، `docs/HIRE_RBAC_MATRIX.md` (السقف العام)، `docs/SEED_001_MASTER_DATA.md` (لا `apply-file`)، أسطر «الحالة» في `docs/IAM_001_SCOPE_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| سياسة الكتابة الافتراضية ترفض كتابة مشروعة لدور لم يُحسب (مثل المعتمد في HR.Contracts) | الجرد 0.4 يسرد كل كتابة يصلها دور قراءة أو اعتماد اليوم، ويُعطى كل منها سياسة صريحة قبل تفعيل الافتراض. وانحدار القبول الكامل في التحقق |
| إزالة المستخدم التفاعلي من `Service.HrCoreLookup` تكسر شاشة تستدعي نقطة داخلية | الجرد 0.6؛ والاختبار `InternalRoutesAreServiceOnlyTests` يمنع العودة |
| إزالة حساب الخدمة من `HrCore.Write` تكسر مستدعياً S2S غير معروف | الجرد 0.5 من الشيفرة ومن مصفوفة PLAN-37، واختبار تكامل لكل عميل في الجرد |
| مشروع اختبار يشير إلى 15 تجميعة Web يبطئ البناء | يُشغَّل مرة في CI بجانب اختبارات الوحدة. ومشروع التكامل يفعل مثله بثمانٍ |
| حجم الخطة عند حدها الأعلى | البنود مستقلة. وإن ضاق الوقت يُؤجَّل البند 4 للإجراءات الحساسة خارج HR.Core وHR.Contracts إلى قائمة السقف (`sensitive-actions-allowlist.txt`) دون كسر الهدف |

## Effort estimate

9.5–10 أيام عمل: المرحلة 0 والجرد (1)، الخدمة الذاتية (1)، نقاط الخدمات والبحث (0.5)، تدقيق البوابة (1)، الفلتر والسياسات من الإعداد والسمة (1.5)، HR.Core وHR.Contracts والإجراءات الحساسة (1.5)، S2S والجهات والأدلة و`hr-admin` (1)، المشروع المعماري (1)، القبول والتكامل والوثائق (1–1.5).
