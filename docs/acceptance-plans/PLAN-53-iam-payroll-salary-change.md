# PLAN-53 — صلاحيات كتابة الرواتب، ومعاملة تغيير الراتب باعتماد مستقل (IAM-B)

**Scope:** IAM — [دراسة الفجوات](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-02، وجزء الرواتب من G-IAM-08؛ ومعه من [دراسة الرواتب](../PAY_001_GAP_STUDY.md) سياسات G-PAY-10 الست عشرة وتغيير الراتب فيها (الباقي من G-PAY-10 في PLAN-54)، والملاحظة 3 في تقرير التحقق المستقل للدراسة (`EmployeePayProfiles.Create` بلا نطاق)  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-51 (مشروع `HRManagement.ArchitectureTests` وقائمة سقف Payroll، وسياسات الوحدة من الإعداد)؛ ويستعمل الـ outbox وحارس النشر الذري من PLAN-40  
**Related cards:** PAY-001 (تجاوز الاعتماد)، PAY-002 (ثبات الإعداد)، IAM-003 (SoD)، D-IAM-2، D-PAY-6. **يستهلكها:** PLAN-54..59

## Goal

- لا تُكتب أي بيانات في الرواتب بدور الوحدة `payroll` وحده. كل إجراء كتابة يحمل سياسة صريحة، وقائمة سقف Payroll فارغة.
- تغيير الراتب الأساسي أو الدرجة لموظف **معاملة**: يطلبها `payroll-processor`، ويعتمدها `payroll-approver` مختلف، ولا تدخل أي تشغيل قبل الاعتماد وتاريخ السريان. الطريق نفسه للواجهة وللـ API.
- تعديل السلّم والدرجات طلب يعتمده معتمد مختلف، ولا تُحذف درجة مستعملة.
- عقد واضح ومستقر (الكيان، الحالات، الانتقالات، الأحداث، واجهة التطبيق) تبني عليه PLAN-54..59 دون أن تعيد تعريفه.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-IAM-2 — تغيير الراتب والسلّم والدرجات | معاملة باعتماد: الطالب `Payroll.Process` (`payroll-processor`)، والمعتمد `Payroll.Approve` (`payroll-approver`) وليس الطالب. السريان بعد الاعتماد. والسلّم والدرجات بالطريقة نفسها مع مرجع قانوني | أي تغيير يحتاج شخصين في كل جهة (الشخصيتان موجودتان في القبول) |
| D-PAY-6 — مصدر تغييرات الراتب | القرار الإداري هو المصدر الطبيعي (عقد، ترفيع، نقل). الطلب اليدوي استثناء: سبب إلزامي، ومرجع قانوني إلزامي (`Payroll:SalaryChange:RequireLegalReference=true`)، ومعتمد ثانٍ. والمصادر الآلية تُسجَّل في الكيان نفسه بحالة `Applied` (البند 3)، ويعتمدها مستهلكوها | سجل واحد لكل تغيير راتب أياً كان مصدره |
| D-53-1 — تاريخ السريان | أول يوم من شهر (`Payroll:SalaryChange:RequireMonthStart=true`)، وليس قبل الشهر الجاري (يُرفض عند الطلب). ولا يُطبَّق في شهر فيه تشغيل مجمّد أو معتمد أو مرحّل (يُرفض عند الاعتماد والتفعيل). لأن التشغيل يأخذ الملف الفعّال وحده ([PayrollRunJobHandlers.cs:90-95](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)) ويقسّط الأيام بتواريخ الملف ([EmployeePayProfileRules.cs:10-26](../../src/Services/Payroll/Payroll.Domain/Services/EmployeePayProfileRules.cs))، فتغيير منتصف الشهر ينقص الأجر | لا أثر رجعي ولا تقسيط في الموجة 1 (G-PAY-18، الموجة 2: PAY-H) |
| D-53-2 — راتب خارج حدود الدرجة | يُرفض عند الطلب (`Payroll:SalaryChange:AllowOutOfGradeBounds=false`)، بالقاعدة التي يطبّقها مستهلك العقد ([InboundEventHandlers.cs:96-108](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) | الاستثناء يصير إعداداً، ويتطلب حينها مرجعاً قانونياً |
| D-53-3 — طلبات مفتوحة متعددة | طلب مفتوح واحد لكل موظف (`PendingApproval` أو `Scheduled`)، بفهرس فريد مرشَّح | طلب جديد يتطلب إلغاء المفتوح |
| D-53-4 — البيانات المرجعية | `SalaryScales`/`PayGrades`: طلب تغيير يعتمده `Payroll.Approve` (`Payroll:MasterData:RequireApproval=true`). `CompensationTypes`/`DeductionTypes`: سياسة `Payroll.MasterData` فقط (والخصومات النظامية تنتقل إلى نسخة الإطار في PLAN-54) | — |
| D-53-5 — `PayrollReports.BulkPayslips` | `Payroll.Process` (تصدير جماعي لقسائم تشغيل) | البديل `Payroll.Post` بسطر إعداد |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-53-iam-payroll-salary-change` | — |
| 0.2 | البناء والأساس | `dotnet build HRManagement.slnx`؛ `dotnet test src/Tests/HRManagement.ArchitectureTests`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay0\|FullyQualifiedName~Payroll"` | أخضر، أو قائمة في `reports/plan53-baseline.md` |
| 0.3 | جرد | قائمة سقف Payroll التي جمّدها PLAN-51 (المتوقع 16)، وكل مستدعٍ لـ `UpsertEmployeePayProfileCommand` ولـ `PUT api/payroll/employees/{id}/pay-profile` (منها AC-23 وAC-25 و`Pay023FrozenRunIsolationTests`)، وكل كاتب لـ `EmployeePayProfiles` (المستهلكات) | الجدول في `reports/plan53-baseline.md` |
| 0.4 | الـ outbox | التأكد من `AddPostgresOutbox<PayrollDbContext>` ([Payroll.Web/Program.cs:118-120](../../src/Services/Payroll/Payroll.Web/Program.cs)) ومن قاعدة PLAN-40 (النشر قبل حفظ واحد) | ملاحظة في التقرير |

---

## In scope

### 1. سياسة صريحة لكل كتابة في الرواتب (G-IAM-02، جزء الرواتب من G-IAM-08)

**المشكلة:** 16 إجراء POST بلا سياسة، يكفيها `Module.Payroll` = Admin أو `payroll` ([HrManagementAuthorizationExtensions.cs:41](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، وكل شخصيات الرواتب تحمل `payroll` ([personas.json:229-240](../../testdata/acceptance/personas.json)). والبوابة تفرض `Module.Payroll` فقط ([GatewayPathAuthorizationMiddleware.cs:228-235](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)):
- `EmployeePayProfiles.Create` ([EmployeePayProfilesController.cs:14, 79-101](../../src/Services/Payroll/Payroll.Web/Controllers/EmployeePayProfilesController.cs))؛
- `SalaryScales` إنشاء/تعديل/حذف ([SalaryScalesController.cs:35-67](../../src/Services/Payroll/Payroll.Web/Controllers/SalaryScalesController.cs))، و`PayGrades` ([PayGradesController.cs:45-86](../../src/Services/Payroll/Payroll.Web/Controllers/PayGradesController.cs))، و`CompensationTypes` ([CompensationTypesController.cs:30-82](../../src/Services/Payroll/Payroll.Web/Controllers/CompensationTypesController.cs))، و`DeductionTypes` ([DeductionTypesController.cs:31-63](../../src/Services/Payroll/Payroll.Web/Controllers/DeductionTypesController.cs))؛
- `PayrollRuns.Create` ([PayrollRunsController.cs:77-80](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollRunsController.cs))، و`PayrollReports.BulkPayslips` ([PayrollReportsController.cs:104-106](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs))، و`ExportJobs.Cancel` ([ExportJobsController.cs:35-37](../../src/Services/Payroll/Payroll.Web/Controllers/ExportJobsController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| سياسة `Payroll.MasterData` | `HrManagementAuthorizationExtensions.cs` + ثابت في `HrManagementPolicies` | الأدوار من الإعداد (PLAN-51)، والافتراضي Admin \| `payroll-processor` |
| السمات | المتحكمات أعلاه | `CompensationTypes`/`DeductionTypes` ← `Payroll.MasterData`. `SalaryScales`/`PayGrades` ← البند 4. `EmployeePayProfiles` ← البند 2. `PayrollRuns.Create` ← `Payroll.Process`. `BulkPayslips` ← D-53-5. `ExportJobs.Cancel` ← `Module.Payroll` صريحاً (فحص المالك قائم) |
| قائمة السقف | `tests/architecture/write-policy-allowlist/payroll.txt` | تُفرَّغ. واختبار `PayrollWriteActionsTests` في `HRManagement.ArchitectureTests` يفرض صفراً لـ Payroll (لا يقبل صفوفاً جديدة) |
| ربط الكيان | المتحكمات التي يعيد هذا البند كتابتها | نماذج عرض بقوائم سماح بدل الكيانات (`PayProfileChangeRequestViewModel`، `PayGradeChangeViewModel`، `SalaryScaleChangeViewModel`) |

**القواعد:**
1. البوابة تبقى على `Module.Payroll` للكتابة: القرار الدقيق في الخدمة، وتعليق البوابة ([GatewayPathAuthorizationMiddleware.cs:230-232](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)) صحيح بعد هذه الخطة.
2. الإجراءات الجديدة في هذه الخطة وفي PLAN-54..59 تحمل سياسات صريحة، والاختبار يرفض غير ذلك.

### 2. معاملة تغيير الراتب: العقد

**المشكلة:**
- `UpsertEmployeePayProfileHandler` يعطّل الملف النافذ وينشئ ملفاً بالراتب المُدخل ويحفظ فوراً: لا معتمد، ولا تحقق من حدود الدرجة، ولا نطاق ([EmployeePayProfileHandlers.cs:20-51](../../src/Services/Payroll/Payroll.Application/Features/EmployeePayProfiles/EmployeePayProfileHandlers.cs)). والإجراء يربط الكيان مباشرة.
- النسخة الجديدة تحمل الراتب والدرجة والتاريخ فقط، فتسقط `OrgUnitId` و`RegimeCode` والحساب المصرفي (`:38-46`؛ الحقول في [EmployeePayProfile.cs:33-59](../../src/Services/Payroll/Payroll.Domain/Entities/EmployeePayProfile.cs)). وتُغلق السابقة بـ `EffectiveTo = EffectiveFrom` الجديد (`:35`)، فيُحسب يوم الحدّ مرتين.
- `PUT api/payroll/employees/{id}/pay-profile` يستدعي الأمر نفسه بسياسة `Payroll.Process` ونطاق ([PayrollApiController.cs:138-177](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs))، بينما نموذج MVC بلا سياسة ولا نطاق.

**العقد (لا تغيّره PLAN-54..59، بل تضيف إليه):**

**الكيان** `PayProfileChangeRequest` (`Payroll.Domain/Entities/PayProfileChangeRequest.cs`، `BaseEntity` بجهة وتزامن `xmin` عبر `ConfigureOptimisticConcurrency`):

| الحقل | النوع | المعنى |
|-------|-------|--------|
| `EmployeeId` | Guid | الموظف |
| `OrgUnitId` | Guid? | من الملف الفعّال وقت الطلب؛ مفتاح النطاق |
| `ChangeKind` | `PayProfileChangeKind` | `BaseSalary` \| `PayGrade` \| `BaseSalaryAndGrade`. (الحساب المصرفي كيان مستقل بصانع ومدقق في PLAN-55، لا نوع تغيير هنا) |
| `Source` | `PayProfileChangeSource` | `Manual` (واجهة) \| `Api` \| `ContractActivated` \| `Promotion` \| `Transfer` \| `Correction` |
| `SourceReference` | string? (100) | معرّف العقد أو رقم القرار |
| `CurrentProfileId`، `CurrentBaseSalary`، `CurrentPayGradeId` | لقطة وقت الطلب | للعرض والتدقيق (`[AuditRedact]` على المبلغ) |
| `ProposedBaseSalary`، `ProposedPayGradeId` | decimal?، Guid? | المطلوب (`[AuditRedact]`) |
| `EffectiveFrom` | DateOnly | D-53-1 |
| `Reason` (500، إلزامي)، `LegalReference` (200) | string | D-PAY-6 |
| `State` | `PayProfileChangeState` | انظر الجدول التالي |
| `RequestedBy`، `RequestedByName`، `RequestedAt` | — | من الرمز |
| `DecidedBy`، `DecidedByName`، `DecidedAt`، `DecisionNote` | — | المعتمد أو الرافض |
| `AppliedAt`، `AppliedProfileId`، `LastApplyError` | — | نتيجة التطبيق |
| `SupersededByRequestId` | Guid? | إن أُبطل بتغيير آلي |

فهرس فريد مرشَّح على `EmployeeId` حين `State IN (PendingApproval, Scheduled)` (D-53-3).

**الحالات والانتقالات:**

| من | الفعل | من يملكه | إلى | شروط |
|----|-------|----------|-----|------|
| — | `Submit` | `Payroll.Process` | `PendingApproval` | النطاق، حدود الدرجة (D-53-2)، التاريخ (D-53-1)، السبب والمرجع، لا طلب مفتوح |
| `PendingApproval` | `Approve` | `Payroll.Approve`، و≠ `RequestedBy` | `Applied` إن كان `EffectiveFrom` ≤ اليوم، وإلا `Scheduled` | إعادة فحص الحدود. والفترة التي تحوي `EffectiveFrom` بلا تشغيل مجمّد أو معتمد أو مرحّل، وإلا رفض برسالة عربية والحالة باقية |
| `PendingApproval` | `Reject` | `Payroll.Approve`، و≠ `RequestedBy` | `Rejected` | `DecisionNote` إلزامي |
| `PendingApproval`، `Scheduled` | `Cancel` | الطالب أو `Payroll.Approve` | `Cancelled` | سبب |
| `Scheduled` | تفعيل آلي | `PayProfileChangeActivationJob` | `Applied` | الشرط نفسه. إن فشل يبقى `Scheduled` مع `LastApplyError`، ويظهر في اللوحة وفي المقياس `payroll_salary_change_apply_blocked` |
| `PendingApproval`، `Scheduled` | تغيير آلي للموظف نفسه طُبّق | `IPayProfileChangeApplier` | `Superseded` | `SupersededByRequestId` |
| — | تغيير آلي | مستهلك الحدث (PLAN-55 وما بعده) | `Applied` مباشرة | `Source` ≠ `Manual`/`Api`، و`DecidedBy` فارغ، والقرار الأصلي في `SourceReference` |

**الأوامر والواجهة البرمجية** (`Payroll.Application/Features/PayProfileChanges/`):

| Artifact | الغرض |
|----------|-------|
| `SubmitPayProfileChangeCommand`، `DecidePayProfileChangeCommand` (Approve/Reject)، `CancelPayProfileChangeCommand` | الانتقالات أعلاه. تعيد `(Success, RequestId, State, Error)` |
| `IPayProfileChangeApplier` / `PayProfileChangeApplier` | **المكان الوحيد** الذي ينشئ نسخة ملف أجر لطلب: يحفظ لقطة السابقة في `EmployeePayProfileHistory` ([EmployeePayProfileRules.cs:28-40](../../src/Services/Payroll/Payroll.Domain/Services/EmployeePayProfileRules.cs)) بعد إغلاقها بـ `EffectiveTo = EffectiveFrom − يوم`، وينسخ **كل** حقول السابقة إلى النسخة الجديدة ثم يطبّق المقترح، ويضع الطلب `Applied`، وينشر `IPayProfileChangeApplied`، بحفظ واحد. ودالة `ApplySystemChangeAsync(employeeId, kind, source, sourceReference, proposed…, effectiveFrom)` للمصادر الآلية |
| `PayProfileChangeActivationJob` | خدمة مستضافة يومية (`Payroll:SalaryChange:ActivationCron`) تطبّق المجدول المستحق. ونقطة `POST api/payroll/pay-profile-changes/activate-due` بسياسة `Payroll.Approve` للتشغيل اليدوي |
| API | `POST api/payroll/pay-profile-changes`، `POST .../{id}/approve\|reject\|cancel`، `GET api/payroll/pay-profile-changes?state=&employeeId=`. و`PUT api/payroll/employees/{id}/pay-profile` يبقى اسماً مستعاراً لـ `Submit` ويعيد 202 مع `requestId` و`state` |
| MVC | `PayProfileChangesController` (قائمة «بانتظار الاعتماد» للمعتمد، تفاصيل بالمقارنة الحالي/المقترح، أزرار الأفعال حسب السياسة). و`EmployeePayProfiles/Create` يصير نموذج طلب (`PayProfileChangeRequestViewModel`)، ويُحذف `UpsertEmployeePayProfileCommand` |

**الأحداث** (`src/Shared/HRManagement.MessageContracts/Events/`، بنمط [IContractActivated.cs](../../src/Shared/HRManagement.MessageContracts/Events/IContractActivated.cs): `Guid? TenantId => null`، `int SchemaVersion => 1`)، تُنشر عبر الـ outbox قبل الحفظ الواحد:

| الحدث | الحقول | متى |
|-------|--------|-----|
| `IPayProfileChangeSubmitted` | `RequestId`، `EmployeeId`، `ChangeKind`، `Source`، `EffectiveFrom`، `RequestedBy`، `OccurredAt` (بلا مبالغ) | `Submit` |
| `IPayProfileChangeDecided` | `RequestId`، `EmployeeId`، `Decision` (`Approved`\|`Rejected`)، `ResultingState`، `DecidedBy`، `OccurredAt` | `Approve`/`Reject` |
| `IPayProfileChangeApplied` | `RequestId`، `EmployeeId`، `NewProfileId`، `PreviousProfileId`، `EffectiveFrom`، `Source`، `SourceReference`، `PayGradeCode`، `BaseSalary`، `PreviousBaseSalary`، `OccurredAt` | كل تطبيق، يدوياً أو آلياً |

**القواعد:**
1. لا يكتب أي كود في `Payroll.Web` أو `Payroll.Application` في `EmployeePayProfiles` إلا المطبِّق والمستهلكات القائمة. والمستهلكات (`HandleContractActivatedHandler`، `HandleTransferExecutedHandler` الذي يعدّل الراتب في مكانه ([InboundEventHandlers.cs:281-313](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))) تُنقل إلى `ApplySystemChangeAsync` في خططها (البند «Out of scope»). اختبار معماري يسرد الكتّاب المسموحين ويفشل عند كاتب جديد.
2. التشغيل يرى الملف الفعّال وقت اللقطة فقط، كما اليوم. فلا يؤثر طلب `PendingApproval` أو `Scheduled` في أي تشغيل، ولا يغيّر تطبيقٌ لقطة تشغيل مجمّد.
3. نطاق الفرع يُفحص في `Submit` والقائمة والتفاصيل والأفعال، بمفتاح `OrgUnitId` (يغلق الملاحظة 3).
4. رسائل الرفض عربية، ولا تتغير الحالة عند الرفض.

### 3. المصادر الآلية تُسجَّل بالعقد نفسه

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ApplySystemChangeAsync` | المطبِّق | جاهزة ومختبرة هنا، مع اختبار وحدة لكل `Source` آلي |
| `docs/PAYROLL_SALARY_CHANGE.md` | جديد | العقد كاملاً (الكيان، الحالات، الانتقالات، الأحداث، المطبِّق، الإعداد) كمرجع لـ PLAN-54..59 |

**القواعد:**
1. هذه الخطة لا تغيّر سلوك المستهلكات. ربطها بالمطبِّق في الخطط المالكة لها.

### 4. السلّم والدرجات بطلب يعتمده آخر، ولا حذف لدرجة مستعملة

**المشكلة:** الإنشاء والتعديل والحذف فورية بلا سياسة ([SalaryScalesController.cs:35-67](../../src/Services/Payroll/Payroll.Web/Controllers/SalaryScalesController.cs)، [PayGradesController.cs:45-86](../../src/Services/Payroll/Payroll.Web/Controllers/PayGradesController.cs)). والحذف يُفرغ `PayGradeId` في ملفات الأجر بصمت لأن العلاقة `SetNull` ([PayrollDbContext.cs:281-293](../../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)). وحدود الدرجة يستعملها HR.Core للتحقق من العروض.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollReferenceChangeRequest` | `Payroll.Domain/Entities/` | `EntityType` (`SalaryScale`\|`PayGrade`)، `EntityId?`، `Operation` (`Create`\|`Update`\|`Delete`)، `ProposedJson`، `CurrentJson`، `Reason`، `LegalReference` (إلزامي)، `State` (`PendingApproval`\|`Applied`\|`Rejected`\|`Cancelled`)، والطالب والمعتمد. تزامن `xmin` |
| أوامر | `Payroll.Application/Features/ReferenceChanges/` | `Submit…`، `Decide…` (المعتمد ≠ الطالب، `Payroll.Approve`)، والتطبيق داخل الحفظ نفسه |
| المتحكمان | `SalaryScalesController`، `PayGradesController` | `Create/Edit/Delete` تنشئ طلباً (`Payroll.MasterData`) بنماذج عرض. وشاشة «طلبات البيانات المرجعية» للمعتمد |
| منع الحذف | المطبِّق | درجة يشير إليها ملف أجر أو طلب مفتوح، أو سلّم فيه درجات ← رفض برسالة. والعلاقة تصير `Restrict` في ترحيل |
| الإعداد | `Payroll:MasterData:RequireApproval` | `true` افتراضياً، ويمكن إطفاؤه في Development فقط (يُتجاهل في غيرها مع تحذير عند الإقلاع) |

**القواعد:**
1. تعديل حدود درجة لا يغيّر ملفات أجر قائمة. ملف خارج الحدود الجديدة يظهر في تقرير الاستثناءات القائم، ولا يُعدَّل آلياً.
2. `CompensationTypes`/`DeductionTypes` بسياسة `Payroll.MasterData` فقط (D-53-4).

---

## Out of scope

- السياسة على المدخلات اليدوية (البدلات والخصومات والتسويات) واعتمادها، والخصومات النظامية والعطل داخل نسخة الإطار ← **PLAN-54** (الباقي من G-PAY-10)، بالنمط نفسه (أمر طلب وأمر قرار، والمعتمد ≠ الطالب).
- تسجيل الحساب المصرفي وتحققه (كيان `EmployeeBankAccount` مستقل)، وربط `HandleContractActivatedHandler` و`HandleEmployeeCreatedHandler` بالمطبِّق (نسخ كل الحقول من الحدث) ← **PLAN-55** (G-PAY-02).
- آلة حالة التشغيل والتجميد ← **PLAN-56**؛ والطلب يقرأ حالة التشغيل كما هي وقت التنفيذ.
- تغيير الراتب من النقل والترفيع (`HandleTransferExecutedHandler` يعدّل في المكان) ← الموجة 2: **PAY-G** (G-PAY-13).
- الأثر الرجعي والتقسيط داخل الشهر ← الموجة 2: **PAY-H** (G-PAY-18).
- تفويض دور `payroll-approver` ← الموجة 2: **WF-F** (الآلية في PLAN-50).
- View Models لبقية متحكمات الرواتب ← الموجة 2: **IAM-F** (G-IAM-10).

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، بالفئة `AcIam53SalaryChangeTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-IAM-53")`).

| ID | Positive | Negative |
|----|----------|----------|
| AC-IAM-53a (يغطي AC-PAY-Ab) | `payroll-processor` يطلب رفع راتب موظف من أول الشهر الجاري (والاختبار يسجّل في `before.json` أن الشهر بلا تشغيل مجمّد) ← `PendingApproval`، والملف الفعّال بالراتب القديم. `payroll-approver` يعتمد ← `Applied`، ونسخة ملف جديدة تحمل الوحدة والنظام والحساب من السابقة، وصف تاريخ، وحدث `IPayProfileChangeApplied` في الـ outbox. ثم تشغيل للشهر الجاري يُحسب بالراتب الجديد | `payroll-processor` يعتمد طلبه ← 403 أو رفض «لا يجوز اعتماد الطلب من طالبه»، والحالة باقية |
| AC-IAM-53b (يغطي AC-IAM-Ba) | — | `payroll-approver`: POST `EmployeePayProfiles/Create` و`PUT .../pay-profile` ← 403 بلا صف. `a-payroll-poster`: POST `DeductionTypes/Edit` و`PayGrades/Delete` ← 403. `b-payroll-approver` يعتمد طلب الجهة A ← 404 |
| AC-IAM-53c | — | طلب براتب خارج حدود الدرجة ← 400 برسالة تذكر الحدين، ولا صف. طلب يسري في منتصف الشهر ← 400. طلب ثانٍ مفتوح للموظف نفسه ← 409 |
| AC-IAM-53d | طلب يسري من الشهر القادم يُعتمد ← `Scheduled`، والملف الفعّال لم يتغير | طلب يسري في الشهر الذي حسبه AC-IAM-53a (تشغيله مجمّد الآن) ← الاعتماد مرفوض برسالة عن الفترة المجمّدة، والحالة `PendingApproval`، ولقطة التشغيل لم تتغير |
| AC-IAM-53e | `payroll-processor` يطلب رفع الحد الأعلى لدرجة بمرجع قانوني ← `payroll-approver` يعتمد ← الحد الجديد يظهر في تفاصيل الدرجة، وفي سجل الطلب الطالب والمعتمد والمرجع. وقبل الاعتماد يبقى القديم | الطالب يعتمد طلبه ← رفض. حذف درجة عليها ملفات أجر ← رفض برسالة، والدرجة باقية |

وخارج القبول الحي:
- اختبار تكامل على PostgreSQL (Testcontainers) `Iam053PayProfileChangePostgreSqlTests`: (1) اعتمادان متزامنان للطلب نفسه ← واحد ينجح والآخر 409 (`xmin`)؛ (2) طلبان متزامنان للموظف نفسه ← الفهرس المرشَّح يرفض الثاني؛ (3) فشل بعد إنشاء النسخة داخل المطبِّق ← لا ملف جديد، ولا صف outbox، والطلب `PendingApproval` (معاملة واحدة)؛ (4) لا يوجد بعد أي سيناريو ملفان فعّالان للموظف (`IX_EmployeePayProfiles_OneActive`).
- اختبار تكامل بساعة مضبوطة لـ `PayProfileChangeActivationJob`: المجدول المستحق يطبَّق مرة واحدة ولو شُغّلت المهمة مرتين.
- تحديث انحدار: AC-23 وAC-25 و`Pay023FrozenRunIsolationTests` يستعملون الطلب ثم الاعتماد بدل الكتابة المباشرة، وتبقى نتائجهم كما هي.

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.ArchitectureTests --filter "FullyQualifiedName~Payroll|FullyQualifiedName~WriteActions"
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Iam053|FullyQualifiedName~Pay0|FullyQualifiedName~PayrollEngine"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Iam053|FullyQualifiedName~Pay023"
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web
# قبول على الملف الشبيه بالإنتاج (أوامر PLAN-37)، ثم:
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --no-deps --force-recreate payroll-web
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcIam53|FullyQualifiedName~Ac12|FullyQualifiedName~Ac23|FullyQualifiedName~Ac25"
pwsh scripts/iam053-validate.ps1
```

## Definition of done

- لا إجراء كتابة في Payroll.Web بلا سياسة صريحة، وقائمة سقف Payroll فارغة ومقفلة.
- لا طريق في الواجهة ولا في الـ API يغيّر راتباً أو درجة دون طلب يعتمده شخص آخر. و`UpsertEmployeePayProfileCommand` محذوف.
- النسخة الجديدة تحمل كل حقول السابقة، والسابقة تُغلق قبل يوم السريان.
- السلّم والدرجات بطلب واعتماد، ولا تُحذف درجة مستعملة.
- العقد موثّق في `docs/PAYROLL_SALARY_CHANGE.md`، والأحداث الثلاثة في `docs/MESSAGE_EVENT_MATRIX.md`.
- AC-IAM-53a..e = Pass في `acceptance-evidence/AC-IAM-53/` بعناصر الدليل الثمانية، وانحدار AC-12 وAC-23 وAC-25 أخضر.
- سطرا «الحالة» لـ G-IAM-02 وجزء الرواتب من G-IAM-08 محدّثان في الدراسة، وسطر G-PAY-10 في دراسة الرواتب («جزئي: السياسات والراتب — PLAN-53»).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-53-iam-payroll-salary-change.md` |
| Domain | `PayProfileChangeRequest.cs`، `PayrollReferenceChangeRequest.cs`، التعدادات `PayProfileChangeKind`/`PayProfileChangeSource`/`PayProfileChangeState`، `EmployeePayProfileVersioning.cs` (النسخ الكامل والإغلاق) |
| Application | `Features/PayProfileChanges/*` (الأوامر، `PayProfileChangeApplier`)، `Features/ReferenceChanges/*`؛ حذف `UpsertEmployeePayProfileCommand` |
| Infrastructure | إعداد الكيانين والفهرس المرشَّح و`Restrict` على `PayGrade` في `PayrollDbContext.cs`؛ `PayProfileChangeActivationJob.cs` |
| Web | `PayProfileChangesController.cs` وواجهاته، `PayrollReferenceChangesController.cs`، تعديل `EmployeePayProfilesController.cs` و`SalaryScalesController.cs` و`PayGradesController.cs` و`CompensationTypesController.cs` و`DeductionTypesController.cs` و`PayrollRunsController.cs` و`PayrollReportsController.cs` و`ExportJobsController.cs` و`PayrollApiController.cs`، ونماذج العرض |
| Contracts | `IPayProfileChangeSubmitted.cs`، `IPayProfileChangeDecided.cs`، `IPayProfileChangeApplied.cs` |
| Migration | `PAY053_SalaryChangeRequests` |
| Config | `Payroll:SalaryChange:*`، `Payroll:MasterData:RequireApproval`، أدوار `Payroll.MasterData` في `appsettings.json`/`Production`/`Staging` ومرآتها في `docker-compose.acceptance-prodlike.yml` |
| Tests | `PayrollWriteActionsTests.cs` و`PayProfileWritersTests.cs` (معماري)؛ `Iam053SalaryChangeStateMachineTests.cs`، `Iam053ApplierCopyForwardTests.cs`، `Iam053ReferenceChangeTests.cs` (وحدة)؛ `Iam053PayProfileChangePostgreSqlTests.cs`، `Iam053ActivationJobTests.cs` (تكامل)؛ تحديث `Pay023FrozenRunIsolationTests.cs` و`Ac23PayrollFreezeTests.cs` و`Ac25FraudHoldTests.cs` |
| Acceptance | `AcIam53SalaryChangeTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)؛ تحديث `scenarios` لـ `payroll-processor` و`payroll-approver` و`a-payroll-poster` و`b-payroll-approver` |
| Scripts | `scripts/iam053-validate.ps1` |
| Docs | `docs/PAYROLL_SALARY_CHANGE.md` (جديد: العقد)، `docs/MESSAGE_EVENT_MATRIX.md`، `docs/EVENT_PUBLISHER_INVENTORY.md` (صفوف الأحداث الثلاثة بنمط `SingleSave`، شرط R4 في حارس PLAN-40)، `docs/PAY_001_APPROVAL_BYPASS.md`، `docs/SOD_MATRIX.md` (تغيير الراتب والسلّم)، أسطر «الحالة» في `docs/IAM_001_SCOPE_GAP_STUDY.md` و`docs/PAY_001_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تغيير سلوك `PUT pay-profile` (202 بدل التطبيق الفوري) يكسر سيناريوهات قائمة | الجرد 0.3، وتحديث AC-23 وAC-25 و`Pay023` في الخطة نفسها، وانحدارها في التحقق |
| تعارض الأسماء مع PLAN-54 | الخطة تستعمل الأسماء القائمة (`Payroll.Process`، `Payroll.Approve`) وتضيف `Payroll.MasterData` فقط، وPLAN-54 يدوّنها في مرحلته 0.5 |
| المستهلكات تبقى تكتب في `EmployeePayProfiles` حتى PLAN-55 | اختبار الكتّاب المسموحين يسمّيها صراحة، فلا يظهر كاتب جديد، ويُحذف اسمها حين تنتقل إلى المطبِّق |
| قاعدة أول الشهر تمنع حالات حقيقية (تعيين في منتصف الشهر) | التعيين يمر بمستهلك العقد لا بالطلب اليدوي. والقاعدة إعداد، والتقسيط في الموجة 2 |
| فشل التفعيل الآلي لمجدول بسبب تشغيل مجمّد | يبقى `Scheduled` مع `LastApplyError` ومقياس، ويظهر للمعتمد |

## Effort estimate

9–10 أيام عمل: المرحلة 0 (0.5)، السياسات والسقف ونماذج العرض (1)، كيان الطلب والأوامر والمطبِّق والأحداث (3)، المهمة والـ API والواجهة (1.5)، السلّم والدرجات (1.5)، اختبارات التكامل والانحدار (1)، القبول والوثائق (1).
