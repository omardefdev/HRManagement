# تحقق مستقل — IAM_001_SCOPE_GAP_STUDY

**النطاق:** كل فجوات P0 (G-IAM-01..03) وكل فجوات P1 (G-IAM-04..13، G-IAM-17)، وأرقام أسطر P2/P3 (G-IAM-14..16)، وإعادة عدّ مصفوفة §2.2. قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`. بحثت عن حراس بديلة: `FallbackPolicy` (مصادقة فقط)، و`HrModuleAuthorizationFilter` المسجّل في كل `Program.cs`، و`AuthorizeFilter(GatewayDashboard)` في البوابة، وقواعد `GatewayPathAuthorizationMiddleware`، وسمات الأصناف الأساسية، و`IApplicationModelConvention` (لا يوجد)، ومُركّبات أدوار Keycloak (لا يوجد)، وملفات compose المدمجة (yml + services + observability + production) وk8s.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-IAM-01 | P0 | مؤكد | كل الأسطر صحيحة. الفلتر `Module.HrMovements` مسجّل في `Program.cs:17-18` ولا استثناء لمتحكمات الحركات. لا دور مُركّب يمنح `hr-movements` للمعتمد، والشخصية تحمل `hr-management-approver` وحده. البوابة تطبّق `HrCore.Write` على كل POST في HR.Core عدا `ApproveWorkflow`/`workflow-instances/approve` (`:180-192`)، فتُحجب الإجراءات التسعة و`Delegations`. لا أحد يقرأ `ApprovalDelegations` إلا `DelegationsController`، وكل `new SeparationOfDutiesService()` (8 مواضع) بلا تفويضات. ملاحظة: معتمد يحمل `hr-employee` أيضاً يصل إلى إجراءات `HrCore.Approve` عبر `/ui/ess/...`، وهذا تجاوز لا حل (انظر الملاحظات الجديدة). |
| G-IAM-02 | P0 | مؤكد | لا `[Authorize]` على الصنف أو الإجراء، ولا صنف أساسي، ولا فلتر غير `Module.Payroll`. البوابة تفرض `Module.Payroll` فقط (`:228-235`). لا `IValidator` لأمر `Upsert`، ولا فحص حدود الدرجة (بخلاف مستهلك `IContractActivated`). المفارقة أن `PUT api/payroll/employees/{id}/pay-profile` يتطلب `Payroll.Process` (`PayrollApiController.cs:138-139`) والنموذج MVC لا. كل الشخصيات السبع تحمل `payroll`. العدد 16 صحيح، لكن السرد كان يذكر 14 فقط. |
| G-IAM-03 | P0 | مؤكد (صُحّح دليل فرعي، والأثر أشد) | البيئة المدمجة لـ compose الإنتاج تؤكد: HR.Core بنطاق `file-storage.references` فقط وبلا `Integration__PayrollBaseUrl`/`FileStorageBaseUrl`؛ HR.Contracts وIDCard وArchive وIT بلا عنوان HR.Core وبلا `hr-core.workflow`؛ Compliance بعنوان وبلا `hr-core.workflow`. فيفشل توليد الرموز في 5 خدمات. k8s لا يربط `SVC_*` بـ `ServiceIdentity__ClientSecret` ولا يضبط النطاق، ولا شيفرة تقرأ `SVC_*`. `WorkflowTriggerService` بلا توقيع، والمسار غير مستثنى. realm JSON ينقصه 4 عملاء. **التصحيح:** بوابة الحجز القانوني تُسجَّل `PassThrough` لأنها تقرأ `Services:HRCore:BaseUrl`/`HrCore:BaseUrl` (DependencyInjection.cs:36)، وهذا المفتاح لا يضبطه إلا `docker-compose.dev-contracts.yml:18`. فالفحص معطّل في القبول أيضاً، لا في الإنتاج وحده. |
| G-IAM-04 | P1 | مؤكد | أُعيد تشغيل منطق `BolaArchitectureTests` حرفياً: 12 مخالفة بالملفات والأسطر نفسها. الاختبار أضعف مما وُصف، لأنه يتخطى أي إجراء يسبقه `[HttpPost` خلال 400 حرف، ولو كان لإجراء آخر. لا معترض EF يفرض النطاق عند الكتابة. `DailyAttendanceRecords.Create` و`Rosters.AddAssignment` و`Diwan`/`SuccessionPlans`/`WorkflowInstances.List` وخطوات `HireWizard` و`SupplyOrders`/`Maintenance`/`Receipt` كلها بلا نطاق. `TransferExecutedConsumer` لا يمس Keycloak. عدد «28 من 72» تقريبي: أجد 23–34 ملفاً بحسب العلامة المعتمدة. |
| G-IAM-05 | P1 | مؤكد | السلسلة الثابتة كاملة: المسار `ui-ess` إلى HR.Core، ونزع البادئة وتوقيع الإثبات في `GatewayAccessTokenTransform`، و`TryGetEssPolicy` يعطي `Module.Ess` لكل `/ui/ess/*`، وتخطي الفلتر لسياسات `Service.*`، و`HrCoreLookupS2S` يقبل المستخدم التفاعلي، و`/api/internal/` مستثنى من الإثبات. `ServiceRegimeInternal` عليه `[ApiController]`، فلا antiforgery. التشغيل الحي (`GET /ui/ess/api/internal/employees/{id}/lifecycle-flags` بـ `employee-a`) يثبته نهائياً. |
| G-IAM-06 | P1 | مؤكد | لا سياسة على `Audit`/`AuditSearch`/`Operations`، و`/Home/*` و`/ops/*` خارج قواعد مسارات البوابة. لا `HasQueryFilter` ولا مرشح جهة في `GatewayAuditDbContext`. لا اختبار يثبت العكس. |
| G-IAM-07 | P1 | مؤكد جزئياً | علم `forceSameActorForLab` (والخانة ظاهرة في `EditRules.cshtml:55`)، و`ValidatePrivilegedGrant` بلا فحص رتبة، و`Roles.Edit`، واعتمادات التدريب والتوزيع والاستئناف بلا SoD، كلها صحيحة. ويُضاف أن `TrainingPlans.Approve` يكتب `ApprovedBy = Guid.Empty` (`:74`). **لكن** منح الأدوار يُكتب في جدول Auth.Directory فقط، ولا يصل إلى Keycloak إلا عبر `TryAssignRealmRolesAsync` حين `Keycloak:DirectorySync:Enabled=true`، وهي معطلة في الإنتاج. ولا `IClaimsTransformation` يقرأ أدوار الدليل. فالتصعيد إلى `hr-admin` كامن حتى تُفعَّل المزامنة. |
| G-IAM-08 | P1 | مؤكد | أعدتُ المسح لكل المشاريع، وتطابقت المصفوفة (انظر أدناه). لا اختبار سقف إلا لـ HR.Core. لا فحص `AuthorizeAsync`/`IsInRole` داخل إجراءات الرواتب والحركات. عبارة «البوابة وحدها تمنعهما» متفائلة: عبر `/ui/ess` لا تمنع البوابة من يحمل `hr-employee` أيضاً. |
| G-IAM-09 | P1 | مؤكد | الأسطر صحيحة. `Onboarding` بـ `HrCore.Approve`، أي `hr-core` و`hr-management-approver` و`platform-admin`. `apply-file` يقرأ مساراً حراً. `Tenant` لا يرث `BaseEntity`. `EvidenceIngest` بلا قائمة عملاء. لا زوج تعارض لـ `hr-admin`. |
| G-IAM-10 | P1 | مؤكد (تصحيح رقمي) | `Pii.ViewNationalId` بلا مستعمل. نموذج التعديل (`Edit.cshtml:20`) والعرضان يعرضون الرقم كاملاً. الاختبار يجس 10 تجميعات، والمشروع يشير إلى 3 Web فقط. أعدتُ مسح ربط الكيانات: الأرقام لكل خدمة مطابقة (20/14/14/14/12/12/12/12/11/4)، لكن مجموعها **125** لا 135. `IssuedCards.Create` يربط `Status` من العميل فعلاً. |
| G-IAM-11 | P1 | مؤكد | `Keycloak:DirectorySync:Enabled=false` ولا يفعّله أي ملف نشر. 480 دقيقة منزلقة بلا حد مطلق، و`offline_access`، و`SignInAsync` بالمبدأ القديم. بند `employee_id` يبقى **يحتاج تشغيلاً**: تعديل الخاصية من حساب المستخدم في Keycloak 24 بالإعداد الفعلي. |
| G-IAM-12 | P1 | مؤكد | 22 دوراً، وتنقص الستة المذكورة. `directAccessGrantsEnabled` عند `:415`. k8s `args: ["start"]`. لا terraform لـ Keycloak، و`realm-rel001-staging.json` فيه دوران فقط. لا `bruteForceProtected`/`passwordPolicy`/أعمار رموز في أي ملف realm. لا فحص `acr`/`amr`. |
| G-IAM-13 | P1 | مؤكد | 15 Deployment تحقن `hr-app-secrets`. السر يضم 12 سر عميل و15 سلسلة اتصال وسر `directory-sync` والإثبات. القيمة المؤقتة لا تُرفض عند الإقلاع (لا شيفرة تفحصها). `anonymize` تحت `DsarSubjectRead`. ملاحظة: k8s يضبط `GatewayCallerProof__Secret` من مدير الأسرار، فالقيمة المؤقتة مشكلة compose الإنتاج وحده. |
| G-IAM-17 | P1 | مؤكد | AC-02..04 طلب واحد على lookup عند `9cb3705`، والملخص يؤكد النواقص، وAC-06 وAC-26..29 وAC-31 في «Not yet executed»، وAC-30 `NoResult`. أسطر 02-P1 (50، 64، 69) و`SERVICE_TRUST_INVENTORY` (11، 21) صحيحة. **صُحّح** في §1 و§2 و§4.6: AC-05 أربعة طلبات على `/api/platform/enter-tenant`، لا lookup. |
| G-IAM-14 | P2 | أسطر صحيحة | `LeaveRequestsController.cs:145`، `HrManagementRoles.cs:50`، `WorkflowPackSeeder.cs:26-49`. |
| G-IAM-15 | P2 | أسطر صحيحة | `EssController.cs:37-43, 48-63`، `PayrollReportsController.cs:67-69, 81`، `EssLearningController.cs:16-18`، `EssPortalController.cs:85-123, 173-187`. |
| G-IAM-16 | P3 | أسطر صحيحة | `GatewayCallerProofMiddleware.cs:45`، `HealthExtendedControllers.cs:208, 260`، `GatewayAccessTokenRefreshMiddleware.cs:44-49`. |

## إعادة عدّ مصفوفة §2.2

مسح آلي مستقل: سمات الإجراء والصنف مع الوراثة، وPOST/PUT/DELETE/PATCH، و`[AllowAnonymous]`.

| الخدمة | الكتابة | على الإجراء | على المتحكم | بلا سياسة | الدراسة |
|---|---|---|---|---|---|
| Payroll | 61 | 45 | 0 | 16 | مطابق |
| HR.Movements | 34 | 6 | 2 | 26 | مطابق |
| HR.Contracts | 64 | 19 | 4 | 41 | مطابق |

الخدمات الباقية مطابقة أيضاً، والمجموع 658/241/68/336 (335 في الخدمات). سطر البوابة (8/2/5/1) صحيح فقط إذا حُسب `CultureController` الموجود في ServiceDefaults. هذا المتحكم جزء تطبيق في كل خدمة أيضاً، وهو إجراء ضبط كعكة بلا أثر. لذلك **لم تُعدَّل المصفوفة**.

## التعديلات على الدراسة
- G-IAM-02: أُكمل سرد الـ 16 (`EmployeePayProfiles.Create` و`PayrollReports.BulkPayslips` و`ExportJobs.Cancel`).
- G-IAM-03: استُبدل بند «بلا عنوان HR.Core يُسجَّل PassThrough» بالسبب الصحيح (مفتاح `Services:HRCore:BaseUrl`، والفحص معطّل في القبول أيضاً). وصُحّحت خلية §2.4 المقابلة.
- G-IAM-07: أُضيف إلى دليل المنح أنه محلي ما دامت المزامنة معطلة. قُيِّد بند التصعيد في الأثر، وأُضيفت ملاحظة التصحيح إلى سطر الأثر.
- G-IAM-10: 135 → 125.
- §1 البند 5، و§2 الصف 15، و§4.6 (→ QA): فُصل AC-05 عن AC-02..04.

## ملاحظات جديدة (خارج الفجوات)
1. **`/ui/ess` يتجاوز قواعد البوابة الدقيقة لـ HR.Core** لكل من يحمل `hr-employee` مع أي دور في `Module.HrCore` (`hr-core-supervisory`، `-executive`، `hr-management-approver`، `branch-manager`). البوابة تطبّق `Module.Ess` وحدها على `/ui/ess/*` (GatewayPathAuthorizationMiddleware.cs:71-75, 240-258)، ثم تنزع البادئة وتوقّع الإثبات، فلا تفحص HR.Core إلا الفلتر وسمات الإجراء. النتيجة: دور القراءة يرسل POST إلى الإجراءات الـ 42 بلا سياسة، والمعتمد يصل إلى `HrCore.Approve` و`Delegations`. الثغرة كامنة، لأن لا شخصية تجمع `hr-employee` بدور آخر، لكن موظفي الإنتاج يحملونه عادة. إصلاح G-IAM-05 (قائمة سماح `Ess*`) يغلقها، فيجب أن يسبق أي اعتماد على البوابة طبقةً وحيدة.
2. توفير Auth.Directory لمستخدمي Keycloak يكتب الخاصية `employeeId` (KeycloakUserProvisioningService.cs:72-74)، ومُعيِّن الرمز يقرأ `employee_id` (realm template:482-488). فمن يُنشأ بالمزامنة بعد تفعيلها (إصلاح G-IAM-11) لن يحمل مطالبة `employee_id`، فتُرفض خدمته الذاتية.
3. `EmployeePayProfiles.Create` (POST) لا يفحص نطاق الفرع، بخلاف `Index`/`History` في المتحكم نفسه. كامن، لأن لا شخصية مقيّدة تصل الرواتب اليوم.
4. `TrainingPlans.Approve` يسجّل المعتمد `Guid.Empty` (TrainingExtendedControllers.cs:74)، فلا أثر تدقيق لمن اعتمد.
