# دراسة الصلاحيات والنطاق التنظيمي وثقة الخدمات (IAM-001) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** التفويض في كل الخدمات والبوابة: الأدوار والسياسات (`HrManagementAuthorizationExtensions`، `HrModuleAuthorizationFilter`، `GatewayPathAuthorizationMiddleware`، `KeycloakRoleMapping`)، شخصيات القبول، وrealm في Keycloak؛ إجراءات الكتابة بلا سياسة صريحة؛ نطاق الجهة والفرع (BOLA/IDOR)؛ الخدمة الذاتية؛ الثقة بين الخدمات (S2S)؛ فصل المهام والتفويض وتجاوزات المسؤول وMFA والجلسة. لا تخلط هذه الدراسة مع بطاقة IAM-001 في `completion-backlog` (JWT وKeycloak)، فهي إحدى الوثائق المقارَنة.  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والإعدادات والوثائق عند `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. مُسحت المتحكمات آلياً (1,415 إجراءً عاماً، منها 658 إجراء POST/PUT/DELETE في 15 مشروع Web) وطابق المسح قائمة HR.Core المجمّدة (42 إجراءً) تماماً. لا توجد Minimal APIs للكتابة.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً.  
**الوثائق المقارَنة:** [PLAN-01](acceptance-plans/PLAN-01-center-branch-isolation.md) · [PLAN-07](acceptance-plans/PLAN-07-national-id-mass-assignment.md) · [PLAN-11](acceptance-plans/PLAN-11-file-dsar-s2s.md) · [P0: SEC-001..004، IAM-001، IAM-002](completion-backlog/01-P0-RELEASE-BLOCKERS.md) · [P1: TEN-001..003، IAM-003، IAM-004](completion-backlog/02-P1-PLATFORM-INTEGRITY.md) · [HIRE_RBAC_MATRIX.md](HIRE_RBAC_MATRIX.md) · [IAM_002_ORG_SCOPE.md](IAM_002_ORG_SCOPE.md) · [SERVICE_TRUST_INVENTORY.md](SERVICE_TRUST_INVENTORY.md) · [iam/realm-rebuild-checklist.md](iam/realm-rebuild-checklist.md) · [EXT_001_MFA_POLICY.md](EXT_001_MFA_POLICY.md) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md)

---

## 1. الخلاصة

1. **الأساس سليم، والتطبيق غير مكتمل.** التحقق من JWT صارم (الجمهور والمُصدِر دائماً)، وعزل الجهة متعدد الطبقات (مرشحات EF وRLS ومعترض يرفض الكتابة عبر الجهات)، ونطاق الفرع مصمم fail-closed، وتوظيف HR.Core محكم بعد PLAN-25. لكن البطاقات IAM-001..004 وSEC-003 وTEN-* موسومة «Done» بينما الشيفرة تُظهر أن التفويض الدقيق يقع في البوابة أساساً، وأن 335 إجراء كتابة في الخدمات بلا سياسة صريحة، وأن ربط هوية الخدمات للإنتاج يختلف عما يُختبر.
2. **ثلاثة عوائق P0:**
   - **(أ)** دور `hr-management-approver` لا يصل إلى اعتماد النقل والترفيع والندب والإعارة، لأن سياسة الإجراء خارج سياسة الوحدة. والتفويض المؤقت يُحفظ ولا يقرؤه شيء (G-IAM-01).
   - **(ب)** الراتب الأساسي وسلّم الرواتب والدرجات تُعدَّل بدور الوحدة `payroll` وحده، وبلا معتمد ثانٍ (G-IAM-02).
   - **(ج)** نطاقات هوية الخدمات وأسرارها وعناوينها في إعدادات الإنتاج (compose وk8s) أضيق مما في القبول. فيفشل توليد الرموز في 5 خدمات وتعيين HR.Contracts والتحقق من راتب العرض، ويبقى فحص الحجز القانوني مفتوحاً دائماً (G-IAM-03).
3. **ثغرات P1 في سلامة القرار:**
   - نطاق الفرع غائب عن قوائم وإجراءات يصلها مدير الفرع (الديوان، المناوبات، معالج التعيين، التوريد). و`BolaArchitectureTests` أحمر بـ 12 مخالفة، وهو ضعيف حتى لو اخضرّ.
   - مسار `/ui/ess` يمرّر الموظف إلى نقاط داخلية تكشف الحجز القانوني لأي زميل.
   - سجل تدقيق البوابة مقروء لكل مستخدم مسجّل وعبر الجهات.
   - تجاوزات فصل المهام: علم `forceSameActorForLab` في الطلب، ومعتمد ثانٍ اسمي لمنح `hr-admin`، واعتمادات بلا «منشئ ≠ معتمد».
   - سياسات واسعة: `hr-admin` شامل، والنطاق `hr-core.workflow` يساوي كتابة HR.Core كاملة، و`Onboarding` متاح لكل `hr-core`.
   - الرقم الوطني يظهر صريحاً في التوظيف ونموذج التعديل. واختبار Mass Assignment يغطي 3 من 14 تجميعة.
   - دورة الهوية معطلة في الإنتاج (المغادر يبقى في Keycloak). وrealm الإنتاج غير مُقنَّن، وMFA وثيقة فقط.
4. **فجوات قدرات (P2):** النطاق مربوط بدور مدير الفرع وحده، فلا يوجد موظف رواتب أو عقود إقليمي مقيّد. ولا اعتماد للمدير المباشر. ودور `auditor` بلا أي صلاحية. والخدمة الذاتية ناقصة: قسيمة PDF وتعلّم ذاتي محجوبان، ولا حالة إجازة، وطلب البطاقة لا يصل إلى IDCard.
5. **فجوة تحقق:** أدلة AC-02..04 «Pass» كل منها طلب واحد على `/api/lookup`، وAC-05 أربعة طلبات على `/api/platform/enter-tenant`، وكلها عند SHA قديم (`9cb3705`، 2026-08-24). وAC-06 وAC-26..29 وAC-31 لم تُشغَّل، وAC-30 بلا نتيجة. قائمة إعادة بناء realm تصف عملاء وأدواراً غير موجودة في الشيفرة. و`OrgScopeRequirement` المذكور دليلاً لـ IAM-002 لا تستعمله أي سياسة.
6. **توائم نمط PLAN-25 («عمل لا يحدث بصمت»)** في هذا المجال: مزامنة Keycloak معطلة وتُتجاهل (`Attempted=false`)؛ فحص الحجز القانوني fail-open؛ تفويض يُخزَّن ولا يُطبَّق؛ تغيير راتب يسري فور الحفظ؛ وتوليد رمز يفشل بتحذير في السجل.

**التقدير الإجمالي:** نحو 62–79 يوم عمل لمطوّر واحد (≈ 4 sprints لفريق من مطوّرَين)، منها 18–24 يوماً للمرحلة P0 (IAM-A وIAM-B وIAM-C).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | الطبقة | التنفيذ الحالي | الحكم |
|---|--------|----------------|-------|
| 1 | realm في Keycloak | قالب تطوير (22 دوراً) + سكربت مزامنة للتطوير؛ لا realm إنتاج في المستودع | ❌ G-IAM-12 |
| 2 | التحقق من الرمز | `ValidateAudience=true` دائماً، حُذف جمهور `account`؛ كل وحدة تقبل `hr-gateway-client` | ✅ (الأثر في G-IAM-13) |
| 3 | البوابة حسب المسار | سياسة وحدة لكل مقطع؛ تفصيل قراءة/كتابة في `hr-core` فقط، و`HrContractsApprove` لكل كتابة عقود، و`ModulePayroll` للرواتب | ⚠️ G-IAM-01، G-IAM-05، G-IAM-06 |
| 4 | فلتر الوحدة في الخدمة | `HrModuleAuthorizationFilter` مع استثناءات بأسماء المتحكمات | ⚠️ G-IAM-08، G-IAM-16 |
| 5 | سياسات الإجراءات | 241 بسياسة على الإجراء + 68 على المتحكم؛ 336 بلا سياسة صريحة (335 في الخدمات)؛ سقف مجمّد في HR.Core فقط | ⚠️ G-IAM-08 |
| 6 | الأدوار مقابل الواجبات | المعتمد لا يصل إلى الحركات؛ `auditor` بلا وصول؛ أدوار خطوات غير موجودة | ❌ G-IAM-01، G-IAM-14 |
| 7 | نطاق الجهة | مرشحات EF + RLS + `TenantIdStampInterceptor` + مطالبة `tenant_id` إلزامية في البوابة | ✅ (عدا تدقيق البوابة وOnboarding: G-IAM-06، G-IAM-09) |
| 8 | نطاق الفرع | `OrgAccessScope` fail-closed لدور `branch-manager`؛ مطبّق في 28 من 72 ملف متحكم في HR.Core، ونادراً خارجه | ⚠️ G-IAM-04 |
| 9 | الخدمة الذاتية | الموظف من مطالبة `employee_id` لا من النموذج؛ القسيمة والتعلّم محجوبان | ⚠️ G-IAM-15، G-IAM-11 |
| 10 | الثقة بين الخدمات | client credentials + نطاق + جمهور؛ إعداد الإنتاج منحرف، سر واحد لكل الحاويات | ❌ G-IAM-03، G-IAM-13 |
| 11 | فصل المهام والتفويض | SoD بالمعرّف في النقل والإجازة والتوريد والأرشيف؛ تجاوز بعلم في الطلب؛ التفويض لا يُقرأ | ⚠️ G-IAM-07، G-IAM-01 |
| 12 | الجلسة وMFA | كعكة 8 ساعات منزلقة + `offline_access`؛ MFA في وثيقة فقط | ❌ G-IAM-11، G-IAM-12 |
| 13 | البيانات الشخصية والإسناد الجماعي | تقنيع الرقم الوطني للموظف؛ يظهر في التوظيف والتعديل؛ اختبار جزئي | ⚠️ G-IAM-10 |
| 14 | دورة حساب المستخدم | المزامنة مع Keycloak معطلة افتراضياً وفي الإنتاج | ❌ G-IAM-11 |
| 15 | التحقق والأدلة | AC-02..04 على lookup فقط، وAC-05 على enter-tenant؛ AC-06 و26–29 و31 لم تُشغَّل | ❌ G-IAM-17 |

### 2.1 مصفوفة الأدوار × الوحدات (ما يصل إليه الدور فعلاً عبر البوابة والخدمة)

الرموز: **W** قراءة وكتابة · **R** قراءة · **A** اعتماد خطوات · **✗** لا وصول · **⚠** يحتاجه واجب الدور ولا يصل.

| الدور | HR.Core | العقود | الرواتب | الحركات | التدريب/الصحة | وحدات الدور الواحد* | ESS | ملاحظة |
|------|---------|--------|---------|---------|---------------|--------------------|-----|--------|
| `hr-admin` | W | W | W (المعالجة والاعتماد والترحيل والعكس) | W | W + السريري | W | W | شامل (G-IAM-09) |
| `hr-core` | W | معالج التعيين فقط (عرض + تنفيذ) | ✗ | ✗ | ✗ | ✗ | ✗ | — |
| `hr-core-supervisory` / `-executive` | R (حسب الفئة) | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | فلتر الوحدة يقبلهما على 42 POST؛ البوابة وحدها تمنع (G-IAM-08) |
| `branch-manager` (+`hr-core`، +`admin-logistics` في الشخصيات) | W + A، والنطاق جزئي | معالج التعيين **بلا نطاق** | ✗ | ✗ | ✗ | اللوجستيات W، والنطاق جزئي | ✗ | G-IAM-04 |
| `hr-management-approver` | R + A؛ **⚠** 9 إجراءات `HrCore.Approve` والتفويض محجوبة في البوابة | R + `Step3Approve` | ✗ | **⚠ ✗** (الاعتماد يتطلب الوحدة) | ✗ | ✗ | ✗ | G-IAM-01 |
| `hr-contracts` | ✗ | W (كل كتابة) | ✗ | ✗ | ✗ | ✗ | ✗ | لا فصل صانع/معتمد بالدور |
| `payroll` (الأساسي) | ✗ | ✗ | R + **16 كتابة** منها الراتب والسلّم | ✗ | ✗ | ✗ | ✗ | G-IAM-02 |
| `payroll-processor/approver/poster/reverser` | ✗ | ✗ | تحتاج `payroll` أيضاً | ✗ | ✗ | ✗ | ✗ | `poster` و`reverser` غائبان عن قالب realm (G-IAM-12) |
| `hr-movements` | ✗ | ✗ | ✗ | W عدا الاعتماد | ✗ | ✗ | ✗ | يُنفّذ بلا سياسة صريحة |
| `hr-training` / `hr-health` / `hr-health-clinical` | ✗ | ✗ | ✗ | ✗ | W / W / W + السريري | ✗ | ✗ | `hr-health-clinical` غائب عن القالب |
| `idcard`، `records-archive`، `admin-logistics`، `it-infra`، `dev-analytics`، `compliance`، `auth-directory`، `file-storage` | ✗ (`compliance`: الاستئنافات وصندوق العمل فقط؛ **⚠** خطوات «مراجعة قانونية» في الانفصال والتأديب) | ✗ | ✗ | ✗ | ✗ | W على وحدته (لا قراءة/كتابة/اعتماد منفصلة) | ✗ | G-IAM-08، G-IAM-14 |
| `hr-employee` | ESS (البوابة والترشّح) + البحث + **نقاط داخلية عبر `/ui/ess`** | ✗ | قائمة القسائم؛ **⚠** PDF محجوب | ✗ | **⚠** التعلّم الذاتي محجوب | ✗ | W | G-IAM-05، G-IAM-15 |
| `platform-admin` | Onboarding والحجر فقط | ✗ | ✗ | ✗ | ✗ | التحليلات W + الأدلة | ✗ | — |
| `auditor` | **⚠ ✗** | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | لا سياسة تذكره (G-IAM-14) |
| `hr-exam-marker` | شاشات التصحيح + البحث | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | — |
| أي مستخدم مسجّل | البحث `/api/lookup` | — | — | — | — | — | — | **سجل تدقيق البوابة لكل الجهات** (G-IAM-06) |

\* IDCard وRecords.Archive وAdmin.Logistics وIT.Infra وDev.Analytics وCompliance وAuth.Directory وFile.Storage: سياسة الوحدة = Admin أو دور الوحدة ([HrManagementAuthorizationExtensions.cs:39-55](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).

### 2.2 إجراءات الكتابة بلا سياسة صريحة (مسح آلي)

«بلا سياسة» = لا `[Authorize(Policy|Roles)]` على الإجراء ولا على المتحكم، ولا `[AllowAnonymous]`. هذه الإجراءات يحرسها فلتر الوحدة وقاعدة البوابة فقط.

| الخدمة | الكتابة | على الإجراء | على المتحكم | **بلا سياسة** | مجهول مبرَّر | سقف؟ | أخطر الأمثلة |
|--------|--------|-------------|-------------|---------------|-------------|------|--------------|
| HR.Core | 252 | 153 | 44 | **42** | 13 (`/careers`، `/careers/my`) | ✅ 42 | `Rosters.AddAssignment`، `Documents.Delete`، `EmployeeProfiles.Save`، `DailyAttendanceRecords.ClosePeriod` |
| HR.Contracts | 64 | 19 | 4 | **41** | 0 | ✗ | `Contracts.Activate`، `SeparationWizard.Step3Execute`، `AdministrativeDecisions.Create/Delete`، `LeaveBalances.InitializeYear` |
| Payroll | 61 | 45 | 0 | **16** | 0 | ✗ | `EmployeePayProfiles.Create`، `SalaryScales`/`PayGrades` Edit/Delete، `PayrollRuns.Create` |
| HR.Movements | 34 | 6 | 2 | **26** | 0 | ✗ | `TransferRequests.Execute`، `PromotionRequests.Execute`، `DistributionPlans.Approve` |
| HR.Training | 32 | 0 | 2 | **30** | 0 | ✗ | `TrainingNominations.Approve`، `MissionFiles.Approve`، `TrainingPlans.Approve` |
| HR.Health | 30 | 6 | 0 | **24** | 0 | ✗ | `InsuranceCards.Revoke`، `MedicalCommitteeReports.Edit`، `HealthStatusEntries.Create` |
| Compliance | 34 | 0 | 4 | **30** | 0 | ✗ | `DisciplinaryActions.Create/DecideAppeal`، `Complaints.Resolve` |
| IDCard | 25 | 0 | 0 | **25** | 0 | ✗ | `IssuedCards.Revoke/UrgentRevoke`، `AccessPermissions.*`، `OfficialSealRecords.*` |
| Admin.Logistics | 28 | 0 | 0 | **28** (2 تحويل فقط) | 0 | ✗ | `Assets.MarkLost/CancelAsset`، `Vendors.Delete` |
| IT.Infra | 25 | 0 | 0 | **25** | 0 | ✗ | `SystemPermissionRecords.Create/Revoke`، `BackupJobs.Run/Delete` |
| Records.Archive | 24 | 0 | 0 | **24** | 0 | ✗ | `ArchiveItems.ReleaseLegalHold/RecordDisposal/UpdateRetention` |
| Dev.Analytics | 19 | 1 | 1 | **17** | 0 | ✗ | `ReadModels.Rebuild`، `EtlJobs.Run` |
| Auth.Directory | 8 | 0 | 1 | **7** | 0 | ✗ | `Roles.Edit/Delete`، `AuthUsers.Edit` (منح الأدوار) |
| File.Storage | 14 | 9 | 5 | 0 | 0 | — | — |
| Gateway | 8 | 2 | 5 | 1 (`Culture.SetCulture`) | 0 | — | — |
| **المجموع** | **658** | **241** | **68** | **336** | **13** | | |

### 2.3 مصفوفة نطاق الفرع: الخدمة × (القوائم، التفاصيل، التصدير، الإجراءات)

النطاق يُفرض على دور `branch-manager` وحده. لذلك يهم أولاً من يصل فعلاً إلى كل خدمة بدور مقيّد. «كامنة» تعني أن لا شخصية مقيّدة تصل الخدمة اليوم، لكن الثغرة تظهر حين تمنح الجهة دور الوحدة لموظف فرع.

| الخدمة | من يُقيَّد فعلاً | القوائم | التفاصيل | التصدير/التقارير | الإجراءات (POST) |
|--------|-----------------|---------|----------|------------------|------------------|
| HR.Core | مدير الفرع (+`hr-core`) | ⚠️ الموظفون والسجلات والمستندات والتوظيف ✅؛ الديوان والتفتيش الميداني والإحصاءات والمناوبات والتعاقب ونسخ المسارات ❌ | ⚠️ الكيانات الأساسية ✅؛ المناوبات والتعاقب والتواقيع ❌ | ✅ `ReportsController` + إعادة فحص التنزيل (`ExportJobAuth`) | ⚠️ `DailyAttendanceRecords.Create`، `Rosters.AddAssignment`، `DiwanCorrespondences.Create` ❌ |
| HR.Contracts | مدير الفرع عبر معالج التعيين | ⚠️ الإجازة والسلفة والأرصدة ✅؛ العقود والانفصال والتجربة والمباشرة والمعاملات والمعالج ❌ | ❌ (`Contracts.Details` وخطوات المعالج) | ❌ كامنة | ❌ خطوات المعالج بمعرّف المعاملة |
| Payroll | لا أحد (كامنة) | ⚠️ ملفات الأجر والمتأخرات ✅؛ الحجوزات ونهاية الخدمة والبدلات والاقتطاعات والتسويات والإنذارات ❌ | ❌ `PayrollReports.Payslip` | ❌ السجل وCSV ونهاية الخدمة | ❌ |
| HR.Movements | لا أحد (كامنة) | ⚠️ النقل والتوزيع والإعارة ✅؛ الندب والترفيع والتكليف والنشرات ❌ | ⚠️ | — | ⚠️ النقل ✅، الباقي ❌ |
| HR.Health / HR.Training | لا أحد (كامنة) | ⚠️ السجلات الطبية فقط / ❌ | ⚠️ / ❌ | — | ❌ / ⚠️ ترشيح واحد |
| Admin.Logistics | br1-mgr، br2-mgr | ⚠️ الأصول والعهد ✅؛ التوريد والمخزون والصيانة والاستلام ❌ | ⚠️ | — | ⚠️ الأصول ✅، الباقي ❌ |
| IDCard، IT.Infra، Records.Archive، Compliance، Dev.Analytics، Auth.Directory | لا أحد (كامنة) | ❌ | ❌ | ❌ | ❌ |
| File.Storage | منحة لكل ملف (جهة/حامل/وحدة) | n/a | ✅ الجهة + 404 عند الرفض | — | ✅ |
| سجل تدقيق البوابة | الجميع | ❌ عبر الجهات | — | — | — |

### 2.4 مصفوفة الثقة بين الخدمات

| المستدعي ← المستدعى | النقطة | المطلوب | المصادقة الفعلية | إعداد الإنتاج (compose) | النتيجة المتوقعة |
|---------------------|--------|---------|------------------|------------------------|------------------|
| HR.Contracts ← HR.Core | `/api/internal/employees/{id}/lifecycle-flags` | `Service.HrCoreLookup` | **لا شيء**: عميل HTTP بلا `ServiceAuthHandler` | المفتاح `Services:HRCore:BaseUrl` غير مضبوط (هنا وفي القبول)، فيُسجَّل `PassThrough` | فحص الحجز القانوني مفتوح دائماً |
| HR.Contracts ← HR.Core | lookup، الأنظمة النافذة، توليد الرموز | `hr-core.lookup` + `hr-core.workflow` | client credentials | النطاق `file-storage.references` فقط، بلا عنوان | 403 أو «BaseAddress must be set» ← التعيين يتعطل |
| HR.Core ← Payroll | `/api/internal/pay-grades/{code}` | `payroll.grades.read` + `svc-hr-core` | client credentials | نطاق ناقص، بلا عنوان | «Unavailable» ← كل عرض استثناء |
| HR.Core ← File.Storage | `service-upload` | `file-storage.service-upload` | client credentials + إثبات | نطاق ناقص | رفع مستندات المرشح يفشل |
| IDCard، Archive، Compliance، IT، HR.Contracts ← HR.Core | `/api/internal/codes/generate` | `HrCore.Write` (`hr-core.workflow`) | client credentials | لا أحد منها يطلب النطاق | «تعذّر توليد الرمز» |
| Admin.Logistics ← HR.Core | `/api/workflow-instances` | `HrCore.Write` + إثبات البوابة | client credentials **بلا إثبات** | فرض الإثبات مفعّل | 403 ← لا مسار اعتماد للعهدة (→ WF) |
| كل الخدمات ← HR.Core | `/api/internal/policies/enforce` | `HrCore.BranchOperation` (`hr-core.workflow`) | client credentials | اللوجستيات فقط | 403 ← بديل ثابت يسمح (→ POL) |
| Compliance ← HR.Core/Payroll/Files | `/api/internal/dsar-subjects` | `dsar.subject.read` + `svc-compliance` | client credentials | من `services.yml` ✅؛ العميل غائب عن realm JSON | يعمل إن أُنشئ العميل يدوياً |
| Payroll، Movements ← HR.Core | lookup، الأنظمة النافذة | `hr-core.lookup` | client credentials + إثبات | ✅ | يعمل |
| IT.Infra ← Auth.Directory | `/api/internal/directory/sync-employee` | `directory.sync` | client credentials | ✅ | يعمل |
| Auth.Directory ← HR.Core | lookup | `Service.HrCoreLookup` (العميل غير مسموح) | بلا `ServiceIdentity:ClientId` | ✗ | اسم الموظف «غير متاح» (يحتاج تحقق) |
| البوابة ← كل الوحدات | كل المسارات | رمز المستخدم + HMAC | سر مشترك واحد | السر غير مضبوط، فتُستعمل القيمة المؤقتة | الإثبات قابل للتزوير (G-IAM-13) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- التحقق من JWT صارم: الجمهور مُفعّل دائماً، وحُذف جمهور `account` المشترك، ويُفحص المُصدِر وانحراف الساعة ([KeycloakExtensions.cs:67-109, 207-242](../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs)). والوحدات تُشغَّل بـ JwtBearer فقط خارج التطوير ([ModuleAuthenticationExtensions.cs:18-25](../src/Shared/HRManagement.ServiceDefaults/ModuleAuthenticationExtensions.cs)).
- نطاق الفرع fail-closed فعلاً: مطالبة مفقودة أو تالفة أو وحدة جذر غير موجودة تعطي `Denied` لا «كل شيء» ([OrgScopeResolver.cs:36-62](../src/Shared/HRManagement.ServiceDefaults/OrgScopeResolver.cs)، [HrOrgScopeVisibility.cs:159-179](../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs)).
- عزل الجهة متعدد الطبقات: `TenantIdStampInterceptor` يرفض الإدراج في جهة أخرى ونقل الصفوف بين الجهات ([TenantIdStampInterceptor.cs:63-148](../src/Shared/HRManagement.ServiceDefaults/TenantIdStampInterceptor.cs))، و`tenant_id` إلزامي في البوابة ([GatewayPathAuthorizationMiddleware.cs:105-111](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs))، والمطالبة تتقدم على الترويسة ([TenantIdMiddleware.cs:44-50](../src/Shared/HRManagement.ServiceDefaults/TenantIdMiddleware.cs)).
- التوظيف في HR.Core بعد PLAN-25: سياسات صريحة، ودور الخطوة في الخادم، و`RecruitmentOrgScopeFilter`، وسقف مجمّد بـ 42 إجراءً مطابق للشيفرة.
- تقارير HR.Core وتصديرها مقيدة بالنطاق ([ReportsController.cs:37-43, 83-90, 117-127](../src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs))، وتنزيل مهمة التصدير يعيد فحص المستخدم والجهة والنطاق ([ExportJobAuth.cs:50-60](../src/Shared/HRManagement.ServiceDefaults/ExportJobAuth.cs)).
- أدوار الرواتب الأربعة منفصلة، مع أزواج متعارضة في منح الأدوار ([SodMatrixDefaults.cs:165-190](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs)).
- طلب النقل يفحص نطاق المصدر والهدف معاً ويمنع المنشئ من الاعتماد ([TransferRequestsController.cs:37-45, 205-216](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)). والأصول في اللوجستيات مقيدة في كل الإجراءات ([AssetsController.cs:175, 262, 297, 385-388](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs)).
- الخدمة الذاتية تأخذ هوية الموظف من المطالبة لا من النموذج ([EssPortalController.cs:19-28](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)، [Payroll EssController.cs:33-57](../src/Services/Payroll/Payroll.Web/Controllers/EssController.cs)).
- نقاط DSAR الداخلية مقيدة بعميل `svc-compliance` ونطاقه ([ServiceAuthorizationExtensions.cs:95-99](../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)). ومنصة الملفات تفوّض لكل ملف (إبطال، انتهاء المنحة، الجهة) وتعيد 404 عند الرفض ([IFileAccessPolicy.cs:33-74](../src/Shared/HRManagement.ServiceDefaults/Files/IFileAccessPolicy.cs)). انتهاء المنحة الذي طلبه PLAN-11 لـ AC-29 موجود (`:40-41`)، والتشغيل الحي غائب.
- مستهلك الانفصال يعطّل حساب Keycloak عندما تكون المزامنة مفعّلة ([SeparationCompletedConsumer.cs:93-106](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs)).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-IAM-01 — دور «الاعتماد الإداري» لا يصل إلى اعتمادات الحركات، وإجراءات `HrCore.Approve` محجوبة في البوابة، والتفويض بلا أثر
- **الدليل:**
  - اعتماد النقل ورفضه يتطلبان `HrManagement.Approve`، أي Admin أو `hr-management-approver` ([TransferRequestsController.cs:183-186, 251-254](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs)، [HrManagementAuthorizationExtensions.cs:149-151](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). ومثلها اعتماد الندب والإعارة والترفيع والتكليف ([MovementsExtendedControllers.cs:333-336, 679-682, 949-952, 1113-1116](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)).
  - لكن كل متحكمات HR.Movements تمر أولاً بـ `Module.HrMovements`، أي Admin أو `hr-movements` ([HR.Movements.Web/Program.cs:18](../src/Services/HRMovements/HR.Movements.Web/Program.cs)، [HrManagementAuthorizationExtensions.cs:39](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). والبوابة تفرض السياسة نفسها على `/ui/hr-movements` ([GatewayPathAuthorizationMiddleware.cs:17-35](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - التقاطع إذن `hr-admin` أو من يحمل الدورين معاً. الشخصية `a-management-approver` تحمل `hr-management-approver` وحده، وهي مخصصة لـ AC-09 وAC-14 ([personas.json:69-83](../testdata/acceptance/personas.json))، وكلاهما لم يُشغَّل. وثّقت FUN-003 الحالة للحركات (G-MOV-01)؛ والنمط هنا عام.
  - في HR.Core تفرض البوابة `HrCore.Write` على كل POST عدا مسارات الاعتماد المسماة ([GatewayPathAuthorizationMiddleware.cs:180-192](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). فلا يصل المعتمد إلى 9 إجراءات سياستها `HrCore.Approve`:
    - `EmployeeCorrections.Approve/ApproveApi` ([EmployeeCorrectionsController.cs:133-134, 175-176](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeCorrectionsController.cs))؛
    - `EmploymentRecords.Approve` ([EmploymentRecordsController.cs:189-190](../src/Services/HRCore/HR.Core.Web/Controllers/EmploymentRecordsController.cs))؛
    - `Competitions.Finalize/Cancel` ([CompetitionsController.cs:452-453, 776-777](../src/Services/HRCore/HR.Core.Web/Controllers/CompetitionsController.cs))؛ و`HIRE_RBAC_MATRIX.md` يعترف بهذه الحالة؛
    - `PolicyTemplates.PublishFromUi` و`Policies.Publish` ([PolicyTemplatesController.cs:173-174](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)، [PoliciesController.cs:102-103](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs))؛
    - `Approvals.Approve/Reject` ([ApprovalsController.cs:102-105, 151-154](../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs)).
  - ولا يصل المعتمد كذلك إلى `Delegations.Create/Revoke` (`SoD.ManageDelegations`) ([DelegationsController.cs:13, 74-76, 110-112](../src/Services/HRCore/HR.Core.Web/Controllers/DelegationsController.cs)).
  - التفويض يُحفظ ولا يُقرأ:
    - `Delegations.Create` يحفظ `ApprovalDelegation` باسم المستخدم الحالي ([DelegationsController.cs:76-107](../src/Services/HRCore/HR.Core.Web/Controllers/DelegationsController.cs)).
    - كل إنشاء لـ `SeparationOfDutiesService` يمر بلا تفويضات، مثل [TransferRequestsController.cs:211](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs) و[LeaveRequestHandlers.cs:238](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)؛ والخدمة لا تستعمل التفويضات إلا إن مُرِّرت ([SeparationOfDutiesService.cs:13-21](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs)).
    - تحويل بند صندوق العمل يرمي `NotSupportedException` ([HrCoreWorkInboxService.cs:35-36](../src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs)).
    - الكيان `WorkflowDelegation` معرَّف بلا أي مستخدم ([WorkflowDelegation.cs:9](../src/Services/HRCore/HR.Core.Domain/Entities/Workflow/WorkflowDelegation.cs)).
- **الأثر:**
  - المعتمد الإداري، بنموذج الأدوار الموثّق، لا يستطيع اعتماد أي نقل أو ترفيع أو ندب أو إعارة.
  - الحل العملي الوحيد إعطاؤه `hr-movements`. عندها يستطيع الإنشاء والتنفيذ أيضاً، و`TransferRequests.Execute` بلا سياسة صريحة.
  - غياب المعتمد يوقف الاعتمادات، لأنه لا يوجد تفويض فعّال.
- **الإصلاح المقترح:**
  - سياسة «اعتماد فقط» تعبر فلتر الوحدة للإجراءات المسماة، كما فُعل لـ `compliance` في PLAN-28. أو نقل اعتماد الحركات إلى محرك المسارات.
  - استثناءات بوابة صريحة لإجراءات `HrCore.Approve`.
  - ربط التفويض بـ `WorkflowStageGuard` وSoD: دور المفوِّض، والمدة، والنطاق، والتدقيق. أو إزالة الشاشة.
- **الخطة:** [PLAN-50](acceptance-plans/PLAN-50-iam-approver-reach-delegation.md)
- **التحقق المطلوب:**
  - **AC-IAM-Aa:** `a-management-approver` يعتمد عبر البوابة نقلاً أنشأه `a-movements` (200، الحالة `Approved`). سلبي: لا يستطيع الإنشاء ولا التنفيذ (403)، و`a-movements` لا يعتمد (403).
  - **AC-IAM-Ab:** معتمد يفوّض زميلاً لمدة. المفوَّض يعتمد خطوة `hr-management-approver` داخل المدة، ويُرفض بعدها، ويظهر التفويض في سجل الخطوة.

#### G-IAM-02 — الراتب الأساسي وسلّم الرواتب يُعدَّلان بدور الوحدة `payroll` وحده وبلا معتمد
- **الدليل:**
  - `EmployeePayProfilesController` بلا `[Authorize]` على الصنف ولا على `Create` POST ([EmployeePayProfilesController.cs:14, 79-101](../src/Services/Payroll/Payroll.Web/Controllers/EmployeePayProfilesController.cs)). فيكفي `Module.Payroll`، أي `payroll` أو Admin ([HrManagementAuthorizationExtensions.cs:41](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). والبوابة تفرض `Module.Payroll` فقط على كتابة الرواتب ([GatewayPathAuthorizationMiddleware.cs:228-235](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - `UpsertEmployeePayProfileHandler` يعطّل الملف النافذ وينشئ ملفاً بالراتب المُدخل ويحفظ فوراً. لا اعتماد، ولا تحقق من حدود الدرجة ([EmployeePayProfileHandlers.cs:20-49](../src/Services/Payroll/Payroll.Application/Features/EmployeePayProfiles/EmployeePayProfileHandlers.cs)). والإجراء يربط الكيان `EmployeePayProfile` مباشرة.
  - بلا سياسة كذلك: `SalaryScales` و`PayGrades` و`CompensationTypes` و`DeductionTypes` (إنشاء/تعديل/حذف)، و`PayrollRuns.Create` ([PayrollRunsController.cs:77-80](../src/Services/Payroll/Payroll.Web/Controllers/PayrollRunsController.cs)، [PayGradesController.cs:45-50](../src/Services/Payroll/Payroll.Web/Controllers/PayGradesController.cs)). المجموع 16 إجراءً مع `EmployeePayProfiles.Create` و`PayrollReports.BulkPayslips` و`ExportJobs.Cancel` (الأخيران ليسا بيانات رئيسية).
  - تعليق السياسات نفسه يقول إن الدور الأساسي «module access only» ([HrManagementAuthorizationExtensions.cs:130-131](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). وكل شخصيات الرواتب تحمل `payroll` ([personas.json:229-240](../testdata/acceptance/personas.json)).
- **الأثر:**
  - أي موظف رواتب يغيّر الراتب الأساسي لأي موظف، أو حدود سلّم الرواتب، أو يحذف درجة. ويشمل ذلك المعتمد والمرحِّل ومن مُنح `payroll` للاطلاع فقط.
  - يدخل التغيير المسير التالي دون معتمد ثانٍ ودون مرجع قانوني.
  - هذا تجاوز مباشر لبوابة P0 في `completion-backlog/01`: «لا تجاوز مباشر لقواعد الفصل والرواتب».
- **الإصلاح المقترح:**
  - سياسة `Payroll.MasterData` (أو `Payroll.Process`) لكل كتابة في الرواتب.
  - تغيير الراتب معاملة: مسودة ← اعتماد `payroll-approver` مختلف ← سريان، مع سبب ومرجع.
  - المصدر الطبيعي للراتب `IContractActivated`؛ والتعديل اليدوي استثناء مدقَّق.
  - اختبار سقف للرواتب مثل HR.Core.
- **الخطة:** [PLAN-53](acceptance-plans/PLAN-53-iam-payroll-salary-change.md)
- **التحقق المطلوب:**
  - **AC-IAM-Ba:** مستخدم بـ `payroll` وحده يرسل POST إلى `EmployeePayProfiles/Create` ← 403، ولا صف جديد.
  - **AC-IAM-Bb:** المعالج ينشئ تغيير راتب ← «بانتظار الاعتماد»، والمسير يستعمل الراتب القديم. المستخدم نفسه لا يعتمد (403). معتمد مختلف يعتمد ← المسير التالي يستعمل الجديد.

#### G-IAM-03 — هوية الخدمات (S2S) في إعدادات الإنتاج لا تطابق ما يُختبر في القبول
- **الدليل:**
  - القبول يمنح HR.Core النطاقات `file-storage.references payroll.grades.read file-storage.service-upload` مع عنواني Payroll وFile.Storage ([docker-compose.acceptance.yml:60-68](../docker-compose.acceptance.yml)). ويمنح HR.Contracts عنوان HR.Core والنطاقات `hr-core.lookup hr-core.workflow`، مع تعليق: «Without these the CandidateAccepted consumer faulted» ([docker-compose.acceptance.yml:98-105](../docker-compose.acceptance.yml)).
  - في الإنتاج: HR.Core بـ `file-storage.references` فقط ([docker-compose.production.yml:256-259](../docker-compose.production.yml))، وHR.Contracts بالنطاق نفسه وبلا عنوان HR.Core ([docker-compose.production.yml:290-292](../docker-compose.production.yml)). ولا يضبط أي `appsettings.Production.json` قسمي `ServiceIdentity` أو `Integration`. والوثيقة تعترف بذلك ([SERVICE_TRUST_INVENTORY.md:86](SERVICE_TRUST_INVENTORY.md)).
  - الرحلة «الشبيهة بالإنتاج» تُطبَّق فوق ملف القبول فترث هذه القيم ([docker-compose.acceptance-prodlike.yml:1-13](../docker-compose.acceptance-prodlike.yml)). لذلك لا يثبت نجاح `AC-HCM-33/prodlike` ربط الإنتاج.
  - توليد الرموز `/api/internal/codes/generate` يتطلب `HrCore.Write`، أي خدمة بنطاق `hr-core.workflow` ([CodeGenerationController.cs:13-22](../src/Services/HRCore/HR.Core.Web/Controllers/CodeGenerationController.cs)، [HrManagementAuthorizationExtensions.cs:73-77](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). يستهلكه HR.Contracts وIDCard وRecords.Archive وCompliance وIT.Infra وAdmin.Logistics. لكن سكربت Keycloak يربط النطاق باللوجستيات والعقود فقط ([sync-keycloak-dev-realm.ps1:519-537](../scripts/sync-keycloak-dev-realm.ps1))، والإنتاج يطلبه للوجستيات فقط. والفشل يعيد «تعذّر توليد الرمز» ([CodeGenerationClient.cs:62-71](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)).
  - فحص الحجز القانوني قبل نشر الانفصال:
    - يستعمل `AddHttpClient("hr-core-lifecycle")` بلا `ServiceAuthHandler` ([HR.Contracts DependencyInjection.cs:36-45](../src/Services/HRContracts/HR.Contracts.Infrastructure/DependencyInjection.cs)).
    - النقطة تتطلب `Service.HrCoreLookup` ([EmployeeLifecycleInternalController.cs:10-12](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleInternalController.cs)).
    - أي رد غير ناجح أو استثناء يعني «allowing publish» ([EmployeeLifecycleGate.cs:30-51](../src/Services/HRContracts/HR.Contracts.Infrastructure/Services/EmployeeLifecycleGate.cs)).
    - ويُسجَّل `PassThroughEmployeeLifecycleGate` أصلاً، لأن التسجيل يقرأ `Services:HRCore:BaseUrl` أو `HrCore:BaseUrl` ([HR.Contracts DependencyInjection.cs:36-45](../src/Services/HRContracts/HR.Contracts.Infrastructure/DependencyInjection.cs)) لا `Integration:HrCoreBaseUrl`. ولا يضبط أيَّ المفتاحين أيُّ ملف نشر سوى [docker-compose.dev-contracts.yml:18](../docker-compose.dev-contracts.yml)، فالفحص معطّل في الإنتاج وفي القبول أيضاً.
  - Admin.Logistics تبدأ مسار العهدة على `/api/workflow-instances` بلا `GatewayCallerProof.Sign` ([WorkflowTriggerService.cs:50-60](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)). المسار غير مستثنى من الإثبات ([GatewayCallerProofMiddleware.cs:42-50](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs))، والفرض مفعّل خارج التطوير (`:17`). النتيجة 403 ثم `return null`. بينما العملاء الذين يوقّعون هم `HrCoreLookupClient` وعميلا الأنظمة النافذة ورفع المستندات فقط ([HrCoreLookupClient.cs:50](../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreLookupClient.cs)).
  - k8s:
    - المتغيرات `SVC_*_CLIENT_SECRET` لا تُربط بـ `ServiceIdentity__ClientSecret`، ولا يُضبط `ServiceIdentity__Scope` ولا `Integration__*` للخدمات ([hr-app-secrets.yaml:114-160](../deploy/k8s/overlays/production-ha/external-secrets/hr-app-secrets.yaml)، [configmap-app.yaml:7-29](../deploy/k8s/base/configmap-app.yaml)).
    - و`appsettings.json` يضع `"ClientSecret": ""`. فلا يُستعمل متغير البيئة الاحتياطي، لأن `??` لا يعمل مع سلسلة فارغة ([ServiceIdentityClient.cs:33-35](../src/Shared/HRManagement.ServiceDefaults/ServiceIdentityClient.cs)). → OPS.
  - realm JSON في المستودع:
    - ينقصه العملاء `svc-compliance` و`svc-hr-health` و`svc-hr-movements` و`svc-dev-analytics`، وفيه أربعة نطاقات فقط ([realm-hr-services.json:7-139](../docker/keycloak/realm-hr-services.json)).
    - لا تُنشأ النطاقات `dsar.subject.read` و`payroll.grades.read` و`file-storage.service-upload` و`evidence.ingest` إلا بسكربت التطوير ([sync-keycloak-dev-realm.ps1:507-516](../scripts/sync-keycloak-dev-realm.ps1)).
    - يستعمل السكربت سراً واحداً لكل العملاء ([:15](../scripts/sync-keycloak-dev-realm.ps1)).
- **الأثر:** بإعدادات الإنتاج المعلنة:
  - معالج التعيين لا يولّد أرقام المعاملات والقرارات، وهي المشكلة التي أصلحها القبول.
  - العرض لا يتحقق من حدود الراتب، فيصبح كل عرض «استثناء» بثلاثة معتمدين.
  - رفع مستندات المرشح يفشل.
  - أرقام البطاقات والأرشيف والقضايا لا تُولَّد.
  - مسار اعتماد العهدة لا يبدأ.
  - فحص الحجز القانوني قبل الانفصال مفتوح دائماً.
  - كل ذلك بتحذير في السجل فقط.
- **الإصلاح المقترح:**
  - مصفوفة S2S واحدة (عميل × نطاق × جمهور × نقطة) تولّد realm JSON وcompose وk8s، مع اختبار يطابقها بسمات `Service.*` و`HrCoreWrite`.
  - نطاق لكل عميل HTTP بدل `ServiceIdentity:Scope` العام.
  - `ServiceAuthHandler` لعميل الحجز، وجعل الفحص fail-closed.
  - توقيع الإثبات في `WorkflowTriggerService`، أو نقل المسار تحت `/api/internal`.
  - ملف «prodlike» يستعمل S2S الإنتاج نفسه.
- **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md)
- **التحقق المطلوب:**
  - **AC-IAM-Ca:** مكدّس بملف الإنتاج (compose prod + realm من الشيفرة). الرحلة تصل إلى معاملة `Appointment` برقم قرار، والعرض يتحقق من حدود الراتب، ومستند المرشح يُرفع.
  - **AC-IAM-Cb (يغطي AC-30):** عميل بلا `hr-core.workflow` يستدعي `codes/generate` ← 403، ولا رقم مُصدَر في `CodeIssuances`.
  - **AC-IAM-Cc:** موظف تحت حجز قانوني ← نشر الانفصال مرفوض، حتى مع تعطل HR.Core.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-IAM-04 — نطاق الفرع غائب عن قوائم وإجراءات يصلها مدير الفرع، و`BolaArchitectureTests` أحمر وضعيف
- **الدليل:**
  - النطاق مربوط بدور `branch-manager` وحده ([HrOrgScopeVisibility.cs:143-147](../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs)). ومستخدمو الفروع يحملون `hr-core` أيضاً ([personas.json:528-547](../testdata/acceptance/personas.json))، فيصلون إلى كل HR.Core وإلى معالج التعيين، لأن `HrContracts.HireView/HireExecute` تقبلان `hr-core` ([HrManagementAuthorizationExtensions.cs:153-162](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
  - في HR.Core يذكر النطاق 28 ملف متحكم من 72. وهذه نقاط بلا نطاق يصلها مدير الفرع:
    - `DiwanCorrespondences.Index/Create` ([OrgBranchControllers.cs:336-360](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs))؛
    - `HrStatisticReports` ([OrgBranchControllers.cs:14-37](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)) و`FieldInspectionVisits` (`:289-300`)؛
    - `DailyAttendanceRecords.Create` لا يفحص `vm.EmployeeId` ([OrgBranchControllers.cs:126-145](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs))؛
    - `Rosters.AddAssignment(employeeId)` ([RostersController.cs:104-106](../src/Services/HRCore/HR.Core.Web/Controllers/RostersController.cs))؛
    - `SuccessionPlans` ([SuccessionPlansController.cs:22-36](../src/Services/HRCore/HR.Core.Web/Controllers/SuccessionPlansController.cs))؛
    - `api/workflow-instances` يعيد كل النسخ مع `ContextJson` ([WorkflowInstancesController.cs:25-46](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
    - ومتابعات PLAN-25 §11: `PromoteFromCitizen/Internal` و`TalentMarketplace` ([plan25-final-report.md:198-207](../reports/plan25-final-report.md)).
  - في HR.Contracts:
    - `HireWizard.Index` بلا نطاق ([HireWizardController.cs:44](../src/Services/HRContracts/HR.Contracts.Web/Controllers/HireWizardController.cs)).
    - `Step2` و`CreatePendingContract` و`IssueAppointmentDecision` و`Step3SubmitForApproval` و`Step3Execute` تعمل بمعرّف المعاملة دون فحص (`:112, 151-152, 161-162, 218-219, 283-284`).
    - الفحص موجود فقط في `Continue(employeeId)` (`:93-99`).
  - في Admin.Logistics (يصلها br1-mgr وbr2-mgr): الأصول والعهد مقيدة. أما `SupplyOrders` ([AdminExtendedControllers.cs:20-60](../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AdminExtendedControllers.cs)) و`MaintenanceRecords` (`:527`) و`ReceiptRecords` (`:739`) و`InventoryItems` فبلا نطاق.
  - `OrgScopeRequirement` لا تستعمله أي سياسة، والمعالج مسجّل في خدمتين فقط ([HR.Core.Web/Program.cs:267](../src/Services/HRCore/HR.Core.Web/Program.cs)، [HR.Movements.Web/Program.cs:68](../src/Services/HRMovements/HR.Movements.Web/Program.cs)). مع ذلك تذكره IAM-002 دليلاً ([01-P0-RELEASE-BLOCKERS.md:113, 120](completion-backlog/01-P0-RELEASE-BLOCKERS.md)، [IAM_002_ORG_SCOPE.md](IAM_002_ORG_SCOPE.md)).
  - **لماذا `BolaArchitectureTests` أحمر (استنتاج ثابت):**
    - الاختبار يمسح كل `public Task<IActionResult> X(... Guid? employeeId ...)` غير POST، ويطلب علامة حارس خلال 800 حرف ([BolaArchitectureTests.cs:21-23, 52-61](../src/Tests/HRManagement.UnitTests/Architecture/BolaArchitectureTests.cs)).
    - أعاد المسح إنتاج 12 مخالفة كلها `Index` بقوائم بلا نطاق:
      - في HR.Contracts: `SeparationRecords:26`، `ProbationRecords:24`، `CommencementRecords:23`، `ServiceTransactions:33`، `Contracts:28` ([ContractsController.cs:28-49](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ContractsController.cs))، `HireWizard:44`.
      - في Payroll: `PaymentHolds:27`، `EndOfService:39`، `AllowanceRecords`/`DeductionRecords`/`PayrollAdjustments` ([PayrollExtendedControllers.cs:17, 64, 138](../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs))، `FraudAlerts:28`.
    - حتى لو اخضرّ، فهو ضعيف:
      - يتخطى POST.
      - يتجاهل `id` و`transactionId` و`relatedEmployeeId`، فلا يرى `DiwanCorrespondences.Index`.
      - يقبل `GetEmployeeId` علامة حارس.
  - نقل مدير الفرع لا يغيّر مطالبة نطاقه. `TransferExecutedConsumer` في Auth.Directory يحدّث `DirectoryEntry` فقط ([TransferExecutedConsumer.cs:27-46](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/TransferExecutedConsumer.cs)). وAC-06 لم يُشغَّل.
- **الأثر:**
  - مدير فرع ريف دمشق يقرأ ويكتب بيانات فروع أخرى: مراسلات الديوان، المناوبات، خطط التعاقب، أوامر التوريد، ونسخ المسارات بسياقها.
  - ويستطيع تنفيذ خطوات تعيين لفرع آخر بمعرّف المعاملة.
  - والمدير المنقول يحتفظ بنطاقه القديم.
- **الإصلاح المقترح:**
  - مرشح نطاق عام لكل كيان يحمل `OrgUnitId` أو `EmployeeId`، على غرار `RecruitmentOrgScopeFilter`.
  - مصفوفة route/action/resource تُولَّد من الشيفرة.
  - إعادة كتابة الاختبار بالانعكاس ليشمل GET وPOST ويعتمد على الكيان لا على اسم المعامل.
  - ربط `OrgScopeRequirement` أو حذفه.
  - تحديث `scope_org_unit_id` عند نقل المستخدم.
- **الخطة:** [PLAN-52](acceptance-plans/PLAN-52-iam-branch-scope-bola.md)
- **التحقق المطلوب:**
  - **AC-IAM-Da:** br1-mgr يجرب القائمة والتفاصيل والتصدير وPOST لكل متحكم في الجدول: يرى سجلات RIF فقط. سجل ALEP ← 404/403 بلا تغيير في القاعدة.
  - **AC-IAM-Db:** معاملة تعيين لفرع ALEP ← `Step3Execute` من br1-mgr ← 404.
  - **AC-06:** نقل مدير فرع ← يفقد الوصول القديم خلال دورة رمز واحدة.

#### G-IAM-05 — مسار `/ui/ess` وسياسة `Service.HrCoreLookup` يفتحان نقاطاً داخلية لكل مستخدم مسجّل
- **الدليل:**
  - المسار `/ui/ess/{**catch-all}` يذهب إلى HR.Core كله ([Gateway appsettings.json:338-342](../src/Gateway/HRManagement.Gateway/appsettings.json)). البوابة تنزع البادئة ([GatewayAccessTokenTransform.cs:28-41](../src/Shared/HRManagement.ServiceDefaults/GatewayAccessTokenTransform.cs))، وسياسته `Module.Ess` لكل طريقة ([GatewayPathAuthorizationMiddleware.cs:240-258](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - في HR.Core يتخطى فلتر الوحدة كل متحكم سياسته `Service.*`، وقائمة أسماء ([HrModuleAuthorizationFilter.cs:21-48](../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)). و`Service.HrCoreLookup` تقبل «Interactive UI users» ([ServiceAuthorizationExtensions.cs:74-82](../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)).
  - النتيجة (يحتاج تحقق):
    - `GET /ui/ess/api/internal/employees/{id}/lifecycle-flags` يعيد `legalHold` و`legalHoldReason` لأي موظف في الجهة ([EmployeeLifecycleInternalController.cs:15-30](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleInternalController.cs)).
    - ومثله `POST .../service-regime/payroll-blockers|active-regimes` ([ServiceRegimeInternalController.cs:23-46](../src/Services/HRCore/HR.Core.Web/Controllers/ServiceRegimeInternalController.cs)).
    - والمسار `/api/internal/*` مستثنى من إثبات البوابة ([GatewayCallerProofMiddleware.cs:49](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs)).
  - `HrCore.Lookup` تعني أي مستخدم مسجّل ([HrManagementAuthorizationExtensions.cs:120-123](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). فيبحث `hr-employee` و`hr-exam-marker` في دليل موظفي الجهة كله بالاسم والوحدة ([LookupApiController.cs:17-19, 37-57](../src/Services/HRCore/HR.Core.Web/Controllers/LookupApiController.cs)).
- **الأثر:**
  - أي موظف يعرف أن زميله تحت حجز قانوني وسببه، وهو مؤشر على قضية تأديبية أو قانونية.
  - ويعدّد الدليل الوظيفي للجهة.
- **الإصلاح المقترح:**
  - قصر `/ui/ess` على مسارات `Ess*` بقائمة سماح في البوابة.
  - `Service.HrCoreLookup` لحسابات الخدمة فقط.
  - `HrCore.Lookup` لأدوار الموظفين لا لـ ESS والمصحح.
- **الخطة:** [PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md)
- **التحقق المطلوب:**
  - **AC-IAM-Ea:** `employee-a` ← `/ui/ess/api/internal/employees/{زميل}/lifecycle-flags` ← 403. و`/ui/ess/Employees` ← 403. و`/ui/ess/EssPortal` ← 200. و`svc-hr-contracts` بنطاق lookup ← 200.

#### G-IAM-06 — سجل تدقيق البوابة مقروء لكل مستخدم مسجّل وعبر الجهات
- **الدليل:**
  - الفلتر العام للبوابة `Gateway.Dashboard`، أي أي مستخدم مسجّل ([Gateway Program.cs:104-106](../src/Gateway/HRManagement.Gateway/Program.cs)، [HrManagementAuthorizationExtensions.cs:59](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
  - `Home/Audit` و`/ops/audit-search` و`Home/Operations?correlationId=` بلا سياسة أخرى ([HomeController.cs:193-276](../src/Gateway/HRManagement.Gateway/Controllers/HomeController.cs)).
  - الاستعلام بلا مرشح جهة ولا مستخدم ([GatewayAuditQueryService.cs:35](../src/Gateway/HRManagement.Gateway/Services/GatewayAuditQueryService.cs))، مع أن الكيان يحمل `TenantId` ([GatewayAuditEntry.cs:22](../src/Gateway/HRManagement.Gateway/Models/GatewayAuditEntry.cs)). ويعيد `UserName` و`Path` بما فيه المعرّفات ([GatewayAuditQueryService.cs:79-80](../src/Gateway/HRManagement.Gateway/Services/GatewayAuditQueryService.cs)).
- **الأثر:** موظف في جهة B يرى أسماء مستخدمي جهة A وما فتحوه، مثل `/ui/hr-core/Employees/Details/{id}`، ومحاولات 401/403. هذا تسريب عبر الجهات، وكشف لنشاط التحقيق والتدقيق.
- **الإصلاح المقترح:** سياسة `Gateway.Audit` لـ `platform-admin` و`auditor` و`compliance` بجهتهم، مع مرشح جهة إلزامي.
- **الخطة:** [PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md)
- **التحقق المطلوب:**
  - **AC-IAM-Ec:** `employee-a` ← `/Home/Audit` ← 403. و`b-auditor` يرى صفوف جهة B فقط.

#### G-IAM-07 — تجاوزات فصل المهام: علم في الطلب، ومعتمد ثانٍ اسمي، واعتمادات بلا «منشئ ≠ معتمد»
- **الدليل:**
  - نشر السياسات:
    - `PolicyTemplates.PublishFromUi(..., bool forceSameActorForLab = false)` يتجاوز شرط «الناشر ≠ المحرّر» بقيمة يرسلها العميل، في كل البيئات ([PolicyTemplatesController.cs:173-191](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)).
    - ومثله `PublishPolicyRequest.ForceSameActorForLab` ([PoliciesController.cs:112-115, 163](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)).
    - و`PolicyVersionPublisher.PublishDraftAsync` لا يعيد الفحص ([PolicyVersionPublisher.cs:22-61](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyVersionPublisher.cs)).
  - منح الأدوار الحساسة (`hr-admin`، `platform-admin`، `payroll-poster`):
    - «المعتمد الثاني» معرّف يكتبه المانح في النموذج. الخادم يتحقق فقط أنه مستخدم نشط مختلف، ولا يطلب موافقته ([AuthUsersController.cs:93-131](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs)). (وثّقته WF-001 أيضاً.) المنح يُكتب في جدول Auth.Directory المحلي، ولا يصل إلى أدوار Keycloak إلا عبر `TryAssignRealmRolesAsync` حين `Keycloak:DirectorySync:Enabled=true` (`:206-213`)، وهي `false` افتراضياً وفي الإنتاج (G-IAM-11). فالأثر كامن اليوم ويصبح فعلياً عند تفعيل المزامنة.
    - و`ValidatePrivilegedGrant` لا يفحص رتبة المانح، بخلاف `ValidateGrant` ([RoleAssignmentGuard.cs:48-51, 62-79](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/RoleAssignmentGuard.cs)). فحامل `auth-directory` (رتبة 55) يمنح `hr-admin` (رتبة 90).
    - و`Roles.Edit` يغيّر اسم أي دور بلا قيد ([AuthExtendedControllers.cs:34-46](../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthExtendedControllers.cs)).
  - اعتمادات بدور الوحدة وحده، بلا فحص «المنشئ ≠ المعتمد»:
    - `TrainingPlans.Approve` و`TrainingNominations.Approve/Reject` و`MissionFiles.Approve` ([TrainingExtendedControllers.cs:67-80, 271-330, 397-421](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs))؛
    - `DistributionPlans.Approve` ([MovementsExtendedControllers.cs:224-240](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs))؛
    - `DisciplinaryActions.DecideAppeal`: من أنشأ العقوبة يبتّ في استئنافها ([ComplianceExtendedControllers.cs:296-308](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)).
- **الأثر:** *(صُحّح بعد التحقق المستقل: منح الأدوار لا يصل إلى Keycloak ما دامت مزامنة الدليل معطلة في الإنتاج، فالتصعيد كامن حتى تُفعَّل)*
  - يُكسر four-eyes الذي تعلنه IAM-003 في قواعد محرك السياسات (→ POL).
  - يمكن التصعيد إلى مدير النظام بموافقة صورية، متى فُعّلت مزامنة الدليل مع Keycloak (الإصلاح المقترح في G-IAM-11). قبل ذلك يبقى المنح في جدول Auth.Directory ولا يغيّر الرمز.
  - القرار التأديبي واستئنافه بيد شخص واحد.
  - أثر إعادة التسمية في Keycloak يحتاج تحقق، لأن المزامنة معطلة افتراضياً (G-IAM-11).
- **الإصلاح المقترح:**
  - حذف علم المختبر، أو ربطه بـ `IsDevelopment && IsLabScale` في الخادم.
  - منح الدور الحساس طلباً معلّقاً يعتمده المعتمد الثاني بجلسته، مع فحص الرتبة في المسارين.
  - منع إعادة تسمية دور له مستخدمون.
  - سياسة اعتماد مستقلة + SoD لاعتمادات التدريب والتوزيع والتأديب.
- **الخطة:** [PLAN-47](acceptance-plans/PLAN-47-pol-rule-lifecycle.md) (جزئياً: حذف علم `forceSameActorForLab` وأربع عيون حقيقية في نشر السياسات). والباقي — منح الأدوار الحساسة بمعتمد ثانٍ، و«المنشئ ≠ المعتمد» في التدريب والتوزيع والتأديب، وإعادة تسمية الدور: الموجة 2 (IAM-F)
- **التحقق المطلوب:**
  - **AC-IAM-Fa:** نشر سياسة بالمحرّر نفسه مع `forceSameActorForLab=true` في ملف الإنتاج ← 400، والنسخة تبقى مسودة.
  - **AC-IAM-Fb:** `a-directory-maker` يمنح `hr-admin` ← طلب معلّق، لا يسري حتى يعتمده `a-directory-checker` بجلسته.
  - **AC-IAM-Fc:** منشئ إجراء تأديبي يبتّ في استئنافه ← 403.

#### G-IAM-08 — 335 إجراء كتابة في الخدمات بلا سياسة صريحة، والسقف في HR.Core وحده
- **الدليل:**
  - انظر الجدول §2.2. السقف المجمّد واختباره يخصان HR.Core ([hrcore-post-policy-allowlist.txt](../tests/architecture/hrcore-post-policy-allowlist.txt)). وتوصي FUN-005 (G-IDA-14) وHCM-006 وHCM-002 بالشيء نفسه لوحداتها.
  - في 10 خدمات يساوي دور الوحدة صلاحية الكتابة والاعتماد. وفي HR.Contracts تفرض البوابة `HrContractsApprove` على كل كتابة ([GatewayPathAuthorizationMiddleware.cs:222-226](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs))، لكن الخدمة نفسها تقبل `hr-management-approver` عند الوصول المباشر ([HrManagementAuthorizationExtensions.cs:28-31](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
  - في HR.Core يقبل فلتر الوحدة الإجراءات الـ 42 المجمّدة لكل أدوار `Module.HrCore`، ومنها `hr-core-supervisory` و`hr-core-executive` للقراءة ([HrManagementAuthorizationExtensions.cs:13-22](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). البوابة وحدها تمنعهما، وإثباتها قابل للتزوير (G-IAM-13).
  - الاستثناء من فلتر الوحدة بأسماء المتحكمات (`Ess*`، `Policies`، `Onboarding`، `CodeGeneration`…) يسري على كل الخدمات ([HrModuleAuthorizationFilter.cs:28-45](../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)). فأي متحكم جديد بهذا الاسم في أي خدمة يخرج من سياسة وحدته.
- **الأثر:**
  - حامل `idcard` يلغي البطاقات ويعدّل صلاحيات الدخول والأختام الرسمية.
  - حامل `records-archive` يرفع الحجز القانوني ويسجّل الإتلاف.
  - حامل `it-infra` يمنح سجلات صلاحيات الأنظمة ويسحبها.
  - كل ذلك بلا فصل بين قارئ وكاتب ومعتمد.
- **الإصلاح المقترح:**
  - ثلاث سياسات لكل وحدة (Read/Write/Approve) واختبار سقف عام لكل تجميعة Web يبدأ بالقوائم الحالية ولا يكبر.
  - سمة صريحة للاستثناء من فلتر الوحدة بدل الأسماء.
- **الخطة:** [PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md)؛ جزء الرواتب: [PLAN-53](acceptance-plans/PLAN-53-iam-payroll-salary-change.md)
- **التحقق المطلوب:**
  - **AC-IAM-Ed:** اختبار معماري لكل التجميعات: كل POST/PUT/DELETE بسياسة صريحة أو في قائمة السقف. سلبي: مستخدم بدور قراءة يرسل POST مباشرة إلى الخدمة بإثبات بوابة صحيح ← 403.

#### G-IAM-09 — سياسات واسعة: `hr-admin` شامل، والنطاق `hr-core.workflow` كتابة كاملة، و`Onboarding` لكل `hr-core`
- **الدليل:**
  - `hr-admin` يدخل في كل سياسة تقريباً:
    - البيانات السريرية ([HrManagementAuthorizationExtensions.cs:36-38](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))؛
    - سياسات الرواتب الأربع ([HrManagementAuthorizationExtensions.cs:132-147](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))؛
    - كل الملفات ([IFileAccessPolicy.cs:46-47, 81](../src/Shared/HRManagement.ServiceDefaults/Files/IFileAccessPolicy.cs))؛
    - والخدمة الذاتية.
    - ولا تعارض أدوار يمنع جمعه مع غيره.
  - النطاق `hr-core.workflow` يعطي `HrCore.Write` كاملة و`Module.HrCore` و`HrCore.BranchOperation` ([HrManagementAuthorizationExtensions.cs:20-22, 73-77, 96-97](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). عميل اللوجستيات يحمله في الإنتاج، وعميل العقود في القبول، مع أن حاجتهما بدء مسار وتوليد رمز فقط.
  - `OnboardingController` ينشئ جهة ويطبّق هيكلاً وسياسات بسياسة `HrCore.Approve`. فيصله كل `hr-core` في أي جهة، ومنهم مدراء الفروع ([OnboardingController.cs:8-34](../src/Services/HRCore/HR.Core.Web/Controllers/OnboardingController.cs)؛ البوابة [GatewayPathAuthorizationMiddleware.cs:182-183](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
    - `apply-file` يقرأ مساراً حراً من القرص ([AgencyManifestLoader.cs:12-18](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyManifestLoader.cs)).
    - إنشاء صف `Tenant` لا يمر بمعترض الجهة، لأن `Tenant` ليس `BaseEntity` ([AgencyProvisioningService.cs:89-103](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)).
    - أما الكتابة في جهة قائمة أخرى فيرفضها `TenantIdStampInterceptor` (يحتاج تحقق).
  - `Service.EvidenceIngest` تقبل أي حساب خدمة يحمل النطاق، بلا قائمة عملاء ([ServiceAuthorizationExtensions.cs:113-115](../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)).
- **الأثر:**
  - مسؤول نظام واحد يرى التشخيصات الطبية ويرحّل الرواتب ويعكسها.
  - اختراق خدمة اللوجستيات يعني الكتابة في سجل الموظفين.
  - موظف موارد بشرية في جهة ينشئ جهات جديدة.
- **الإصلاح المقترح:**
  - تفكيك `hr-admin` إلى إعدادات وبيانات، و«كسر زجاج» بـ MFA وتدقيق.
  - نطاقات S2S ضيقة (`hr-core.workflow.start`، `hr-core.codes.generate`).
  - `Onboarding` لـ `platform-admin` فقط، وحذف `apply-file` من HTTP.
- **الخطة:** [PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md)
- **التحقق المطلوب:**
  - **AC-IAM-Ee:** `svc-admin-logistics` ← POST `/Employees/Create` ← 403، وبدء المسار ← 200.
  - `central-a` ← `onboarding/agencies/apply` ← 403، و`platform-admin` ← 200 مع تدقيق.

#### G-IAM-10 — الرقم الوطني ظاهر خارج السياسة، واختبار الإسناد الجماعي يغطي 3 من 14 تجميعة (PLAN-07)
- **الدليل:**
  - السياسة `Pii.ViewNationalId` معرّفة ولا يستعملها أي ملف ([HrManagementAuthorizationExtensions.cs:181-186](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
  - التقنيع موجود في قائمة الموظفين وتفاصيلهم ([EmployeeNationalIdPresentation.cs:11-31](../src/Services/HRCore/HR.Core.Web/Services/EmployeeNationalIdPresentation.cs)). لكن الرقم الكامل يظهر في ثلاثة مواضع:
    - نموذج التعديل يملؤه لكل من يحمل `HrCore.Write`، ومنهم مدراء الفروع ([EmployeesController.cs:230-240](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeesController.cs))؛
    - تفاصيل طلب التوظيف تعرضه لكل قراء HR.Core ([RecruitmentApplications/Details.cshtml:52](../src/Services/HRCore/HR.Core.Web/Views/RecruitmentApplications/Details.cshtml))؛
    - طلبات المواطنين ([PublicJobPostings/Details.cshtml:67](../src/Services/HRCore/HR.Core.Web/Views/PublicJobPostings/Details.cshtml)).
  - `MassAssignmentArchitectureTests` بقائمة استثناء فارغة، ويمسح التجميعات المحمّلة فقط ويتخطى الباقي بصمت ([MassAssignmentArchitectureTests.cs:40-59](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs)). ومشروع الاختبار يشير إلى ثلاث تجميعات Web فقط: HR.Contracts وHR.Core وAdmin.Logistics ([HRManagement.UnitTests.csproj:30, 33, 52](../src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj)).
  - المسح الثابت يجد 125 إجراء POST في 10 خدمات تربط كيانات مباشرة: الصحة 20، التدريب 14، IT 14، الرواتب 14، الحركات 12، IDCard 12، الأرشيف 12، التحليلات 12، Compliance 11، الدليل 4. أمثلة:
    - `IssuedCards.Create(IssuedCard)`؛
    - `MedicalRecords.Edit(MedicalRecord)`؛
    - `PayrollConfigurations.Edit(PayrollConfiguration)`؛
    - `Roles.Create/Edit(Role)`.
  - AC-26 وAC-31 لم يُشغَّلا.
- **الأثر:**
  - مدير الفرع يرى الرقم الوطني الكامل مع أن السياسة صُمّمت لمنعه.
  - الحقول التي يملكها الخادم (الحالة، المعتمد، المبالغ) قابلة للتعيين من العميل في 10 خدمات. والأثر لكل كيان يحتاج تحقق.
- **الإصلاح المقترح:**
  - `Pii.ViewNationalId` في كل عرض ونموذج: حقل مقنّع، وتغيير الرقم بإجراء منفصل مدقَّق.
  - View Models للكيانات الحساسة أولاً.
  - اختبار يحمّل كل التجميعات ويفشل إن تعذّر التحميل.
- **التحقق المطلوب:**
  - **AC-26:** مدير الفرع يفتح تفاصيل الموظف ونموذج تعديله وطلب التوظيف ← الرقم مقنّع في الثلاثة.
  - **AC-31:** POST إلى `IssuedCards/Create` بحقل `Status=Delivered` ← الحالة `Requested`.

#### G-IAM-11 — دورة هوية المستخدم والجلسة: المغادر يبقى، والمنقول يحتفظ بنطاقه، والجلسة تمتد
- **الدليل:**
  - `DirectorySync:Enabled=false` افتراضياً ([Auth.Directory.Web/appsettings.json:23-24](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json))، ولا يفعّلها الإنتاج. فيعيد `TryGetSyncConfig` القيمة false ([KeycloakUserProvisioningService.cs:290](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Keycloak/KeycloakUserProvisioningService.cs)).
  - مستهلك الانفصال يعطّل المستخدم محلياً، ويسكت حين `Attempted=false` ([SeparationCompletedConsumer.cs:93-106](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/SeparationCompletedConsumer.cs)). وكذلك منح الأدوار من الدليل لا يصل إلى Keycloak.
  - النقل لا يحدّث النطاق (G-IAM-04).
  - `employee_id` هو أساس ملكية الخدمة الذاتية، لكنه غير معرَّف خاصيةً مُدارة للمسؤول، بخلاف `tenant_id` و`scope_org_unit_id` ([sync-keycloak-dev-realm.ps1:381-405](../scripts/sync-keycloak-dev-realm.ps1)). إن سمحت سياسة الخصائص غير المُدارة بالتعديل الذاتي، يغيّر الموظف `employee_id` فيرى قسائم زميله (يحتاج تحقق).
  - الجلسة:
    - كعكة البوابة 480 دقيقة منزلقة بلا حد مطلق ([KeycloakExtensions.cs:147-150](../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs)، [Gateway appsettings.Production.json:17-21](../src/Gateway/HRManagement.Gateway/appsettings.Production.json)).
    - البوابة تطلب `offline_access` ([KeycloakExtensions.cs:171](../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs))، فيعيش رمز التحديث بعد جلسة SSO. القائمة نفسها تذكر 30 دقيقة خمول للجلسة و30 يوماً للرمز غير المتصل ([realm-rebuild-checklist.md:95-97](iam/realm-rebuild-checklist.md)).
    - التحديث لا يعيد بناء أدوار المبدأ في الكعكة ([GatewayAccessTokenRefreshMiddleware.cs:104-135](../src/Shared/HRManagement.ServiceDefaults/GatewayAccessTokenRefreshMiddleware.cs))، فتقرر البوابة بأدوار لحظة الدخول.
- **الأثر:**
  - موظف منتهية خدمته يدخل الخدمة الذاتية حتى يُعطَّل يدوياً.
  - مدير منقول يرى فرعه القديم.
  - جلسة مسروقة تبقى 8 ساعات وتتمدد.
- **الإصلاح المقترح:**
  - تفعيل المزامنة مع فشل ظاهر (عدّاد وتنبيه).
  - مستهلكات انضمام/نقل/مغادرة تحدّث الخصائص والأدوار.
  - `employee_id` خاصية مُدارة للمسؤول فقط.
  - خمول 30 دقيقة وحد مطلق 10 ساعات، بلا `offline_access`.
  - إعادة بناء المبدأ عند كل تحديث.
- **الخطة:** [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md) (المغادر)؛ [PLAN-52](acceptance-plans/PLAN-52-iam-branch-scope-bola.md) (المنقول يفقد نطاقه). الجلسة وMFA والمزامنة الكاملة: الموجة 2 (IAM-G)
- **التحقق المطلوب:**
  - **AC-IAM-Ga:** انفصال موظف ← حسابه معطّل في Keycloak خلال ≤ 5 دقائق، والخدمة الذاتية تعيده إلى الدخول.
  - **AC-IAM-Gb:** تعديل `employee_id` من حساب المستخدم ← مرفوض.
  - **AC-IAM-Gc:** خمول 31 دقيقة ← إعادة دخول.

#### G-IAM-12 — Keycloak غير مُقنَّن للإنتاج، وMFA غير مفروضة
- **الدليل:**
  - قالب realm فيه 22 دوراً، وينقصه `payroll-poster` و`payroll-reverser` و`hr-health-clinical` و`hr-core-supervisory` و`hr-core-executive` و`hr-exam-marker` ([realm-hrmanagement-dev.template.json:11](../docker/keycloak/realm-hrmanagement-dev.template.json)). وعميل البوابة فيه `directAccessGrantsEnabled: true` (`:415`).
  - لا realm إنتاج في المستودع. ونشر Keycloak في k8s يعمل بـ `args: ["start"]` بلا استيراد ([keycloak/deployment.yaml:60-61](../deploy/k8s/data/base/keycloak/deployment.yaml)).
  - قائمة إعادة البناء تصف realm آخر: عملاء `hr-core-service`، وأدوار `hr-officer` و`payroll-admin` و`mss-user` غير موجودة في الشيفرة ([realm-rebuild-checklist.md:57-86](iam/realm-rebuild-checklist.md)). مع ذلك تقول IAM-001 «Done» إن «إعادة إنشاء realm من الصفر تنتج الأدوار والعملاء والسياسات نفسها» ([01-P0-RELEASE-BLOCKERS.md:97](completion-backlog/01-P0-RELEASE-BLOCKERS.md)).
  - MFA:
    - `EXT_001_MFA_POLICY.md` يطلبها لـ `payroll-operator` و`security-admin` و`break-glass-operator`، وهذه أدوار غير موجودة.
    - التقييم يفحص مستند JSON فقط ([ExtFederationEvaluator.cs:8-9, 184-203](../src/Shared/HRManagement.ServiceDefaults/ExtFederation/ExtFederationEvaluator.cs)).
    - لا فحص `acr`/`amr` ولا step-up في أي مسار.
    - لا سياسة كلمات مرور، ولا حماية من التخمين، ولا أعمار رموز في أي ملف realm.
- **الأثر:**
  - لا يمكن بناء Keycloak الإنتاج من المستودع.
  - ما يُبنى من القالب لا يعرف المرحِّل ولا السريري، فيبقى الترحيل والعكس بيد `hr-admin`.
  - الترحيل ومنح الأدوار بكلمة مرور فقط.
- **الإصلاح المقترح:**
  - realm-as-code (keycloak-config-cli أو Terraform) يُولَّد من `HrManagementRoles` و`ServiceClientIds` و`ServiceScopes`، مع اختبار تطابق.
  - WebAuthn/OTP للأدوار الحساسة، وفحص `acr` في البوابة على مساراتها.
- **الخطة:** [PLAN-39](acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md) (جزئياً: realm إنتاجي كشيفرة بالأدوار والعملاء، وسياسة كلمات المرور والحماية من التخمين). وMFA وstep-up و`acr`: الموجة 2 (IAM-G)
- **التحقق المطلوب:**
  - **AC-IAM-Gd:** realm يُبنى من الصفر، واختبار الجرد يجد كل دور وعميل ونطاق في الشيفرة.
  - **AC-IAM-Ge:** `a-payroll-poster` بلا عامل ثانٍ يحاول الترحيل ← طلب step-up أو 403.

#### G-IAM-13 — الثقة بين الخدمات: سر واحد لكل الحاويات، وإثبات بوابة قابل للتزوير، ونطاق قراءة يجيز المحو
- **الدليل:**
  - في k8s يُحقن سر واحد `hr-app-secrets` في كل الحاويات ([payroll-web/deployment.yaml:48-54](../deploy/k8s/base/payroll-web/deployment.yaml)). يحوي:
    - أسرار 12 عميل خدمة؛
    - سر عميل البوابة؛
    - سر `directory-sync`، وهو عميل إدارة Keycloak؛
    - سر إثبات البوابة؛
    - سلاسل اتصال 15 قاعدة ([hr-app-secrets.yaml:18-163](../deploy/k8s/overlays/production-ha/external-secrets/hr-app-secrets.yaml)). → OPS.
  - إثبات البوابة:
    - HMAC بسر واحد مشترك بين البوابة و14 خدمة، يغطي الوقت والطريقة والمسار فقط، ويقبل الإعادة خلال 5 دقائق ([GatewayCallerProof.cs:18, 38-42](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProof.cs)).
    - القيمة في `appsettings.Production.json` هي «USE_ENV_OR_SECRET_MANAGER» ([HR.Core appsettings.Production.json:251-254](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)). compose الإنتاج لا يضبطها، ولا شيء يرفضها عند الإقلاع.
    - File.Storage بلا `appsettings.Production.json`.
  - كل وحدة تقبل جمهور `hr-gateway-client` ([KeycloakExtensions.cs:232-239](../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs)). فرمز أي مستخدم يصلح لكل خدمة مباشرة، والبوابة هي طبقة التفويض الدقيقة الوحيدة لـ 335 إجراءً (G-IAM-08).
  - `dsar.subject.read` يجيز `POST .../candidates/{id}/anonymize` ([DsarInternalController.cs:19-21, 368-369](../src/Services/HRCore/HR.Core.Web/Controllers/DsarInternalController.cs)).
- **الأثر:**
  - اختراق حاوية واحدة يعطي هوية كل خدمة وإدارة Keycloak وكل القواعد.
  - معيار IAM-004 («لا shared superuser؛ خدمة غير مخولة لا تستدعي API أو table») غير متحقق في k8s ([02-P1-PLATFORM-INTEGRITY.md:64](completion-backlog/02-P1-PLATFORM-INTEGRITY.md)).
- **الإصلاح المقترح:**
  - سر لكل خدمة (ExternalSecret لكل Deployment).
  - إثبات مرتبط بالرمز أو mTLS بين البوابة والوحدات.
  - رفض القيم المؤقتة عند الإقلاع.
  - نطاق `dsar.subject.erase` مستقل.
- **الخطة:** [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md)
- **التحقق المطلوب:**
  - **AC-IAM-Cd:** حاوية Payroll لا ترى `SVC_HR_CORE_CLIENT_SECRET`. الإقلاع بسر «USE_ENV_OR_SECRET_MANAGER» يفشل.
  - **AC-IAM-Ce:** عميل بـ `dsar.subject.read` وحده ← anonymize ← 403.

### 4.3 قدرات ناقصة (P2)

#### G-IAM-14 — نموذج الأدوار لا يغطي واجبات جهة حكومية
- **الدليل:**
  - التقييد بالوحدة مرتبط بـ `branch-manager` وحده ([HrOrgScopeVisibility.cs:143-147](../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs)). فلا يمكن تقييد موظف رواتب أو عقود أو صحة في محافظة إلا بمنحه دور مدير فرع، وهذا الدور يجلب `HrCore.BranchOperation`.
  - لا اعتماد للمدير المباشر:
    - اعتماد الإجازة والسلفة `HrContracts.Approve`، أي `hr-contracts` فقط ([HrManagementAuthorizationExtensions.cs:126-128](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)، [LeaveRequestsController.cs:145](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveRequestsController.cs)).
    - دور MSS موجود في القائمة فقط.
  - `auditor` (3 شخصيات) لا تذكره أي سياسة ([HrManagementRoles.cs:50](../src/Shared/HRManagement.ServiceDefaults/HrManagementRoles.cs)). و`Evidence.Read` لا يشمله ([HrManagementAuthorizationExtensions.cs:176-179](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
  - أدوار خطوات غير موجودة أو لا تصل ([WorkflowPackSeeder.cs:26-49](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)):
    - `security` (ID-CARD-ISSUANCE) و`it-support` (IT-TICKET) غير موجودين؛
    - `compliance` لـ «مراجعة قانونية» في SEPARATION-WIZARD ولـ DISCIPLINARY-APPROVAL، و`admin-logistics` لـ ASSET-LOSS: نقطة الاعتماد العامة `HrCore.BranchOperation` لا تقبلهما ([HrManagementAuthorizationExtensions.cs:91-97](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- **الأثر:**
  - الجهة إما تمنح موظفي المحافظات صلاحية على كامل الجهة، أو تحوّلهم إلى «مدراء فروع».
  - المدير المباشر خارج دورة الإجازة.
  - المدقق المستقل لا يرى شيئاً.
- **الإصلاح المقترح:**
  - النطاق خاصية مستقلة: كل دور يحمل `scope_org_unit_id` يُقيَّد.
  - دور MSS للمدير المباشر بنطاق مرؤوسيه.
  - `auditor` للقراءة المقنّعة عبر الوحدات وللأدلة.
  - مواءمة أدوار الخطوات مع الأدوار الموجودة (→ WF للتشغيل).
- **الخطة:** [PLAN-45](acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md) و[PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) (جزئياً: أدوار خطوات العهدة والبطاقة والتأديب تصل إلى نقطة الاعتماد، وسحب التعريفات بأدوار غير موجودة)، و[PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md) و[PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md) (جزئياً: `auditor` يقرأ تدقيق البوابة ويراجع الرواتب). والنطاق خاصيةً مستقلة وMSS: الموجة 2 (IAM-H)
- **التحقق المطلوب:**
  - **AC-IAM-Ha:** موظف رواتب مقيّد بـ RIF يرى ملفات أجر RIF فقط.
  - **AC-IAM-Hb:** المدير المباشر يعتمد إجازة مرؤوسه، ويُرفض لغيره.
  - **AC-IAM-Hc:** `auditor` يقرأ ولا يكتب (POST ← 403).

#### G-IAM-15 — الخدمة الذاتية ناقصة ومكسورة جزئياً
- **الدليل:**
  - قسيمة الراتب:
    - `Ess/Payslip` يعيد التوجيه إلى `PayrollReports/Payslip` ([Payroll EssController.cs:48-63](../src/Services/Payroll/Payroll.Web/Controllers/EssController.cs)). ذلك المتحكم خاضع لـ `Module.Payroll`، ولا استثناء له في الفلتر ولا في البوابة. النتيجة 403 للموظف.
    - وفي المقابل يمنع الإجراء نفسه موظفي الرواتب الذين يحملون `employee_id` من قسائم غيرهم ([PayrollReportsController.cs:67-69](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)). وكل الشخصيات تحمل `employee_id`.
    - طلب GET يعلّم القسيمة «مرسلة» ([PayrollReportsController.cs:81](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)).
  - قائمة القسائم بلا فرز بحالة التشغيل ([EssController.cs:37-43](../src/Services/Payroll/Payroll.Web/Controllers/EssController.cs))، فقد تظهر المسودات (يحتاج تحقق).
  - التعلّم الذاتي: `EssLearningController` بسياسة `Module.Ess` ([EssLearningController.cs:16-18](../src/Services/HRTraining/HR.Training.Web/Controllers/EssLearningController.cs))، لكن قاعدة ESS في البوابة لا تشمل `/ui/hr-training` ([GatewayPathAuthorizationMiddleware.cs:247-252](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). النتيجة 403.
  - طلب الإجازة حدث فقط، ولا شاشة لحالته أو للرصيد ([EssPortalController.cs:85-123](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)).
  - طلب البطاقة ينشئ `EmployeeDocument` من نوع «أخرى» ولا يصل إلى IDCard، ويقبل `AttachmentFileId` أي معرّف ([EssPortalController.cs:173-187](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)).
- **الأثر:** الموظف لا ينزّل قسيمته، ولا يتعلم ذاتياً، ولا يعرف مصير إجازته، ولا يُعالَج طلب بطاقته.
- **الإصلاح المقترح:**
  - نقطة قسيمة PDF داخل `EssController` بالملكية.
  - إضافة `/ui/hr-training` إلى قاعدة ESS.
  - شاشات «طلباتي» من HR.Contracts عبر S2S.
  - طلب بطاقة يصل إلى IDCard، مع فحص ملكية الملف.
- **التحقق المطلوب:**
  - **AC-IAM-He:** `employee-a` ينزّل قسيمة معتمدة له (200)، وقسيمة غيره ← 404، ومسودة ← لا تظهر.
  - **AC-IAM-Hf:** يسجّل في دورة إلكترونية عبر البوابة (200).

### 4.4 تحسينات (P3)

#### G-IAM-16 — تحصينات صغيرة
- **الدليل:**
  - بادئة `/health` المستثناة من الإثبات تطابق `/HealthStatusEntries` و`/HealthRetirementRecords` عند الاستدعاء المباشر لـ HR.Health ([GatewayCallerProofMiddleware.cs:45](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs)، [HealthExtendedControllers.cs:208, 260](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
  - سجل تصحيح متروك `[DEBUG-8b0e6a]` ([GatewayAccessTokenRefreshMiddleware.cs:44-49](../src/Shared/HRManagement.ServiceDefaults/GatewayAccessTokenRefreshMiddleware.cs)).
  - تمييز حساب الخدمة ببادئة `preferred_username` ([ServiceAuthorizationExtensions.cs:10-24](../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)).
- **الإصلاح المقترح:** مطابقة المقطع كاملاً (`/health` أو `/health/...`)؛ حذف السجل؛ تمييز حساب الخدمة بـ `azp` وقائمة العملاء فقط.
- **التحقق المطلوب:** اختبار وحدة لكل بند.

### 4.5 فجوات التحقق والتوثيق (P1)

#### G-IAM-17 — ادعاءات «Done» وأدلة «Pass» لا تثبت ما تقوله
- **الدليل:**
  - أدلة AC-02 وAC-03 وAC-04 طلب HTTP واحد لكل منها على `/api/hr-core/api/lookup/employees`، عند `gitSha 9cb3705` بتاريخ 2026-08-24 ([AC-02/metadata.json](../acceptance-evidence/AC-02/metadata.json)، [AC-03/http](../acceptance-evidence/AC-03/http)، [AC-04/http](../acceptance-evidence/AC-04/http)). وينقصها `steps.md` و`before/after/audit` ([summary.md](../acceptance-evidence/summary.md)). فلا تثبت شيئاً عن القوائم والتفاصيل والتصدير والإجراءات.
  - AC-06 وAC-26..29 وAC-31 لم تُشغَّل، وAC-30 بلا نتيجة.
  - IAM-002 «Done» بمعيار «كل endpoint حساس في مصفوفة route/action/resource policy»، ودليله `OrgScopeRequirement` غير المستعمل ([01-P0-RELEASE-BLOCKERS.md:113-120](completion-backlog/01-P0-RELEASE-BLOCKERS.md)).
  - IAM-001 «Done» بإعادة بناء realm مطابقة (G-IAM-12).
  - IAM-003 يذكر «delegation مؤقت مؤرخ» و«four-eyes لتعديل الأدوار» ([02-P1-PLATFORM-INTEGRITY.md:50](completion-backlog/02-P1-PLATFORM-INTEGRITY.md))، وكلاهما غير فعّال (G-IAM-01، G-IAM-07).
  - IAM-004 يستشهد بـ `realm-hr-services.json` الناقص ([02-P1-PLATFORM-INTEGRITY.md:69](completion-backlog/02-P1-PLATFORM-INTEGRITY.md)).
  - جرد الثقة يقول إن نطاق HR.Contracts `file-storage.references` وحده، وإن نطاق Compliance «—» ([SERVICE_TRUST_INVENTORY.md:11, 21](SERVICE_TRUST_INVENTORY.md)). الشيفرة تستعمل أكثر من ذلك.
  - الاختباران المعماريان: أحدهما أحمر (BOLA)، والآخر يمرّ دون فحص لـ 11 تجميعة (Mass Assignment). → QA.
- **الإصلاح المقترح:**
  - تشغيل AC-01..06 وAC-26..31 حيّاً على SHA الحالي، بكل عناصر الدليل الثمانية.
  - تصحيح البطاقات IAM-001..004 وTEN وSEC-003، و`HIRE_RBAC_MATRIX` وجرد الثقة وقائمة realm، لتطابق الشيفرة.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)
- **التحقق المطلوب:** مجلد دليل لكل AC بخريطة `scenarios`، وأي «Done» يشير إلى دليل حي لا إلى اختبار وحدة.

### 4.6 تقاطعات مع الدراسات الأخرى

- **→ WF:** نقطة الاعتماد العامة `api/workflow-instances/{id}/approve` بسياسة `HrCore.BranchOperation` لكل أنواع الكيانات، ودور الخطوة مفروض على 7 أنواع فقط ([WorkflowInstancesController.cs:103-121](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs)).
- **→ WF:** تعريفات مسارات بأدوار `security` و`it-support` غير الموجودة، وخطوات `compliance` و`admin-logistics` لا يصلها أصحابها ([WorkflowPackSeeder.cs:26-49](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).
- **→ WF:** بدء مسار العهدة من اللوجستيات يفشل بلا إثبات البوابة، ويعيد `null` ([WorkflowTriggerService.cs:50-60](../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/WorkflowTriggerService.cs)).
- **→ POL:** `PolicyEnforceClient` يرجع إلى بديل ثابت `Allowed=true` عند 403، وأغلب الخدمات لا تملك `hr-core.workflow` ([PolicyEnforceClient.cs:44-50, 85-110](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)).
- **→ POL:** تجاوز four-eyes في نشر السياسات بعلم `ForceSameActorForLab` (يُصلح في IAM-F).
- **→ OPS:** k8s لا يربط أسرار الخدمات بـ `ServiceIdentity__ClientSecret` ولا يضبط `Integration__*` ([hr-app-secrets.yaml:114-160](../deploy/k8s/overlays/production-ha/external-secrets/hr-app-secrets.yaml)). وقيم «USE_ENV_OR_SECRET_MANAGER» لا تُرفض عند الإقلاع ([HR.Core appsettings.Production.json:251-254](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)).
- **→ OPS:** سلاسل اتصال كل القواعد في سر واحد لكل الحاويات ([hr-app-secrets.yaml:38-94](../deploy/k8s/overlays/production-ha/external-secrets/hr-app-secrets.yaml)).
- **→ QA:** `BolaArchitectureTests` أحمر بـ 12 مخالفة ([BolaArchitectureTests.cs:37-71](../src/Tests/HRManagement.UnitTests/Architecture/BolaArchitectureTests.cs)). و`MassAssignmentArchitectureTests` يمرّ دون فحص لـ 11 تجميعة ([HRManagement.UnitTests.csproj:30, 33, 52](../src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj)).
- **→ QA:** أدلة AC-02..05 من SHA قديم، وAC-02..04 طلب واحد لكل منها ([AC-02/metadata.json](../acceptance-evidence/AC-02/metadata.json)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-IAM-1 | من يعتمد النقل والترفيع والندب والإعارة؟ | `hr-management-approver` باعتماد فقط (لا إنشاء ولا تنفيذ)، والتنفيذ لـ `hr-movements` | IAM-A |
| D-IAM-2 | هل تغيير الراتب الأساسي والسلّم والدرجات معاملة باعتماد؟ | نعم: صانع `payroll-processor`، ومعتمد `payroll-approver` مختلف، وسريان بعد الاعتماد، ومرجع قانوني للسلّم | IAM-B |
| D-IAM-3 | نموذج النطاق في الوحدات الأخرى | النطاق خاصية مستقلة تقيّد أي دور يحملها، لا دور `branch-manager` وحده | IAM-D، IAM-H |
| D-IAM-4 | صلاحيات `hr-admin` | فصل مسؤول الإعدادات عن البيانات؛ إخراج السريري والترحيل والعكس منه؛ «كسر زجاج» بـ MFA وتدقيق | IAM-E، IAM-G |
| D-IAM-5 | سياسة الجلسة وMFA | خمول 30 دقيقة، وحد مطلق 10 ساعات، بلا `offline_access`؛ WebAuthn أو OTP لـ `hr-admin` و`platform-admin` و`auth-directory` و`payroll-approver/poster/reverser` | IAM-G |
| D-IAM-6 | اعتماد المدير المباشر (MSS) في v1؟ | نعم للإجازة والسلفة، بنطاق المرؤوسين | IAM-H |
| D-IAM-7 | من يقرأ سجل تدقيق البوابة؟ | `platform-admin` لكل الجهات؛ `auditor` و`compliance` لجهتهم فقط | IAM-E |
| D-IAM-8 | ما حدود دور `auditor`؟ | قراءة مقنّعة عبر الوحدات + الأدلة، بلا كتابة | IAM-H |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   IAM-A (وصول المعتمد والتفويض)  ║  IAM-B (صلاحيات الرواتب)  ║  IAM-C (ربط S2S للإنتاج)
الموجة 2 (P1)   IAM-D (نطاق الفرع وBOLA)  ║  IAM-E (سياسة لكل إجراء وتضييق السياسات)  ║  IAM-F (فصل المهام والبيانات الشخصية)
الموجة 3 (P1)   IAM-G (Keycloak كشيفرة ودورة الهوية والجلسة وMFA)
الموجة 4 (P2)   IAM-H (نموذج أدوار حكومي والخدمة الذاتية)
مستمر           IAM-I (التحقق الحي وتصحيح الوثائق والتحصينات)
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة البرنامج |
|-------|---------|---------|----------|-----------|-------------------|--------------|
| IAM-A | وصول أدوار الاعتماد والتفويض الفعلي | G-IAM-01 | P0 | — | 6–8 | [PLAN-50](acceptance-plans/PLAN-50-iam-approver-reach-delegation.md) |
| IAM-B | صلاحيات الرواتب ومعاملة تغيير الراتب | G-IAM-02 (+ الرواتب من G-IAM-08) | P0 | — | 5–7 | [PLAN-53](acceptance-plans/PLAN-53-iam-payroll-salary-change.md) |
| IAM-C | ربط هوية الخدمات للإنتاج وتقوية الثقة | G-IAM-03، G-IAM-13 | P0 | — | 7–9 | [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (G-IAM-03)، [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md) (G-IAM-13) |
| IAM-D | نطاق الفرع وBOLA في كل الخدمات | G-IAM-04 | P1 | IAM-C (لقطات الوحدات) | 8–10 | [PLAN-52](acceptance-plans/PLAN-52-iam-branch-scope-bola.md) |
| IAM-E | سياسة صريحة لكل إجراء وتضييق السياسات الواسعة | G-IAM-05، G-IAM-06، G-IAM-08، G-IAM-09 | P1 | IAM-A | 8–10 | [PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md) |
| IAM-F | فصل المهام والبيانات الشخصية | G-IAM-07، G-IAM-10 | P1 | IAM-B | 7–9 | «الموجة 2» |
| IAM-G | Keycloak كشيفرة ودورة الهوية والجلسة وMFA | G-IAM-11، G-IAM-12 | P1 | IAM-C | 8–10 | «الموجة 2»؛ المغادر في G-IAM-11: [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md)، والمنقول: PLAN-52 |
| IAM-H | نموذج أدوار حكومي والخدمة الذاتية | G-IAM-14، G-IAM-15 | P2 | IAM-A، IAM-D، IAM-G | 8–10 | «الموجة 2» |
| IAM-I | التحقق الحي وتصحيح الوثائق والتحصينات | G-IAM-16، G-IAM-17 | P1 ← مستمر | كل الخطط | 5–6 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) (G-IAM-17)؛ G-IAM-16: «الموجة 2» |
| **المجموع** | | | | | **≈ 62–79** |  |

**IAM-A — وصول أدوار الاعتماد والتفويض.**
- سياسة «اعتماد فقط» للحركات تعبر فلتر الوحدة، وقرار D-IAM-1.
- استثناءات بوابة لإجراءات `HrCore.Approve` و`ManageDelegations`.
- التفويض مربوط بـ `WorkflowStageGuard` وSoD، أو حذفه.
- مصفوفة role × endpoint مولّدة من الشيفرة واختبارها.
- القبول: AC-IAM-Aa، AC-IAM-Ab، وتشغيل AC-09 وAC-14.

**IAM-B — صلاحيات الرواتب.**
- `Payroll.MasterData` على 16 إجراءً.
- معاملة تغيير الراتب بحالات واعتماد مختلف.
- View Models بدل الكيانات في الرواتب.
- سقف معماري للرواتب.
- القبول: AC-IAM-Ba، AC-IAM-Bb.

**IAM-C — ربط S2S للإنتاج.**
- مصفوفة S2S مصدراً واحداً لـ realm وcompose وk8s.
- نطاق لكل عميل HTTP.
- `ServiceAuthHandler` وfail-closed لفحص الحجز القانوني.
- توقيع `WorkflowTriggerService`.
- سر لكل خدمة، ورفض القيم المؤقتة، ونطاق `dsar.subject.erase`.
- ملف «prodlike» على S2S الإنتاج.
- القبول: AC-IAM-Ca..Ce، وAC-27 وAC-30.

**IAM-D — نطاق الفرع.**
- مرشح نطاق عام للكيانات.
- إغلاق القوائم والإجراءات في §2.3.
- اختبار BOLA بالانعكاس يشمل GET/POST.
- تحديث النطاق عند النقل.
- القبول: AC-IAM-Da، AC-IAM-Db، وإعادة AC-01..06 بأدلة كاملة.

**IAM-E — سياسات صريحة وتضييق.**
- Read/Write/Approve لكل وحدة، وسقف عام لكل تجميعة.
- قصر `/ui/ess`، وإخراج المستخدم التفاعلي من `Service.HrCoreLookup`.
- سياسة تدقيق البوابة ومرشح الجهة.
- نطاقات `hr-core.*` ضيقة، و`Onboarding` لـ `platform-admin`.
- تفكيك `hr-admin` (D-IAM-4).
- القبول: AC-IAM-Ea..Ee.

**IAM-F — فصل المهام والبيانات الشخصية.**
- حذف `forceSameActorForLab`.
- منح الأدوار الحساسة بطلب يعتمده الثاني.
- SoD لاعتمادات التدريب والتوزيع والتأديب.
- `Pii.ViewNationalId` في كل عرض ونموذج.
- توسيع اختبار Mass Assignment وView Models للكيانات الحساسة.
- القبول: AC-IAM-Fa..Fc، وAC-26 وAC-31.

**IAM-G — Keycloak ودورة الهوية.**
- realm-as-code للإنتاج: الأدوار، والعملاء، والنطاقات، و`employee_id` مُدارة، والحماية من التخمين، والأعمار.
- MFA وفحص `acr` (D-IAM-5).
- تفعيل المزامنة بفشل ظاهر، ومستهلكات انضمام/نقل/مغادرة.
- سياسة الجلسة.
- القبول: AC-IAM-Ga..Ge.

**IAM-H — نموذج أدوار حكومي والخدمة الذاتية.**
- النطاق خاصية مستقلة (D-IAM-3).
- MSS (D-IAM-6).
- `auditor` (D-IAM-8).
- مواءمة أدوار الخطوات.
- قسيمة PDF، والتعلّم الذاتي، و«طلباتي»، وطلب البطاقة إلى IDCard.
- القبول: AC-IAM-Ha..Hf.

**IAM-I — التحقق والتوثيق والتحصينات.**
- إعادة AC-01..06 وAC-26..31 حيّاً على SHA الحالي.
- تصحيح IAM-001..004 وTEN-* وSEC-003، و`HIRE_RBAC_MATRIX` و`SERVICE_TRUST_INVENTORY` وقائمة realm.
- بنود G-IAM-16.
- القبول: مجلد أدلة كامل لكل AC، وكل «Done» يشير إلى دليل حي.

**الربط بخطط البرنامج (2026-10-30):** عمود «خطة البرنامج» يربط كل خطة مقترحة هنا بخطة الموجة 1 المنفذة لها. تسميات «الموجة 1..4» في المخطط أعلاه ترتيب داخلي لهذه الدراسة، وليست موجات البرنامج. ما وُسم «الموجة 2» يبقى برمزه (IAM-F، IAM-G، IAM-H، وG-IAM-16 من IAM-I) خارج الموجة 1. وبعض البنود انتقلت بين الحروف: جزء الرواتب من G-IAM-08 إلى PLAN-53، ونقل المستخدم المقيّد (من G-IAM-11) إلى PLAN-52، والمغادر إلى PLAN-62.

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: عدد المتحكمات التي تحتاج View Models في IAM-F، وقرار D-IAM-3 الذي قد يوسّع IAM-D وIAM-H.

---

## 7. معيار «اكتمال الصلاحيات والنطاق» المقترح

- [ ] **1. كل دور مكلّف بخطوة أو واجب يصل إليها، ولا يصل إلى غيرها.** مصفوفة role × endpoint تُولَّد من الشيفرة (البوابة + الفلتر + السمات) وتُختبر في CI، ومنها AC-09 وAC-14.
- [ ] **2. كل إجراء كتابة في كل الخدمات بسياسة صريحة، ونطاق الفرع مثبت باختبار يشمل GET وPOST.** قوائم السقف لا تكبر، و`BolaArchitectureTests` الجديد أخضر بلا استثناءات غير مبررة.
- [ ] **3. رحلة شبيهة بالإنتاج تستعمل S2S الإنتاج نفسه (realm من الشيفرة + نطاقات compose/k8s) تنجح.** تشمل التعيين بالترميز، والعرض بحدود الراتب، وDSAR. والهوية أو النطاق الخاطئ يُرفض بلا أثر (AC-27..30).
- [ ] **4. AC-01..06 وAC-26 وAC-31 ناجحة حيّاً على SHA الحالي بأدلة كاملة.** تشمل لكل خدمة يصلها مستخدم مقيّد: قائمة وتفاصيل وتصديراً وإجراءً، بموجب وسالب.
- [ ] **5. Keycloak الإنتاج يُبنى من المستودع، وMFA مفروضة على الأدوار الحساسة.** والمغادر يُعطَّل حسابه خلال مدة محددة، والجلسة تنتهي بالخمول والحد المطلق.
