# دراسة الإجازات والحضور والورديات ووقت العمل (LVA) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** الإجازات في HR.Contracts (`LeavePolicies`، `LeaveBalances`، `LeaveRequests`) وكل شيفرة الإجازة خارجها (ESS في HR.Core، مستهلكو `ILeaveApproved`/`ILeaveCancelled` في HR.Core والرواتب، رصيد نهاية الخدمة)؛ والحضور ووقت العمل: `DailyAttendanceRecords` (في HR.Core)، الورديات والجداول والإضافي، المهمات (`MissionFiles` في HR.Training)، العطل الرسمية، إغلاق الفترة وتسليمها للرواتب. الحساب المالي نفسه خارج النطاق (→ PAY)، وصرف رصيد الإجازة عند انتهاء الخدمة (→ SEP).  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والوثائق في `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. كل حالة «Done» عوملت كادعاء يحتاج دليلاً من الشيفرة.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK في النسخة المدروسة). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [HCM_002_ATTENDANCE.md](HCM_002_ATTENDANCE.md) · [completion-backlog/09 (HCM-002، ESS-001، MSS-001)](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md) · [12-SYRIA-BASELINE](completion-backlog/12-SYRIA-BASELINE-AND-SOURCES.md) · [13-SYRIA-PRODUCTION-CARDS (SYR-014)](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md) · [14-SYRIA-GO-LIVE-DECISIONS](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md) · [syr/holiday-calendar-policy.json](syr/holiday-calendar-policy.json) · [syr/legal-source-binding.md](syr/legal-source-binding.md) · [PILOT_DEPLOYMENT.md](PILOT_DEPLOYMENT.md) · [RELEASE_PRODUCT_SCOPE.md](RELEASE_PRODUCT_SCOPE.md) · [FUN_003_EXCEPTION_PATHS.md](FUN_003_EXCEPTION_PATHS.md) · [DATA_INVARIANTS_INVENTORY.md](DATA_INVARIANTS_INVENTORY.md) · [QA_RISK_TO_TEST_MATRIX.md](QA_RISK_TO_TEST_MATRIX.md) · [QA_E2E_JOURNEY_MATRIX.md](QA_E2E_JOURNEY_MATRIX.md) · [SOD_MATRIX.md](SOD_MATRIX.md) · [mss/manager-capabilities.json](mss/manager-capabilities.json) · [prod-ess/employee-selfservice-matrix.json](prod-ess/employee-selfservice-matrix.json)

---

## 1. الخلاصة

1. **المسار مبني كواجهات CRUD أساسية، بلا شرط `IsLabScale` ولا وضع محرك سياسات، لكن الإجازة لا تعمل بإعدادات الإنتاج الحالية.** الإجازة: طلب (HR أو ESS) ← اعتماد محلي ← خصم رصيد ذري ← `ILeaveApproved` إلى HR.Core (الحضور وحالة الموظف) والرواتب (خصم الإجازة بدون أجر). الحضور: سجل حالة واحدة لكل موظف ويوم، وإدخال جماعي، و«إغلاق فترة» ينشر ملخصاً لكل موظف إلى الرواتب. لا يتكرر هنا نمط G-02 من التوظيف. لكن `hr-contracts-web` في compose الإنتاج وفي k8s بلا `Integration__HrCoreBaseUrl` وبلا النطاق `hr-core.workflow`، فيفشل توليد الرقم المرجعي ولا يُنشأ أي طلب إجازة، لا من HR ولا من ESS (G-LVA-18، يملك إصلاحه PLAN-37). الحضور في HR.Core لا يتأثر بهذا. *(صُحّح بعد التحقق المستقل: كانت العبارة «يعمل بإعدادات الإنتاج دون تعليق»)* وعبارة «Done» في HCM-002 وSYR-014 وESS-001 وMSS-001 تستند إلى نماذج `SharedKernel` وملفات JSON لا تستخدمها الشيفرة التشغيلية.
2. **أربعة عوائق P0:**
   - **(0) G-LVA-18:** إنشاء طلب الإجازة يفشل بإعدادات الإنتاج لغياب ربط HR.Contracts بخدمة الترميز في HR.Core (خارج تقدير هذه الدراسة؛ → PLAN-37).
   - **(أ) G-LVA-01:** اعتماد الإجازة وإلغاؤها على PostgreSQL يلتزمان الحالة والرصيد بـ `ExecuteUpdate`، ثم يتعثر `SaveChanges` بتعارض `xmin` قبل إكمال المعاملة ونشر الحدث. الإجازة «معتمدة» ولا تصل إلى الحضور ولا إلى الرواتب (يحتاج تحقق؛ كل اختبارات المعالج على SQLite).
   - **(ب) G-LVA-02:** لا يمكن تشغيل الأرصدة بحجم جهة حقيقية. كل نوع إجازة يحتاج سجل رصيد يُدخل يدوياً لموظف واحد، وشاشة «تهيئة السنة» غير موجودة، ولا تعديل للرصيد، وقائمة الطلبات تعرض أحدث 100 طلب فقط.
   - **(ج) G-LVA-03:** «إغلاق فترة الحضور» لا يقفل شيئاً. التصحيح بعد الإغلاق لا يصل إلى الرواتب، والإدخال الجماعي يعدّ الجمعة والسبت والعطل غياباً، والإغلاق المتأخر عن تشغيل الرواتب لا يُخصم أبداً. وهذا مؤشر نجاح الإطلاق التجريبي.
3. **ثغرات سلامة القرار والمال (P1):**
   - طلب ESS يُحسب بالأيام التقويمية ويتجاوز كل القواعد.
   - الاعتماد بدور واحد (`hr-contracts`)، دون مدير مباشر، وفصل الواجبات شكلي (يسقط لطلبات ESS)، والرفض بلا ضوابط.
   - التداخل لا يُعاد فحصه عند الاعتماد، ولا يُفحص مع المهمات والحركات والفصل.
   - الإلغاء بعد الاستهلاك يعيد الرصيد كاملاً ويمحو أياماً من فترة مغلقة.
   - قيم الإجازة القانونية مثبتة في الشيفرة وغير مربوطة بالنظام القانوني، واحتساب المدة خاطئ عند العطل ورأس السنة والإشعار.
   - تعديل سجل الحضور قد ينقله إلى موظف آخر بصمت.
   - بدل المهمة قد يتكرر عند إعادة تسليم الرسالة.
4. **فجوات قدرات (P2):** أنواع الإجازات (حج، دراسية، طارئة، وفاة…) والاستحقاق بالقدم والترحيل والسقوط؛ ربط الإجازة المرضية باللجان الطبية؛ تقويم رسمي مركزي؛ ربط الورديات بالحضور، والتوقيت والتأخير، واستيراد الأجهزة، والإضافي إلى الرواتب.
5. **فجوة تحقق:** لا خطة قبول ولا مجلد أدلة ولا بيانات بذرة قبول للإجازة أو الحضور. اختبارات HCM-002 انعكاس على نماذج غير مستخدمة، واختبارات SYR-014 تقرأ JSON، ورحلة LEV في E2E تفحص الوصول إلى الصفحة فقط. والثابت INV-09 «xmin + حالة» هو تحديداً ما يكسر الاعتماد.

**التقدير الإجمالي:** نحو 61–74 يوم عمل لمطوّر واحد، منها 19–23 يوماً للمرحلة P0 (LVA-A وLVA-B وLVA-C). منها 9–10 أيام في الموجة 1 (LVA-C = PLAN-57)، والباقي في الموجة 2. إصلاح G-LVA-18 خارج هذا التقدير (PLAN-37).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | أنواع الإجازة وقيمها | `LeaveType` بخمس قيم؛ حدود ثابتة في `GetMaxDuration`؛ جدول `LeavePolicies` لكل نوع بلا نظام قانوني | ❌ G-LVA-08، G-LVA-12 |
| 2 | الاستحقاق والرصيد | `LeaveBalance` يدوي لكل موظف/نوع/سنة؛ لا استحقاق تلقائي ولا ترحيل ولا تسوية | ❌ G-LVA-02 |
| 3 | طلب من HR | `CreateLeaveRequestHandler` ← `Pending` + `ServiceTransaction`، تحقق تداخل مع المعتمد فقط | ❌ بإعدادات الإنتاج (G-LVA-18)؛ ⚠️ G-LVA-06، G-LVA-09 |
| 4 | طلب من ESS | `EssPortalController.Leave` ← `ILeaveRequested` ← `ProcessLeaveRequestedHandler` بلا تحقق | ❌ G-LVA-18 (المستهلك يفشل في الإنتاج)، G-LVA-04 |
| 5 | الاعتماد | محلي؛ SoD منشئ≠معتمد؛ حجز وخصم بـ `ExecuteUpdate` | ❌ على PostgreSQL (G-LVA-01)؛ حوكمة (G-LVA-05) |
| 6 | الرفض | `Reject` بلا سياسة صريحة ولا SoD ولا سبب | ⚠️ G-LVA-05 |
| 7 | الإلغاء والقطع | `Cancel` لأي إجازة معتمدة في أي وقت؛ لا قطع ولا مباشرة | ⚠️ G-LVA-07 (+G-LVA-01) |
| 8 | الأثر في HR.Core | `LeaveApprovedConsumer` يكتب `OnLeave`؛ `LeaveScheduleReconcilerJob` كل ساعة | ✅ مع ثغرات (G-LVA-10) |
| 9 | الأثر في الرواتب | الإجازة بدون أجر ← `AbsenceDeduction` واحد بمرجع الطلب | ⚠️ تسليم → PAY (G-LVA-04، G-LVA-07) |
| 10 | الإجازة المرضية واللجان الطبية | لا ربط مع HR.Health | ❌ G-LVA-13 |
| 11 | الرصيد عند انتهاء الخدمة | `ComputeAnnualLeaveRemainingAsync` = استحقاق السنة − المستخدم | ⚠️ → SEP (G-LVA-12) |
| 12 | الحضور اليومي | `DailyAttendanceRecord` حالة واحدة/يوم؛ إدخال فردي وجماعي وتعديل وحذف | ⚠️ G-LVA-10 |
| 13 | إغلاق الفترة ← الرواتب | `ClosePeriodAndPublishAsync` ← `AttendancePeriodClosedConsumer` | ❌ G-LVA-03 |
| 14 | الورديات والجداول والإضافي | `Shift`، `ShiftRoster`، `ShiftAssignment.OvertimeHours` | ❌ غير مربوطة بالحضور ولا بالرواتب (G-LVA-14) |
| 15 | المهمات | `MissionFilesController` ← `IMissionApproved` ← بدل في الرواتب | ⚠️ G-LVA-11 |
| 16 | العطل الرسمية والتقويم | `OfficialHoliday` في الرواتب، 4 عطل ثابتة | ❌ G-LVA-15 |
| 17 | الأجهزة والتوقيت والتأخير | غير موجود | ❌ G-LVA-14 |

### 2.1 السلوك بإعدادات الإنتاج والاعتمادات والنطاق (باختصار)

- **لا بوابات مختبر:** لا يوجد `IsLabScale` ولا `ResolveMode` في HR.Contracts ولا في متحكمات الحضور والورديات، وحماية المسار الوحيدة هي القواعد المكتوبة في الشيفرة. لكن **الإجازة لا تعمل بإعدادات الإنتاج الحالية:** `hr-contracts-web` بلا `Integration__HrCoreBaseUrl` وبنطاق `file-storage.references` وحده، فيفشل إنشاء كل طلب إجازة (G-LVA-18 → PLAN-37). الحضور والإغلاق في HR.Core يعملان كما هما. *(صُحّح بعد التحقق المستقل)*
- **محرك السياسات:** لا مفتاح `PolicyEngine` في أي من ملفات `appsettings*.json` الأربعة لـ HR.Contracts. لذلك يُحسم المجال `Contracts.Leave` إلى `Off`، ويعيد `PolicyEnforceClient` القيمة `Allowed=true` ([PolicyEnforceClient.cs:29-41](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)). القواعد `R-LEAVE-LONG/SELF/MAX` بلا أثر (→ POL).
- **المهام الخلفية:** `LeaveScheduleReconciler:Enabled` افتراضه `true`، فتعمل المهمة كل ساعة في الإنتاج ([Program.cs:144-146](../src/Services/HRCore/HR.Core.Web/Program.cs)).
- **الاعتمادات (→ WF):** اعتماد الإجازة والمهمة وتصحيح الحضور محلي في كل خدمة، ولا يوجد تعريف مسار عمل لأي منها.
- **النطاق (→ IAM):** نطاق الفرع يُطبَّق على دور `branch-manager` فقط ([HrOrgScopeVisibility.cs:143-146](../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs))، وهذا الدور لا يصل إلى HR.Contracts ([HrManagementAuthorizationExtensions.cs:28-31](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). فعملياً لا نطاق فرع على الإجازات. الحضور مقيّد بالنطاق لمديري الفروع، لأنهم يحملون `hr-core` أيضاً في كتالوج الشخصيات.

---

## 3. ما هو منجز فعلاً (للإنصاف)

- خصم الرصيد ذري ومشروط (`Used + d <= TotalEntitlement`)، وعليه قيد فريد `(TenantId, EmployeeId, LeaveType, Year)` ([ContractsDbContext.cs:206-212](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)). وإذا فشل الخصم يُعاد الطلب إلى `Pending` ([LeaveRequestHandlers.cs:291-297](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
- الطلب الجديد `Pending` دائماً بلا معتمد من العميل. التعديل والحذف محصوران بـ `Pending`، وحذف إجازة معتمدة ممنوع لصالح الإلغاء الذي يعيد الرصيد ([LeaveRequestHandlers.cs:70-83, 117-120, 142-145](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
- Outbox بـ PostgreSQL في HR.Contracts وHR.Core والرواتب وHR.Training. مستهلكا الإجازة في الرواتب يستخدمان `ShouldSkipAsync`/`TryRecordAsync` ([PayrollEventConsumers.cs:227-258](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)).
- الإجازة بدون أجر لها حامل خصم واحد بمرجع الطلب وبفترة الإجازة نفسها، ويُحذف عند الإلغاء ما لم يُطبَّق في تشغيل (PAY-DD-001، [InboundEventHandlers.cs:369-396, 425-431](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- HR.Core لا يضع الموظف في `OnLeave` قبل بداية الإجازة. وتعيده مهمة `LeaveScheduleReconciler` إلى `Active` عند انتهائها، بقفل استشاري ([LeaveAndAdvanceApprovedConsumer.cs:25-45](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs)).
- سجل الحضور فريد لكل موظف ويوم ([HRCoreDbContext.cs:392](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)). القائمة والتفاصيل والتعديل والحذف والإغلاق تطبّق نطاق الفرع ([OrgBranchControllers.cs:74-113, 262-273](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs))، أما الإنشاء فلا يفحص نطاق `vm.EmployeeId` (`:126-145`؛ G-IAM-04 → PLAN-52). *(صُحّح بعد التحقق المستقل)*
- ESS يقيّد الطلب بموظف المستخدم نفسه (مطالبة `employee_id`)، ويحفظ الـ outbox فعلاً ([EssPortalController.cs:85-124](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)).
- البوابة تفرض `HrContractsApprove` على كل POST في `hr-contracts` خارج معالج التعيين ([GatewayPathAuthorizationMiddleware.cs:222-226](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-LVA-01 — اعتماد الإجازة وإلغاؤها ينكسران على PostgreSQL بعد التزام الحالة والرصيد (يحتاج تحقق)
- **الدليل:**
  - المعالج يحمّل الطلب **متتبَّعاً**، فـ `GetLeaveRequestAsync` بلا `AsNoTracking` ([ContractsRepository.cs:26-27](../src/Services/HRContracts/HR.Contracts.Infrastructure/Repositories/ContractsRepository.cs)).
  - ثم يحجز الحالة `Pending→Approved` بـ `ExecuteUpdateAsync` ([LeaveRequestHandlers.cs:267-276](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs))، ويخصم الرصيد بـ `ExecuteUpdateAsync` ([:284-297](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)). كلاهما يُلتزم فوراً، إذ لا `BeginTransaction`، وسلسلة MediatR فيها `ValidationBehavior` فقط ([DependencyInjection.cs:13](../src/Services/HRContracts/HR.Contracts.Application/DependencyInjection.cs)).
  - بعد ذلك يعدّل الكيان المتتبَّع `entity.Status = Approved` ([:278-279](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)) ويستدعي `SaveChangesAsync` ([:317](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - `LeaveRequest` يحمل رمز تزامن `xmin` على PostgreSQL ([ContractsDbContext.cs:135](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)، [ConcurrencyExtensions.cs:21-27](../src/Shared/HRManagement.ServiceDefaults/ConcurrencyExtensions.cs)). الـ `ExecuteUpdate` السابق غيّر `xmin`، فالـ UPDATE الذي يولّده `SaveChanges` بشرط `xmin` القديم يصيب 0 صفوف ويرمي `DbUpdateConcurrencyException`. يحوّلها `ConcurrencyConflictMiddleware` إلى رسالة «تعارض» ([ConcurrencyConflictMiddleware.cs:28-32](../src/Shared/HRManagement.ServiceDefaults/ConcurrencyConflictMiddleware.cs)).
  - النتيجة المتوقعة: الطلب `Approved` والرصيد مخصوم (التزما)، لكن `ServiceTransaction` لا تكتمل و`ILeaveApproved` لا يُكتب في الـ outbox ([:318-329](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)). إعادة المحاولة تعطي «الطلب ليس قيد الانتظار» ([:235](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - الإلغاء بالنمط نفسه: حجز ([:170-177](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)) ← تعديل الكيان ([:181-182](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)) ← إعادة الرصيد ملتزمة ([:184](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)) ← `SaveChanges` ([:193](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)) قبل نشر `ILeaveCancelled` ([:196-206](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - لم يُشغَّل المعالج قط على PostgreSQL. كل اختباراته على SQLite، حيث لا يُضبط `xmin` (`if (!context.IsNpgsql()) return entity;`): [OutboxAtomicityIntegrationTests.cs:355-398](../src/Tests/HRManagement.IntegrationTests/OutboxAtomicityIntegrationTests.cs)، [ContractLifecycleIntegrationTests.cs:31](../src/Tests/HRManagement.IntegrationTests/ContractLifecycleIntegrationTests.cs). واختبار Testcontainers الوحيد يختبر جملة SQL منفصلة لا المعالج ([Data001ConcurrencyMatrixTests.cs:160-194](../src/Tests/HRManagement.IntegrationTests/Data001ConcurrencyMatrixTests.cs)).
  - ثانوي (1): `ExecuteUpdate` يتجاوز `AuditSaveChangesInterceptor`، فهو `SaveChangesInterceptor` ([AuditSaveChangesInterceptor.cs:13-30](../src/Shared/HRManagement.ServiceDefaults/AuditSaveChangesInterceptor.cs)). لذلك لا سجل تدقيق لخصم الرصيد ولا لإعادته.
  - ثانوي (2): إذا كان `ServiceTransactionId` فارغاً يأخذ المعالج أحدث معاملة `LeaveAdvance` للموظف ([:300-305](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)). والسلف تستخدم النوع نفسه ([AdvanceRequestHandlers.cs:70](../src/Services/HRContracts/HR.Contracts.Application/Features/AdvanceRequests/AdvanceRequestHandlers.cs))، فقد يُكمل المعالج معاملة سلفة.
- **الأثر:** (بعد إصلاح G-LVA-18، إذ لا يُنشأ طلب أصلاً قبله) في الإنتاج يرى المعتمد رسالة تعارض في كل اعتماد. الرصيد يُخصم، لكن الموظف لا يظهر «في إجازة» في الحضور، والإجازة بدون أجر لا تُخصم من الراتب. وفي الإلغاء يُعاد الرصيد بينما يبقى الخصم والحضور كما كانا. لا أثر تدقيق لحركة الرصيد.
- **الإصلاح المقترح:**
  - Transaction واحدة (`BeginTransactionAsync`) تضم الحجز والخصم وإكمال المعاملة وكتابة الـ outbox.
  - عدم تعديل الكيان المتتبَّع بعد `ExecuteUpdate`، أو الاستغناء عن `ExecuteUpdate` للطلب والاعتماد على `xmin` مع الكيان المتتبَّع.
  - سجل حركة رصيد (ledger) مدقَّق بدل التعديل الصامت.
  - ربط صريح بالمعاملة بلا بحث احتياطي.
  - سكربت إصلاح للطلبات `Approved` التي لا تملك معاملة مكتملة ولا حدثاً.
- **التحقق المطلوب:**
  - **موجب:** على PostgreSQL (Testcontainers وبيئة القبول) ينجح الاعتماد، والطلب والرصيد والمعاملة والحدث في الـ outbox كلها في commit واحد، ويصل الحدث إلى HR.Core والرواتب.
  - **سالب:** حقن فشل بعد الخصم لا يترك أي تغيير ملتزم.
  - **الإلغاء:** مثله.

#### G-LVA-02 — لا يمكن تشغيل الأرصدة والطلبات بحجم جهة حقيقية
- **الدليل:**
  - كل نوع إجازة، حتى المرضية والأمومة وبدون أجر، يحتاج سجل رصيد، وغيابه رفض: `if (balance is null) return "لا يوجد رصيد إجازات مسجل…"` ([LeaveAndAdvanceRules.cs:63-68](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs))، ويُستدعى عند الاعتماد ([LeaveRequestHandlers.cs:258-264](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - في التشغيل لا يُنشأ الرصيد إلا بطريقين:
    - `LeaveBalancesController.Create`، لموظف ونوع وسنة واحدة ([LeaveBalancesController.cs:89-117](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs)).
    - `InitializeYear` ([:119-162](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs))، لكن مجلد `Views/LeaveBalances` يحوي `Create` و`Index` فقط، فلا شاشة له ولا رابط إليه.
  - لا إنشاء تلقائي عند التعيين ولا عند بداية السنة. خارج هذين الطريقين لا يظهر `new LeaveBalance` إلا في بذرة العرض.
  - لا `Edit` ولا `Delete` ولا تسوية في المتحكم. رصيد أُدخل خطأً لا يُصحَّح من الواجهة، ولا طريق لأرصدة افتتاحية عند الترحيل.
  - حتى لو وُجدت الشاشة، يمنح `InitializeYear` القيمة `MaxDaysPerYear` كاملة، بلا تناسب مع تاريخ التعيين ([:147-154](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs)).
  - قائمة الطلبات تجلب أحدث 100 طلب بتاريخ البداية ثم ترشّح في الذاكرة ([LeaveRequestHandlers.cs:37-38](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)، [LeaveRequestsController.cs:44-52](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveRequestsController.cs)، `MaxPageSize = 100` في [SearchFilterBase.cs:7](../src/Shared/HRManagement.SharedKernel/ViewModels/SearchFilterBase.cs)). الطلب `Pending` الذي يقع خارج أحدث 100 لا يظهر، ولو مع ترشيح الحالة أو الموظف.
- **الأثر:**
  - جهة بـ 1000 موظف وخمسة أنواع تحتاج 5000 إدخال يدوي في السنة، وإلا رُفض كل اعتماد.
  - طلبات الانتظار تختفي من القائمة بعد أيام من التشغيل.
  - لا يمكن تصحيح رصيد ولا ترحيل الأرصدة الافتتاحية للموظفين القائمين.
- **الإصلاح المقترح:**
  - خدمة استحقاق تُنشئ الأرصدة عند `IEmployeeCreated` أو تفعيل العقد، وبمهمة سنوية idempotent، بالتناسب مع تاريخ التعيين وبقيم الكتالوج (G-LVA-08).
  - تسوية مدققة: سبب إلزامي ومعتمد ثانٍ.
  - استيراد أرصدة افتتاحية.
  - الأنواع التي لا رصيد لها (مثل المرضية وبدون أجر) تُقاس بحدود القاعدة لا بسجل رصيد.
  - ترقيم وترشيح على الخادم، وصندوق «بانتظار اعتمادي».
- **التحقق المطلوب:**
  - تهيئة سنة لجهة 500 موظف تنشئ الأرصدة مرة واحدة، وإعادة التشغيل لا تكرر.
  - موظف معيَّن في 1 تموز يحصل على نصف الاستحقاق السنوي.
  - تسوية بلا سبب، أو من منشئها نفسه، مرفوضة.
  - الطلب `Pending` رقم 150 يظهر في صندوق الانتظار.

#### G-LVA-03 — إغلاق فترة الحضور ليس إغلاقاً، والتسليم للرواتب يفقد التصحيحات
- **الدليل:**
  - لا كيان «فترة حضور» ولا حقل قفل. `DailyAttendanceRecord` = موظف + يوم + حالة + ملاحظة ([DailyAttendanceRecord.cs:7-23](../src/Services/HRCore/HR.Core.Domain/Entities/DailyAttendanceRecord.cs)).
  - `ClosePeriodAndPublishAsync` لا يحفظ شيئاً عن الفترة. ينشر `IAttendancePeriodClosed` لكل موظف بـ `TransactionId = Guid.NewGuid()` ([AttendanceIntegrationService.cs:75-124](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs)).
  - بعد «الإغلاق» يبقى الإنشاء والتعديل والحذف والإدخال الجماعي مفتوحاً ([OrgBranchControllers.cs:128-198, 213-249](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)). والإجازة المعتمدة أو الملغاة تعيد كتابة أيام مغلقة أو تحذفها ([AttendanceIntegrationService.cs:25-44, 61-71](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs)).
  - الرواتب تأخذ أول إغلاق فقط:
    - تتجاهل الإغلاق المتكرر لنفس الموظف والفترة ([PayrollEventConsumers.cs:350-359](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)).
    - تتجاهل الملخص الذي صار فيه `AbsentDays = 0` (`if (m.AbsentDays <= 0) return;`، [:348](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs))، فيبقى الخصم القديم.
    - فترة بحدود مختلفة قليلاً (1–30 مقابل 1–31) تنشئ خصماً ثانياً.
  - الإغلاق المتأخر لا يُخصم أبداً:
    - المحمّل يأخذ الغياب المتقاطع مع فترة التشغيل الحالية فقط ([PayrollPartitionInputLoader.cs:158-162](../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)).
    - تشغيل التصحيح يمرر قائمة غياب فارغة ([CorrectionRunHandlers.cs:90](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
    - لا شيء في الرواتب يشترط إغلاق الحضور قبل الحساب.
  - الإدخال الجماعي يكتب الحالة الافتراضية لكل أيام الشهر، ومنها الجمعة والسبت والعطل والأيام المستقبلية ([OrgBranchControllers.cs:233-242](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)). باختيار `Absent` يحسب الإغلاق 30–31 يوم غياب. واليوم الذي لا سجل له لا يُعدّ غياباً ولا حضوراً.
  - إغلاق وحدة يشمل موظفيها المباشرين فقط (`e.PrimaryOrgUnitId == orgUnitId`، [AttendanceIntegrationService.cs:83](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs))، لا الوحدات الفرعية.
  - يستبعد الإغلاق كل `Terminated`، ولو عمل جزءاً من الفترة ([:81](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs)).
  - لا تحقق من `periodEnd >= periodStart` ولا من تداخل الفترات ([OrgBranchControllers.cs:262-273](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
  - الوثائق متناقضة: [PILOT_DEPLOYMENT.md:21](PILOT_DEPLOYMENT.md) يجعل «إغلاق فترة حضور واحدة مرتبطة بالرواتب» مؤشر نجاح، و[RELEASE_PRODUCT_SCOPE.md:28](RELEASE_PRODUCT_SCOPE.md) يقول إن الحضور «CRUD محلي؛ لا أحداث إلى الرواتب» ومؤجَّل إلى v2.
- **الأثر:**
  - خصومات غياب خاطئة: غياب على أيام العطلة، أو غياب صُحِّح ولم يُعكس.
  - لا يمكن تصحيحها من النظام.
  - إغلاق متأخر يعني غياباً بلا خصم.
  - مؤشر نجاح الإطلاق التجريبي لا يُثبت بنتيجة موثوقة.
- **الإصلاح المقترح:**
  - كيان `AttendancePeriod` (وحدة، فترة، حالة `Open→Closed→Locked`) بقيد فريد يمنع التداخل.
  - رفض كل كتابة على يوم في فترة مغلقة، إلا عبر تصحيح بسبب ومعتمد ينتج فرقاً (delta) مرقّماً.
  - ملخص بمعرّف فترة ثابت ونسخة تستبدل ما قبلها، وحساب الأثر في الرواتب (→ PAY).
  - قاعدة صريحة لليوم بلا سجل.
  - إدخال جماعي على أيام العمل فقط حسب التقويم، وشمول الوحدات الفرعية.
  - تحذير في تشغيل الرواتب عن الفترات غير المغلقة.
  - **الخطة:** [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md)
- **التحقق المطلوب:**
  - تعديل يوم في فترة مغلقة مرفوض.
  - تصحيح معتمد بعد الإغلاق ينتج delta واحداً يُطبَّق في التشغيل التالي.
  - الإغلاق المكرر لا يكرر الخصم، وإرجاع الغياب إلى صفر يلغيه.
  - الإدخال الجماعي «غائب» لا يكتب الجمعة والسبت والعطل.
  - إغلاق وحدة أب يشمل الأبناء.

#### G-LVA-18 — إنشاء طلب الإجازة يفشل بإعدادات الإنتاج: HR.Contracts غير موصول بخدمة الترميز (أُضيفت بعد التحقق المستقل)
- **الدليل:**
  - إنشاء الطلب من HR ([LeaveRequestHandlers.cs:91](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)) واستهلاك طلب ESS ([InboundEventCommands.cs:269](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)) يستدعيان `ApplyReferenceAsync`. وهو يرمي `ValidationException` إذا فشل توليد الرقم ([ServiceTransactionReferenceSupport.cs:20-32](../src/Services/HRContracts/HR.Contracts.Application/Services/ServiceTransactionReferenceSupport.cs)).
  - عميل الترميز يأخذ عنوانه من `Integration:HrCoreBaseUrl`، وبدونه يُسجَّل `HttpClient` بلا عنوان ([CodeGenerationClient.cs:79-85](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)).
  - `hr-contracts-web` في [docker-compose.production.yml:279-293](../docker-compose.production.yml) بلا `Integration__HrCoreBaseUrl`، ونطاقه `file-storage.references` وحده (`:292`). وفي k8s يقرأ `hr-app-config` ([deployment.yaml:49-50](../deploy/k8s/base/hr-contracts-web/deployment.yaml)) ولا مفتاح فيه؛ المفتاح موجود في إعداد البوابة فقط ([configmap-yarp.yaml:39](../deploy/k8s/base/configmap-yarp.yaml)).
  - compose القبول يضيف المفتاح والنطاق `hr-core.workflow` صراحةً ([docker-compose.acceptance.yml:99-105](../docker-compose.acceptance.yml))، فلا يظهر العطل في أي اختبار.
  - ESS يقول للموظف «تم تقديم طلب الإجازة» بعد نشر الحدث ([EssPortalController.cs:103-123](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs))، بينما يفشل المستهلك في العقود بعد المحاولات.
- **الأثر:** بإعدادات الإنتاج لا يُنشأ أي طلب إجازة، قبل أن يُطرح سؤال الاعتماد (G-LVA-01). وطلبات ESS تضيع بصمت في طابور الأخطاء.
- **الإصلاح المقترح:** ربط كل خدمة بعناوين ونطاقات S2S التي تحتاجها في compose الإنتاج وk8s، وملف القبول الشبيه بالإنتاج لا يرث ربط القبول. خارج تقدير هذه الدراسة.
  - **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (يملك الإصلاح؛ G-IAM-03)
- **التحقق المطلوب:** على الملف الشبيه بالإنتاج بعد PLAN-37: إنشاء طلب إجازة من HR ومن ESS ينجح ويحمل رقماً مرجعياً. سالب: حذف المفتاح يُفشل فحص جاهزية الإقلاع لا الطلب الأول.

### 4.2 ثغرات سلامة القرار والمال (P1)

#### G-LVA-04 — طلب ESS يتجاوز كل القواعد ويُحسب بالأيام التقويمية
- **الدليل:**
  - ESS يحسب `duration = EndDate − StartDate + 1` بالأيام التقويمية ([EssPortalController.cs:96](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)). المستهلك يحفظها كما هي ([InboundEventCommands.cs:279](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs))، بينما مسار HR يحسب أيام العمل ([LeaveAndAdvanceRules.cs:16-18](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)). أسبوع إجازة من ESS يستهلك 7 أيام، ومن HR يستهلك 5.
  - `ProcessLeaveRequestedHandler` لا يفحص التداخل ولا الرصيد ولا الحد ولا الإشعار ولا المحرك ([InboundEventCommands.cs:252-289](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs))، بخلاف مسار HR ([LeaveRequestHandlers.cs:55-68](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - نوع الإجازة نص حر ([EssPortalController.cs:197](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)). الشاشة تعرض «طارئة» `Emergency` ([Leave.cshtml:12-16](../src/Services/HRCore/HR.Core.Web/Views/EssPortal/Leave.cshtml))، وهو غير موجود في `LeaveType`، فيتحول بصمت إلى `Other` ([InboundEventConsumers.cs:81-83](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)).
  - شاشة ESS لا تعرض «بدون أجر» أصلاً (سنوية/مرضية/طارئة/أخرى فقط)، لكن النوع نص حر بلا تحقق على الخادم، فطلب POST معدَّل بالنوع `Unpaid` يُخصم في الرواتب على الأيام التقويمية مقسومة على 22 أو 30 ([InboundEventHandlers.cs:357-367](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))، أي خصم زائد (→ PAY).
  - لا يرى الموظف رصيده ولا حالة طلبه، ولا يستطيع سحبه. شاشات ESS للإجازة تقتصر على نموذج التقديم. بطاقة ESS-001 «Done» تستند إلى `EmployeeServiceCase` غير المستخدم في التشغيل ([09:236-247](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)).
- **الأثر:** خصم رصيد ومال مختلف للإجازة نفسها بحسب القناة. طلبات متداخلة أو بلا رصيد تصل إلى المعتمد دون إنذار. والموظف لا يعرف مصير طلبه. *(صُحّح بعد التحقق المستقل: «بدون أجر» غير معروض في شاشة ESS، فالخصم المالي الزائد يحتاج طلباً معدَّلاً؛ فرق الرصيد بالأيام التقويمية قائم في الاستخدام العادي)*
- **الإصلاح المقترح:**
  - خدمة تحقق نطاقية واحدة يستدعيها مسارا HR وESS.
  - ESS يرسل التواريخ والنوع فقط، والمدة تُحسب في العقود.
  - النوع غير المعروف يُرفض.
  - حدث نتيجة يعود إلى الموظف.
  - شاشة «إجازاتي» فيها الرصيد والحالة والسحب.
- **التحقق المطلوب:**
  - أسبوع من ESS = 5 أيام عمل مخصومة.
  - طلب ESS متداخل أو بلا رصيد يُرفض بسبب يظهر للموظف.
  - نوع غير معروف مرفوض.
  - سحب طلب `Pending` من ESS.

#### G-LVA-05 — حوكمة الاعتماد: دور واحد، بلا مدير مباشر، وفصل واجبات شكلي، ورفض بلا ضوابط
- **الدليل:**
  - `HrContractsApprove` = `admin` أو `hr-contracts` ([HrManagementAuthorizationExtensions.cs:126-128](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، وهو دور المنشئ نفسه ([SOD_MATRIX.md:8](SOD_MATRIX.md): maker = checker = `hr-contracts`).
  - المدير المباشر لا دور له في الاعتماد. توجد علاقة «رئيس الوحدة» `OrgUnit.HeadEmployeeId` ([OrgUnit.cs:26-27](../src/Services/HRCore/HR.Core.Domain/Entities/OrgUnit.cs))، ومؤرخة في `OrgUnitVersion`، لكن لا شيء في مسار الإجازة يقرؤها ولا تصل إلى HR.Contracts. مع ذلك يعرّف [mss/manager-capabilities.json:21-27](mss/manager-capabilities.json) القدرة `approval.leave` للمدير، وبطاقة MSS-001 «Done» ([09:249-262](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)).
  - فصل الواجبات الوحيد: المعتمد ≠ `CreatedBy` ([LeaveRequestHandlers.cs:238-248](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
    - طلب ESS يُنشأ في مستهلك بلا `HttpContext`، فـ `CreatedBy` فارغ ([AuditSaveChangesInterceptor.cs:139-142](../src/Shared/HRManagement.ServiceDefaults/AuditSaveChangesInterceptor.cs))، ويسقط الفحص.
    - لا مقارنة بين المعتمد وصاحب الإجازة، فموظف بدور `hr-contracts` يعتمد إجازته التي قدّمها من ESS.
    - القاعدة `R-LEAVE-SELF` تعتمد حقيقة `isSelfApproval` لا يرسلها أحد، ولا تُقيَّم عند الاعتماد أصلاً ([PolicyEngineDomainSeeds.cs:174-177](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)، [PolicyDomainFactsBuilders.cs:25-40](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyDomainFactsBuilders.cs)).
  - `Reject` و`RejectAjax` بلا سياسة صريحة ولا SoD ولا سبب ([LeaveRequestsController.cs:194-235](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveRequestsController.cs)، [LeaveRequestHandlers.cs:370-396](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - أي مستخدم `hr-contracts` يُنشئ الاستحقاق والسياسات ويعدّلها، ولو لنفسه، بلا معتمد ثانٍ ([LeaveBalancesController.cs:89-117](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs)، [LeavePoliciesController.cs:29-102](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeavePoliciesController.cs)). هذه قيم مالية تُصرف عند انتهاء الخدمة.
- **الأثر:** موظف شؤون عاملين يمنح نفسه رصيداً ويعتمد إجازته. ولا يمرّ أي قرار إجازة على المدير المباشر. *(صُحّح بعد التحقق المستقل: علاقة رئيس الوحدة `OrgUnit.HeadEmployeeId` موجودة ومؤرخة، فالنقص في استعمالها للاعتماد لا في النموذج)*
- **الإصلاح المقترح (التنفيذ → WF):**
  - مسار اعتماد بالمحرك: مدير مباشر ثم شؤون العاملين، مع خطوات إضافية بحسب النوع والمدة.
  - فحص على الخادم أن المعتمد ليس صاحب الإجازة (مطالبة `employee_id`).
  - رفض بسبب إلزامي وتحت SoD.
  - Maker-checker على الأرصدة والسياسات.
- **التحقق المطلوب:**
  - موظف `hr-contracts` يعتمد إجازته من ESS ← رفض.
  - رفض بلا سبب ← رفض.
  - تسوية رصيد ذاتية ← رفض.
  - مسار «مدير ثم HR» يكتمل بإعدادات الإنتاج.

#### G-LVA-06 — التداخل والتعارض لا يُفحصان عند الاعتماد ولا مع المسارات الأخرى
- **الدليل:**
  - التداخل يُفحص عند الإنشاء والتعديل، ومع الإجازات **المعتمدة** فقط ([LeaveAndAdvanceRules.cs:20-38, 57-58](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)). ولا يُعاد عند الاعتماد ([LeaveRequestHandlers.cs:229-331](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)). طلبان `Pending` متداخلان يُعتمدان كلاهما ويُخصمان. ولا قيد قاعدة بيانات يمنع التداخل ([ContractsDbContext.cs:122-136](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs)).
  - لا فحص مع المهمات (HR.Training)، ولا مع النقل والانتداب والإعارة، ولا مع الفصل:
    - [FUN_003_EXCEPTION_PATHS.md:7](FUN_003_EXCEPTION_PATHS.md): «تكامل Leave/Separation أعمق خارج هذه البطاقة».
    - الفصل يقرأ الرصيد فقط ([SeparationRecordSupport.cs:31-45](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs))، ولا يلغي الإجازات المستقبلية المعتمدة.
  - لا تحقق أن الموظف موجود أو على رأس عمله. يكفي أن يكون `EmployeeId` غير فارغ ([LeaveRequestHandlers.cs:30](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs))، ولقطة الموظف في العقود بلا حالة ([EmployeeOrgUnitSnapshot.cs:6-11](../src/Shared/HRManagement.SharedKernel/Entities/EmployeeOrgUnitSnapshot.cs)).
- **الأثر:** رصيد يُخصم مرتين. إجازة معتمدة لموظف في مهمة أو منتدب أو مفصول. وتعارض في الحضور والرواتب.
- **الإصلاح المقترح:**
  - إعادة فحص التداخل داخل transaction الاعتماد، مع `Pending`، أو قيد استبعاد `daterange` في PostgreSQL.
  - استعلام تعارض عبر الخدمات (مهمة/حركة/فصل) عند الاعتماد.
  - لقطة حالة الموظف في العقود.
  - الفصل يلغي الإجازات اللاحقة لتاريخه.
- **التحقق المطلوب:**
  - طلبان متداخلان: يُعتمد الأول ويُرفض الثاني.
  - إجازة تتقاطع مع مهمة معتمدة ← رفض.
  - فصل بتاريخ يسبق إجازة معتمدة يلغيها ويعيد رصيدها.

#### G-LVA-07 — الإلغاء بعد الاستهلاك يعيد الرصيد كاملاً ويمحو أياماً مغلقة، ولا قطع ولا مباشرة
- **الدليل:**
  - `Cancel` مسموح لأي إجازة `Approved` أياً كان تاريخها، ويعيد `DurationDays` كاملة ([LeaveRequestHandlers.cs:160-161, 184, 211-226](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - HR.Core يحذف كل سجلات `OnLeave` في نافذة الإجازة، ولو في فترة مغلقة، ولا يعيد الحالة السابقة لليوم ([AttendanceIntegrationService.cs:50-73](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs)). والإجازة المعتمدة تكتب `OnLeave` فوق أي حالة سابقة ([:40-44](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs)).
  - الرواتب لا تعكس خصم الإجازة بدون أجر إذا طُبّق في تشغيل (`AppliedInRunId is null`، [InboundEventHandlers.cs:425-431](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) (→ PAY).
  - لا «قطع إجازة» ولا «مباشرة مبكرة» ولا تمديد. التعديل محصور بـ `Pending` ([LeaveRequestHandlers.cs:119-120](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
- **الأثر:** إلغاء إجازة بعد انتهائها يمنح الموظف أيامها مجاناً. ويمحو حضوراً سبق إغلاقه، ويترك خصماً مالياً لإجازة صارت ملغاة.
- **الإصلاح المقترح:**
  - الإلغاء الكامل قبل البداية فقط.
  - بعد البداية: «قطع» بتاريخ مباشرة يعيد الأيام غير المستهلكة فقط، وينشر حدثاً بالفرق.
  - الأيام في فترة مقفلة تمر بتصحيح G-LVA-03.
  - التمديد طلب مرتبط بالأصل.
- **التحقق المطلوب:**
  - إلغاء إجازة منتهية ← رفض.
  - قطع إجازة 10 أيام في يومها الرابع ← إعادة 6 أيام، وحضور صحيح، وفرق مالي واحد.

#### G-LVA-08 — قيم الإجازة القانونية مثبتة في الشيفرة وغير مربوطة بالنظام القانوني
- **الدليل:**
  - خمسة أنواع فقط ([LeaveType.cs:5-17](../src/Services/HRContracts/HR.Contracts.Domain/Enums/LeaveType.cs)).
  - حدود ثابتة لكل طلب: سنوية 30، مرضية 15، أمومة 90، بدون أجر 60 ([LeaveAndAdvanceRules.cs:179-186](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs))، تُطبَّق عند الاعتماد ([LeaveRequestHandlers.cs:250-251](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - جدول `LeavePolicies` مصدر ثانٍ متعارض: بذرة العرض تضع الأمومة 120 وبدون أجر 90 ([DemoContractsSeeder.cs:289-296](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/DemoContractsSeeder.cs)).
  - `LeavePolicy` فريد لكل (جهة، نوع) ([ContractsDbContext.cs:215-217](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs))، بلا `legal_regime_id` ولا `legal_source_id`.
    - العقد يحمل `LegalRegimeId` ([Contract.cs:52](../src/Services/HRContracts/HR.Contracts.Domain/Entities/Contract.cs)).
    - والأساس السوري ينص: «لا يوجد نموذج أجر أو إجازة واحد؛ يلزم `legal_regime_id` مؤرخ لكل علاقة عمل» ([12-SYRIA-BASELINE-AND-SOURCES.md:20](completion-backlog/12-SYRIA-BASELINE-AND-SOURCES.md)).
  - `IsPaid` و`RequiresMedicalCertificate` لا يقرؤهما أي منطق ([LeavePolicy.cs:18-21](../src/Services/HRContracts/HR.Contracts.Domain/Entities/LeavePolicy.cs)). الرواتب تقرر الخصم بمقارنة النص `"Unpaid"` ([InboundEventHandlers.cs:341](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))، فنوع «أخرى» المضبوط كغير مدفوع يُصرف كاملاً.
  - المرجع القانوني نفسه غير محسوم:
    - [syr/legislation-register.json:26](syr/legislation-register.json) يذكر المادة 65 من القانون 50/2004.
    - [rep/statutory-catalog.json:61](rep/statutory-catalog.json) يذكر المواد 68–72.
    - النص المصدق `PLACEHOLDER_HASH_PENDING_CERTIFIED_COPY`.
- **الأثر:**
  - موظفو القانون 50 والقانون 53 والأنظمة الخاصة يخضعون للقيم نفسها.
  - إجازة مرضية من 16 يوم عمل لا يمكن اعتمادها.
  - تغيير قانوني يحتاج نشر شيفرة.
  - لا يمكن تتبع قرار إجازة إلى نص، وهو شرط التحقق في [legal-source-binding.md:58-64](syr/legal-source-binding.md).
- **الإصلاح المقترح:**
  - كتالوج إجازات مؤرخ لكل نظام قانوني، بالحقول: النوع، مدفوع/جزئي، أيام تقويمية/عمل، الحد لكل طلب وسنة وخدمة، الإشعار، الشهادة، الأثر على القدم. لكل قيمة `legal_source_id`.
  - حذف `GetMaxDuration`.
  - الحدث يحمل فئة الأجر (`PayCategory`) والرواتب تقرأها، لا اسم النوع.
  - لا قيم افتراضية في Production، على نمط PLAN-31.
- **التحقق المطلوب:**
  - موظفان بنظامين قانونيين يحصلان على حدود مختلفة.
  - نوع «أخرى» غير مدفوع يُخصم.
  - تغيير حد بإصدار مؤرخ يسري من تاريخه دون نشر شيفرة، ولا يغيّر القرارات السابقة.

#### G-LVA-09 — احتساب المدة والرصيد خاطئ عند العطل ورأس السنة والإشعار
- **الدليل:**
  - الجمعة والسبت ثابتان، والعطل الرسمية مهملة ([LeaveAndAdvanceRules.cs:167-177](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)). أما الرواتب فتقرأ `NonWorkingDays` من الإعداد ([PayrollPeriodRules.cs:10-23](../src/Services/Payroll/Payroll.Domain/Services/PayrollPeriodRules.cs)) والعطل من جدولها.
  - الإشعار المسبق يُقاس من تاريخ **الاعتماد** لا التقديم ([LeaveRequestHandlers.cs:255](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)، [LeaveAndAdvanceRules.cs:85-90](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)). طلب قُدّم قبل 10 أيام من بدايته واعتُمد بعد 5 أيام يُرفض إذا كان الحد 7.
  - إجازة تعبر رأس السنة تُخصم كلها من رصيد سنة البداية ([LeaveRequestHandlers.cs:258-261](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
  - «اليوم» يُحسب بتوقيت UTC لا `Asia/Damascus` ([LeaveAndAdvanceApprovedConsumer.cs:28, 71](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs)، [LeaveScheduleReconcilerJob.cs:79](../src/Services/HRCore/HR.Core.Infrastructure/Services/LeaveScheduleReconcilerJob.cs))، فتنقلب الحالة متأخرة 3 ساعات.
- **الأثر:** رصيد يُخصم على أيام عطلة رسمية. رفض طلبات سليمة بسبب تأخر المعتمد. ورصيد سنة جديدة لا يُستهلك.
- **الإصلاح المقترح:**
  - خدمة تقويم واحدة (G-LVA-15) يستعملها احتساب المدة.
  - الإشعار يُحسب من تاريخ التقديم.
  - تقسيم الإجازة العابرة على السنتين.
  - التاريخ المحلي بتوقيت دمشق.
- **التحقق المطلوب:**
  - إجازة تشمل عيد العمال لا تخصمه.
  - طلب مستوفٍ للإشعار يُعتمد متأخراً بنجاح.
  - إجازة 28/12–5/1 تُقسم على رصيدَي السنتين.

#### G-LVA-10 — تصحيح الحضور بلا ضوابط، والتعديل قد ينقل السجل إلى موظف آخر
- **الدليل:**
  - قائمة الموظفين في شاشتي الإنشاء والتعديل هي أول 50 موظفاً أبجدياً ([OrgBranchControllers.cs:275-281](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
  - شاشة التعديل بلا خيار فارغ ([Edit.cshtml:9-10](../src/Services/HRCore/HR.Core.Web/Views/DailyAttendanceRecords/Edit.cshtml)). إذا لم يكن صاحب السجل ضمن الخمسين يختار المتصفح أول موظف في القائمة، و`Edit` يكتب `record.EmployeeId = vm.EmployeeId` ([:177](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)). فينتقل السجل بصمت (يحتاج تحقق في المتصفح).
  - لا سبب ولا معتمد ولا مصدر للتصحيح، والحذف حر ([:186-198](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
  - تكرار اليوم في الإنشاء يصطدم بالقيد الفريد ([HRCoreDbContext.cs:392](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)) ويظهر كتعارض عام.
  - `LeaveScheduleReconciler` يعدّ أي سجل `OnLeave` لليوم مصدراً للحقيقة ([LeaveScheduleReconciler.cs:26-56](../src/Services/HRCore/HR.Core.Infrastructure/Services/LeaveScheduleReconciler.cs)):
    - سجل يدوي أو جماعي بحالة «في إجازة» يحوّل الموظف إلى `OnLeave` بلا إجازة.
    - وكل موظف `OnLeave` بلا سجل لليوم يعود `Active`، ومنهم حالات بذرة القبول ([AcceptanceDataSeeder.cs:1169](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs)).
  - أثر الإجازة على الحضور غير ذري: المستهلك يحفظ سجل الحالة بعلامة idempotency ثم يكتب الحضور في `SaveChanges` ثانٍ ([LeaveAndAdvanceApprovedConsumer.cs:46-53](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs)). فشل بينهما ثم إعادة تسليم = علامة موجودة وحضور لم يُكتب.
- **الأثر:** غياب أو حضور يُنسب إلى موظف آخر، ويصل أثره إلى راتبه. وحالة توظيف خاطئة تؤثر في التقارير والحركات.
- **الإصلاح المقترح:**
  - منتقي موظفين بحث من الخادم.
  - `EmployeeId` و`AttendanceDate` غير قابلين للتعديل.
  - سبب إلزامي وسجل تصحيح.
  - `OnLeave` لا يُكتب إلا من إجازة معتمدة وبمرجعها.
  - `LeaveScheduleReconciler` يقرأ الإجازات المعتمدة لا السجلات.
  - كتابة الحالة والحضور في `SaveChanges` واحد.
  - **الخطة:** [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md) (نطاق `DailyAttendanceRecords.Create` على `vm.EmployeeId` يأتي من PLAN-52)
- **التحقق المطلوب:**
  - تعديل سجل الموظف رقم 80 يحفظ صاحبه.
  - إدخال `OnLeave` يدوي ← رفض.
  - إعادة تسليم `ILeaveApproved` بعد فشل جزئي تكمل الحضور.

#### G-LVA-11 — المهمات: اعتماد بلا ضوابط، وبدل قد يتكرر، ولا أثر على الحضور والإجازة
- **الدليل:**
  - `MissionFilesController` بلا أي `[Authorize]` (فلتر الوحدة فقط)، والاعتماد بلا SoD ([TrainingExtendedControllers.cs:398-421](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
  - الحالة تُحفظ أولاً ثم يُنشر الحدث في `SaveChanges` ثانٍ ([:405-418](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
  - `MissionApprovedConsumer` بلا `ShouldSkipAsync` ولا علامة ([PayrollEventConsumers.cs:296-303](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)). والمعالج يضيف تسوية بالمبلغ في كل مرة ([InboundEventHandlers.cs:541-569](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). إعادة التسليم (3 محاولات افتراضياً) تكرر البدل. والمبلغ ثابت من الإعداد، لا بحسب المدة (→ PAY).
  - لا حالة «مهمة» في الحضور ([DailyAttendanceStatus.cs:5-15](../src/Services/HRCore/HR.Core.Domain/Enums/DailyAttendanceStatus.cs))، ولا فحص تداخل مع الإجازة. المهمة مفتوحة النهاية تُرسل `DateTime.MaxValue` ([:414](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
- **الأثر:** بدل مهمة يُصرف مرتين. موظف في مهمة يظهر غائباً أو يأخذ إجازة في الفترة نفسها. ومنشئ المهمة يعتمدها بنفسه.
- **الإصلاح المقترح:**
  - سياسة كتابة واعتماد صريحة، وSoD.
  - حفظ الحالة والحدث في `SaveChanges` واحد.
  - مستهلك idempotent بمرجع المهمة.
  - حالة «مهمة» في الحضور تُكتب من الحدث.
  - فحص تداخل مع الإجازة.
  - الإلغاء يعكس الأثر.
- **التحقق المطلوب:**
  - إعادة تسليم `IMissionApproved` لا تكرر البدل.
  - إجازة متداخلة مع مهمة ← رفض.
  - أيام المهمة تظهر «مهمة» في الحضور.

### 4.3 قدرات ناقصة (P2)

#### G-LVA-12 — أنواع الإجازات والاستحقاق القانوني ناقصة
- **الدليل:**
  - لا حج ولا دراسية ولا طارئة ولا وفاة ولا زواج ولا مرافقة ولا أبوة ([LeaveType.cs:5-17](../src/Services/HRContracts/HR.Contracts.Domain/Enums/LeaveType.cs)).
  - لا استحقاق بحسب سنوات الخدمة، ولا ترحيل وسقوط، ولا تناسب للمعيَّن حديثاً (G-LVA-02).
  - لا أثر للإجازة بدون أجر على القدم أو الترفيع: لا مرجع لـ `Unpaid` خارج الرواتب.
  - لا مرفقات للطلب ([LeaveRequest.cs](../src/Services/HRContracts/HR.Contracts.Domain/Entities/LeaveRequest.cs)).
  - رصيد نهاية الخدمة = استحقاق السنة كاملاً ناقص المستخدم ([SeparationRecordSupport.cs:31-45](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs))، بلا تناسب مع تاريخ الانتهاء ولا رصيد مرحَّل (الصرف → SEP).
- **الأثر:** معاملات إجازة يومية في جهة حكومية تُسجَّل خارج النظام أو كـ «أخرى» بلا قاعدة. والتعويض عند انتهاء الخدمة يُحسب على أساس غير صحيح.
- **الإصلاح المقترح:** من الكتالوج (G-LVA-08): الأنواع، والاستحقاق بالقدم، والترحيل والسقوط، وحدث «فترة بلا أجر» إلى HR.Core لحساب القدم، ومرفقات عبر File.Storage، ورصيد متناسب يُسلَّم إلى SEP.
- **التحقق المطلوب:**
  - إجازة حج مرة واحدة في الخدمة.
  - ترحيل الرصيد غير المستهلك بحدّه، والفائض يسقط.
  - 60 يوماً بدون أجر تؤخر تاريخ استحقاق الترفيع بقدرها.

#### G-LVA-13 — الإجازة المرضية غير مربوطة باللجان الطبية
- **الدليل:**
  - توصيات HR.Health لا تشمل «إجازة مرضية» ولا مدة ([MedicalRecommendationType.cs:5-17](../src/Services/HRHealth/HR.Health.Domain/Enums/MedicalRecommendationType.cs)).
  - لا مستهلك في HR.Contracts لأحداث HR.Health. مستهلكو `IMedicalRecommendationIssued` هم Compliance والأرشيف والرواتب فقط.
  - `RequiresMedicalCertificate` غير مقروء (G-LVA-08).
  - `WorkplaceAccident.RelatedSickLeaveDays` رقم حر لا ينشئ إجازة ([WorkplaceAccident.cs:34-36](../src/Services/HRHealth/HR.Health.Domain/Entities/WorkplaceAccident.cs)).
  - المتطلبات تذكر «إجازات مرضية موثقة» ([Requariments.md:2058](Requariments.md)).
- **الأثر:** الإجازة المرضية الطويلة لا تمر على اللجنة الطبية، ولا يُحفظ إثباتها.
- **الإصلاح المقترح:**
  - توصية «إجازة مرضية» بمدة من اللجنة، تُنشئ أو تعتمد طلباً في العقود.
  - شهادة مرفقة إلزامية عند تجاوز حدّ يحدده القرار D-LVA-7.
- **التحقق المطلوب:**
  - تقرير لجنة بـ 30 يوماً يُنشئ إجازة مرضية مرتبطة به.
  - إجازة مرضية فوق الحد بلا تقرير ← رفض.

#### G-LVA-14 — وقت العمل: الورديات والجداول والإضافي والأجهزة غير موصولة
- **الدليل:**
  - `Shift` و`ShiftRoster` و`ShiftAssignment` لا يقرؤها الحضور ولا الرواتب. المرجع الوحيد إليها خارج متحكمها هو بذرة العرض.
  - لا توقيت دخول وخروج، ولا تأخير، ولا خروج مبكر، ولا استيراد أجهزة: لا `ClockIn` ولا `SourceDevice` في الشيفرة التشغيلية.
  - الإضافي رقم حر في الإسناد ([ShiftAssignment.cs:21-23](../src/Services/HRCore/HR.Core.Domain/Entities/ShiftAssignment.cs))، بلا اعتماد.
  - «البدل» محسوب للعرض فقط، بأجر ساعة ثابت 100 ونسب ثابتة 25% و10% و1.5× ([RostersController.cs:14, 90-99](../src/Services/HRCore/HR.Core.Web/Controllers/RostersController.cs)، [ShiftAllowanceRules.cs:12-18](../src/Services/HRCore/HR.Core.Domain/Services/ShiftAllowanceRules.cs)).
  - لا قيد يمنع إسنادين لنفس الموظف واليوم ([HRCoreDbContext.cs:619](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)). ورمز الوردية فريد على مستوى النظام كله لا لكل جهة ([:593](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
  - الجداول بلا نطاق فرع، وقائمة الموظفين فيها أول 50 ([RostersController.cs:16-45](../src/Services/HRCore/HR.Core.Web/Controllers/RostersController.cs)).
- **الأثر:** لا يمكن إدارة دوام المستشفيات والخدمات المستمرة. لا ضبط للتأخير، ولا صرف للإضافي. وأرقام «البدل» المعروضة مضللة.
- **الإصلاح المقترح (بقرار D-LVA-8):**
  - الوردية تحدد اليوم المتوقع للحضور.
  - استيراد حركات idempotent بمفتاح جهاز/موظف/وقت.
  - قواعد تأخير من الكتالوج.
  - طلب إضافي باعتماد يُسلَّم إلى PAY.
  - حذف الأجر الثابت من الواجهة.
- **التحقق المطلوب:**
  - ملف أجهزة مكرر لا يكرر الحركات.
  - وردية تعبر منتصف الليل تُحسب ليوم البداية.
  - إضافي معتمد يصل إلى الرواتب مرة واحدة.

#### G-LVA-15 — لا تقويم رسمي مركزي
- **الدليل:**
  - العطل موجودة في الرواتب فقط: 4 عطل ثابتة تُبذر لكل نظام ([PayrollRegimeSeed.cs:56-73](../src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs)، [Program.cs:200-205](../src/Services/Payroll/Payroll.Web/Program.cs)).
  - في المقابل يعرّف [holiday-calendar-policy.json:7-22](syr/holiday-calendar-policy.json) سبع عطل ثابتة وعطلاً متغيرة، ويمنع القائمة الثابتة (`forbidHardcodedHolidayList: true`).
  - لا تقويم لكل جهة أو موقع، ولا تقويم لجهات 24×7. والإجازة والحضور لا يقرآن العطل.
  - بطاقة SYR-014 «Done» ([13-SYRIA-PRODUCTION-CARDS.md:169-180](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md))، واختباراتها تقرأ ملف JSON فقط ([Syr014CalendarTests.cs:21-84](../src/Tests/HRManagement.UnitTests/Syr014CalendarTests.cs)).
- **الأثر:** يوم الشهداء ويوم الجيش والأعياد المتغيرة يُخصم من الرصيد ويُعدّ غياباً. والإعلان المتأخر عن عيد لا يُطبَّق.
- **الإصلاح المقترح:**
  - خدمة تقويم مؤرخة واحدة (مثلاً في HR.Core) بعطل ثابتة ومتغيرة بقرار موقّع، وبتقاويم جهة وموقع وجهات 24×7.
  - تستهلكها الإجازة والحضور والرواتب.
  - الإعلان بأثر رجعي يولّد إعادة احتساب (→ PAY).
- **التحقق المطلوب:**
  - إعلان عيد الفطر متأخراً يعيد الأيام إلى الأرصدة ويصحح الحضور قبل اعتماد الراتب.
  - مستشفى 24×7 لا يُغلق في العطلة.

### 4.4 فجوة التحقق والتوثيق

#### G-LVA-16 — ادعاءات «Done» بلا تشغيل ولا أدلة قبول (P1)
- **الدليل:**
  - [HCM_002_ATTENDANCE.md:75-89](HCM_002_ATTENDANCE.md) يعلّم كل معايير القبول `[x]` على `SharedKernel.Time.AttendanceRecord` و`ShiftDefinition`. لا مستهلك لهما في التشغيل: لا `using HRManagement.SharedKernel.Time` خارج الملفين ([AttendanceRecord.cs](../src/Shared/HRManagement.SharedKernel/Time/AttendanceRecord.cs)).
  - `Hcm002AttendanceTests` انعكاس ووجود ملفات ([Hcm002AttendanceTests.cs:23-174](../src/Tests/HRManagement.UnitTests/Hcm002AttendanceTests.cs))، و`scripts/hcm002-validate.ps1:22-23` يفحص الملفين نفسيهما. ومع ذلك HCM-002 «Done» ([09:103-116](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)).
  - [DATA_INVARIANTS_INVENTORY.md:28](DATA_INVARIANTS_INVENTORY.md) يحمي INV-09 بـ «xmin + حالة»، وهو تحديداً ما يكسر الاعتماد (G-LVA-01). و[QA_RISK_TO_TEST_MATRIX.md:14-15](QA_RISK_TO_TEST_MATRIX.md) «Covered» باختبارات SQLite.
  - [QA_E2E_JOURNEY_MATRIX.md:10](QA_E2E_JOURNEY_MATRIX.md) يعد بـ `LeaveJourneyE2ETests` و`LEV-F3` «عبر-نطاق».
    - الموجود `PayrollLeaveTransferFilesRoleJourneyE2ETests` يفحص الوصول إلى الصفحة، وأن اسمَي شخصيتين مختلفان ([PayrollLeaveTransferFilesRoleJourneyE2ETests.cs:73-89](../src/Tests/HRManagement.E2E/PayrollLeaveTransferFilesRoleJourneyE2ETests.cs)).
    - والشخصية `checker-mgmt` لا تملك أصلاً صلاحية اعتماد الإجازة.
  - [prod-ess/employee-selfservice-matrix.json:62-63](prod-ess/employee-selfservice-matrix.json) يقول «FUN-002 Attendance Done» و«FUN-003 Leave Done»، بينما FUN-002 هو الفصل وFUN-003 هو التنقلات. وملف السيناريو `testdata/prod003/scenarios/leave-request-pass.json` سطر واحد بلا بيانات.
  - لا خطة قبول ولا مجلد `acceptance-evidence` للإجازة أو الحضور. ولا بيانات إجازة في بذرة القبول: عدد الطلبات من الكتالوج يُستعمل في بذرة العرض فقط ([DemoContractsSeeder.cs:367](../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/DemoContractsSeeder.cs)، [Program.cs:101-106](../src/Services/HRContracts/HR.Contracts.Web/Program.cs)).
  - نطاق v1 متناقض: [RELEASE_PRODUCT_SCOPE.md:28-29](RELEASE_PRODUCT_SCOPE.md) يقول إن الحضور غير متكامل، وإن ESS «مشروع خارجي». لكن `EssPortalController` ينشر `ILeaveRequested`، والإطلاق التجريبي يشترط إغلاق الحضور ([PILOT_DEPLOYMENT.md:21](PILOT_DEPLOYMENT.md)).
- **الأثر:** القرار بالإطلاق يُبنى على حالة غير حقيقية. G-LVA-01 كان سيظهر في أول اختبار قبول على PostgreSQL.
- **الإصلاح المقترح:**
  - خطط قبول AC-LVA-* ببيانات بذرة قبول حتمية: أرصدة وطلبات بكل الحالات وفترة حضور.
  - التشغيل على PostgreSQL تحت `ACCEPTANCE_REQUIRE_ALL=true`.
  - حذف `SharedKernel.Time` أو ربطه، على نمط PLAN-33.
  - تصحيح الوثائق المذكورة.
- **التحقق المطلوب:** مجلد أدلة لكل خطة فيه العناصر الثمانية، ولا اختبار ينجح بلا بيانات.

### 4.5 تحسينات (P3)

#### G-LVA-17 — بنود صغيرة
- **الدليل:**
  - `DailyAttendanceRecords.Details` موجود ([OrgBranchControllers.cs:107-113](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)) بلا `Details.cshtml`.
  - قائمة الوحدات في الإغلاق والإدخال الجماعي أول 50 وحدة ([:200-209, 251-260](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
  - حذف سياسة نوعٍ ما يسقط فحص الإشعار لذلك النوع بصمت ([LeavePoliciesController.cs:92-102](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeavePoliciesController.cs)، [LeaveAndAdvanceRules.cs:82-83](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)).
- **الإصلاح المقترح:** إضافة الشاشة أو إزالة الإجراء، ومنتقي وحدات بالبحث، وتعطيل السياسة بدل حذفها.
- **الخطة:** [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md) (جزئياً: شاشة تفاصيل سجل الحضور، ومنتقي الموظفين بالبحث). ومنتقي الوحدات وتعطيل سياسة الإجازة: الموجة 2
- **التحقق المطلوب:**
  - فتح تفاصيل سجل حضور لا يعيد 500.
  - تعطيل السياسة يُبقي القرارات السابقة قابلة للتتبع.

### 4.6 تقاطعات مع الدراسات الأخرى

- **→ IAM:** إجراءات POST بلا سياسة صريحة، وحمايتها من البوابة وحدها (`HrContractsApprove`، [GatewayPathAuthorizationMiddleware.cs:222-226](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). فلتر الوحدة يقبل `hr-management-approver` عند الوصول المباشر إلى الخدمة ([HrManagementAuthorizationExtensions.cs:28-31](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). الإجراءات: `LeaveRequests.Create/Edit/Delete/Reject/RejectAjax` ([LeaveRequestsController.cs:82-98, 121-141, 162-172, 194-235](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveRequestsController.cs))، و`LeavePolicies.*` ([LeavePoliciesController.cs:29-102](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeavePoliciesController.cs))، و`LeaveBalances.Create/InitializeYear` ([LeaveBalancesController.cs:89-162](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs)).
- **→ IAM:** طلب الإجازة المنشأ من HR بلا `OrgUnitId` ([LeaveRequestHandlers.cs:72-83](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)). فالقائمة المقيدة بالنطاق تُسقطه، و`CanAccess(null)` يرفضه ([HrOrgScopeVisibility.cs:60-73](../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs)).
- **→ IAM:** `LeaveBalances.Create` لا يفحص نطاق الموظف ([LeaveBalancesController.cs:91-117](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs)). وتصدير الإجازات بلا نطاق ([ExportController.cs:24-40](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ExportController.cs)).
- **→ IAM:** `DailyAttendanceRecords.Create` لا يفحص أن `vm.EmployeeId` ضمن نطاق مدير الفرع، و`Edit` يسمح بنقل السجل إلى موظف خارجه ([OrgBranchControllers.cs:128-145, 177](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)). والجداول بلا نطاق أصلاً ([RostersController.cs:26-155](../src/Services/HRCore/HR.Core.Web/Controllers/RostersController.cs)). القائمة المجمّدة في [hrcore-post-policy-allowlist.txt:21-25, 40-46](../tests/architecture/hrcore-post-policy-allowlist.txt).
- **→ IAM:** `MissionFilesController` بلا أي سياسة على POST ([TrainingExtendedControllers.cs:339-448](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
- **→ POL:** القاعدة `R-LEAVE-SELF` تحتاج حقيقة `isSelfApproval` غير مرسلة. و`R-LEAVE-MAX` (رفض أكثر من 30 يوماً لكل الأنواع) تمنع الأمومة في وضع Enforce. والسياسة تُقيَّم عند إنشاء HR فقط، لا في ESS ولا عند الاعتماد. والمجال `Contracts.Leave` غير مضبوط فهو `Off` ([PolicyEngineDomainSeeds.cs:23-29, 174-182](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)، [LeaveRequestHandlers.cs:64-68](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)).
- **→ WF:** اعتماد الإجازة والمهمة وتصحيح الحضور محلي بلا تعريف مسار. ولا علاقة مدير مباشر في `Employee` تسمح بتوجيه خطوة المدير ([mss/manager-capabilities.json:21-27](mss/manager-capabilities.json)).
- **→ QA:** نمط «`ExecuteUpdate` ثم تعديل كيان متتبَّع بـ `xmin` ثم `SaveChanges`» لا تكشفه اختبارات SQLite ([ConcurrencyExtensions.cs:21-22](../src/Shared/HRManagement.ServiceDefaults/ConcurrencyExtensions.cs)). يستحق فحصاً في بقية مستخدمي `ExecuteUpdate`: `TransferRequestsController`، `HireCaseHandlers`، `InventoryItemsController`. وكذلك اختبارات الانعكاس `Hcm002AttendanceTests` و`Syr014CalendarTests`.
- **→ PAY:**
  - إجازة بدون أجر عابرة لشهرين تُحمَل كخصم واحد بكامل أيامها في أول تشغيل يتقاطع معها ([PayrollPartitionInputLoader.cs:158-162](../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)).
  - المقسوم 22 أو 30 يُطبَّق على أيام قد تكون تقويمية ([InboundEventHandlers.cs:357-367](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - الإلغاء بعد التطبيق لا يُعكس ([:430](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - الغياب لفترة رواتب مُرحَّلة لا يُطبَّق ([CorrectionRunHandlers.cs:90](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
  - بدل المهمة ثابت لا بحسب الأيام ([InboundEventHandlers.cs:548](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- **→ SEP:** رصيد الإجازة السنوية عند انتهاء الخدمة = استحقاق السنة كاملاً − المستخدم، بلا تناسب ولا ترحيل ([SeparationRecordSupport.cs:31-45](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs)).
- **→ OPS:** لا جدولة لتهيئة الأرصدة السنوية، ومهمة `LeaveScheduleReconcilerJob` تعمل بالتاريخ UTC ([LeaveScheduleReconcilerJob.cs:79](../src/Services/HRCore/HR.Core.Infrastructure/Services/LeaveScheduleReconcilerJob.cs)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-LVA-1 | كتالوج الإجازات لكل نظام قانوني: الأنواع، والمدد، والأجر الكامل أو الجزئي، والأيام التقويمية أم أيام العمل لكل نوع، والاستحقاق بالقدم، والترحيل والسقوط، والإشعار. المرجع غير محسوم بين المادة 65 والمواد 68–72 من القانون 50/2004، والنص المصدق غير متاح. | تسليم جدول مصدَّق لكل `legal_regime_id` مع `legal_source_id`. لا قيم افتراضية في الإنتاج، والنظام يرفض النوع الذي لا قاعدة له. | LVA-B، LVA-D |
| D-LVA-2 | سلسلة اعتماد الإجازة | مدير مباشر ثم شؤون العاملين عبر محرك المسارات. خطوة ثالثة للإجازة الطويلة أو بدون أجر حسب الكتالوج. والمعتمد ليس صاحب الإجازة. | LVA-E، WF |
| D-LVA-3 | هل الحضور وإغلاقه المرتبط بالرواتب ضمن v1 والإطلاق التجريبي؟ (تناقض RELEASE_PRODUCT_SCOPE مع PILOT_DEPLOYMENT) | نعم، بحد أدنى: حالة يومية من الفرع، وإغلاق شهري مقفل، وتصحيح بفرق. استيراد الأجهزة خارج v1. | LVA-C |
| D-LVA-4 | من يُدخل الحضور ومن يعتمد التصحيح؟ | مدير الفرع يُدخل لوحدته وما تحتها. التصحيح بعد الإغلاق يعتمده مسؤول شؤون العاملين المركزي (فصل واجبات). | LVA-C، IAM |
| D-LVA-5 | الإلغاء بعد بدء الإجازة | لا إلغاء بعد البداية. «قطع» بتاريخ مباشرة يعيد الأيام غير المستهلكة فقط، والتمديد طلب مرتبط بالأصل. | LVA-E |
| D-LVA-6 | توفير الأرصدة: تلقائي عند التعيين وبداية السنة؟ ومصدر الأرصدة الافتتاحية؟ | تلقائي ومتناسب. الأرصدة الافتتاحية من ملف ترحيل معتمد مرة واحدة لكل جهة. | LVA-B |
| D-LVA-7 | حد الإجازة المرضية الذي يستلزم لجنة طبية | يُحدد من النص. اللجنة في HR.Health تصدر الإجازة المرضية الطويلة، والقصيرة بشهادة مرفقة. | LVA-G |
| D-LVA-8 | الإضافي والورديات والتوقيت ضمن v1؟ | الورديات والإضافي باعتماد في v1 لجهات 24×7 فقط إن وُجد نص يحدد الأجر. والتأخير والأجهزة في v2. | LVA-H |

---

## 6. خارطة التنفيذ المقترحة

**ترقيم الخطط (2026-10-30):** في الموجة 1 من البرنامج خطة واحدة من هذه الدراسة: **LVA-C ← [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md)**، وجانب الرواتب منها منسَّق مع G-PAY-19 (جزء الحضور). وعائق الإنتاج G-LVA-18 يملكه [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) خارج هذا التقدير. بقية الخطط (LVA-A وB وD–I) **الموجة 2**، وتبقى برموزها الحرفية.

```text
موجة البرنامج 1   LVA-C = PLAN-57 (فترة الحضور والقفل والتسليم للرواتب)   ← يعتمد على PLAN-56؛ ويستهلكه PLAN-59
                  (G-LVA-18 ← PLAN-37، خارج هذه الدراسة)
الموجة 2 (P0)     LVA-A (سلامة الاعتماد على PostgreSQL)  ║  LVA-B (توفير الأرصدة وتشغيلها)
الموجة 2 (P1)     LVA-D (الكتالوج القانوني والاحتساب)  ║  LVA-E (الحوكمة والتداخل والقطع)  ║  LVA-F (ESS)  ║  LVA-G (المهمات والطبي والتقويم)
الموجة 2 (P2)     LVA-H (وقت العمل، بقرار D-LVA-8)
مستمر             LVA-I (القبول الحي والتوثيق) — الموجة 2؛ والتحقق الحي لـ PLAN-57 في PLAN-65
```

| الخطة | رقم الخطة / الموجة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|-------------------|---------|---------|----------|-----------|-------------------|
| LVA-A | الموجة 2 | اعتماد وإلغاء ذريان على PostgreSQL | G-LVA-01 | P0 | PLAN-37 (G-LVA-18) | 4–5 |
| LVA-B | الموجة 2 | توفير الأرصدة وتشغيلها بحجم جهة | G-LVA-02، جزء G-LVA-12 | P0 | LVA-A، D-LVA-6 (والقيم المؤقتة حتى D-LVA-1) | 6–8 |
| LVA-C | **PLAN-57** (الموجة 1) | فترة حضور مقفلة وتصحيح بفرق وتسليم للرواتب | G-LVA-03، G-LVA-10، جزء الحضور من G-PAY-19 | P0 | PLAN-56 (وPLAN-52 لنطاق الإنشاء) | 9–10 |
| LVA-D | الموجة 2 | كتالوج الإجازات القانوني والاحتساب | G-LVA-08، G-LVA-09، جزء G-LVA-12 | P1 | D-LVA-1، LVA-B | 7–9 |
| LVA-E | الموجة 2 | حوكمة الاعتماد والتداخل والقطع | G-LVA-05، G-LVA-06، G-LVA-07 | P1 | LVA-A، D-LVA-2، D-LVA-5، WF | 8–10 |
| LVA-F | الموجة 2 | الإجازة في الخدمة الذاتية | G-LVA-04 | P1 | LVA-A، LVA-D | 5–6 |
| LVA-G | الموجة 2 | المهمات والإجازة المرضية والتقويم الرسمي | G-LVA-11، G-LVA-13، G-LVA-15 | P1/P2 | PLAN-57، D-LVA-7 | 8–10 |
| LVA-H | الموجة 2 | الورديات والتوقيت والإضافي والأجهزة | G-LVA-14 | P2 (D-LVA-8) | PLAN-57، LVA-G | 9–10 |
| LVA-I | الموجة 2 | القبول الحي وتصحيح التوثيق | G-LVA-16، G-LVA-17 | P1→مستمر | كل الخطط | 5–6 |
| **المجموع** | | | | | | **≈ 61–74** |

**LVA-A — اعتماد وإلغاء ذريان على PostgreSQL.**
- Transaction صريحة في `ApproveLeaveRequestHandler` و`CancelLeaveRequestHandler`، تضم الحجز والخصم والمعاملة والـ outbox، دون تعديل كيان متتبَّع بعد `ExecuteUpdate`.
- جدول حركة رصيد مدقَّق.
- إزالة البحث الاحتياطي عن معاملة `LeaveAdvance`.
- سكربت إصلاح للطلبات العالقة.
- اختبار Testcontainers للمعالجين.
- سيناريوهات القبول:
  - AC-LVA-Aa: اعتماد على PostgreSQL ← حدث في HR.Core والرواتب.
  - AC-LVA-Ab: فشل محقون بعد الخصم ← لا تغيير.
  - AC-LVA-Ac: إلغاء على PostgreSQL ← `ILeaveCancelled` وإعادة رصيد في commit واحد.
  - AC-LVA-Ad: سكربت الإصلاح يكمل طلباً عالقاً.

**LVA-B — توفير الأرصدة وتشغيلها.**
- خدمة استحقاق عند التعيين وبداية السنة، idempotent ومتناسبة.
- تسوية مدققة بمعتمد ثانٍ، واستيراد أرصدة افتتاحية.
- أنواع بلا رصيد تُقاس بالقاعدة.
- ترقيم وترشيح على الخادم، وصندوق «بانتظار اعتمادي».
- سيناريوهات القبول:
  - AC-LVA-Ba: تهيئة سنة لـ 500 موظف بلا تكرار.
  - AC-LVA-Bb: تناسب المعيَّن حديثاً.
  - AC-LVA-Bc: تسوية بلا سبب أو ذاتية ← رفض.
  - AC-LVA-Bd: الطلب رقم 150 يظهر في الصندوق.
  - AC-LVA-Be: إجازة مرضية تُعتمد بلا سجل رصيد ضمن حد القاعدة.

**LVA-C — فترة حضور مقفلة (= [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md)، الموجة 1؛ التفاصيل والعقد مع الرواتب في الخطة).**
- كيان `AttendancePeriod` بحالاته، ورفض الكتابة على الأيام المقفلة.
- تصحيح بسبب ومعتمد ينتج delta، وملخص بنسخة.
- `EmployeeId` و`AttendanceDate` غير قابلين للتعديل، ومنتقي بحث.
- منع `OnLeave` اليدوي، والـ reconciler يقرأ الإجازات.
- إدخال جماعي على أيام العمل، وشمول الوحدات الفرعية.
- تحذير في الرواتب عن الفترات غير المغلقة، بالتنسيق مع PAY للحساب.
- سيناريوهات القبول:
  - AC-LVA-Ca: تعديل يوم مقفل ← رفض.
  - AC-LVA-Cb: تصحيح معتمد ← delta واحد يُطبَّق مرة.
  - AC-LVA-Cc: إغلاق مكرر لا يكرر، والعودة إلى صفر تلغي الخصم.
  - AC-LVA-Cd: الإدخال الجماعي لا يكتب الجمعة والسبت والعطل.
  - AC-LVA-Ce: وحدة أب تشمل الأبناء.
  - AC-LVA-Cf: تعديل سجل الموظف رقم 80 يحفظ صاحبه.

**LVA-D — الكتالوج القانوني والاحتساب.**
- `LeaveRule` مؤرخ لكل `legal_regime_id` بـ `legal_source_id`، وحذف `GetMaxDuration`.
- `PayCategory` في الحدث.
- الإشعار من تاريخ التقديم، وتقسيم الإجازة على السنوات.
- أيام تقويمية أو أيام عمل لكل نوع، مع التقويم.
- أنواع D-LVA-1، والترحيل والسقوط، وحدث «فترة بلا أجر» إلى HR.Core.
- سيناريوهات القبول:
  - AC-LVA-Da: نظامان قانونيان بحدود مختلفة.
  - AC-LVA-Db: «أخرى» غير مدفوعة تُخصم.
  - AC-LVA-Dc: إصدار قاعدة مؤرخ لا يغير الماضي.
  - AC-LVA-Dd: إجازة عابرة للسنة تُقسم.
  - AC-LVA-De: الترحيل بحدّه.

**LVA-E — الحوكمة والتداخل والقطع.**
- مسار الإجازة بالمحرك (مدير ثم HR) بالتنسيق مع WF.
- المعتمد ليس صاحب الإجازة، ورفض بسبب.
- Maker-checker على السياسات.
- إعادة فحص التداخل في الاعتماد، وقيد `daterange`.
- استعلام تعارض مع المهمات والحركات والفصل.
- القطع والتمديد.
- سيناريوهات القبول:
  - AC-LVA-Ea: اعتماد ذاتي من ESS ← رفض.
  - AC-LVA-Eb: رفض بلا سبب ← رفض.
  - AC-LVA-Ec: طلبان متداخلان ← يُعتمد واحد.
  - AC-LVA-Ed: إجازة على مهمة معتمدة ← رفض.
  - AC-LVA-Ee: قطع في اليوم الرابع ← 6 أيام تعود وفرق مالي واحد.
  - AC-LVA-Ef: الفصل يلغي الإجازات اللاحقة.

**LVA-F — الإجازة في الخدمة الذاتية.**
- المدة تُحسب في العقود، والتحقق الموحد، ورفض النوع غير المعروف.
- حدث النتيجة إلى الموظف.
- شاشة «إجازاتي»: الرصيد والحالة والسحب.
- سيناريوهات القبول:
  - AC-LVA-Fa: أسبوع = 5 أيام عمل.
  - AC-LVA-Fb: طلب متداخل يُرفض بسبب ظاهر للموظف.
  - AC-LVA-Fc: «طارئة» إما نوع في الكتالوج أو مرفوضة.
  - AC-LVA-Fd: سحب طلب `Pending`.
  - AC-LVA-Fe: موظف لا يرى طلب غيره.

**LVA-G — المهمات والطبي والتقويم.**
- سياسة وSoD على المهمات، ونشر ذري، ومستهلك idempotent.
- حالة «مهمة» في الحضور، وتداخل مع الإجازة.
- توصية «إجازة مرضية» من اللجنة إلى العقود، وشهادة مرفقة.
- خدمة تقويم مؤرخة تستهلكها الإجازة والحضور والرواتب.
- سيناريوهات القبول:
  - AC-LVA-Ga: إعادة تسليم المهمة لا تكرر البدل.
  - AC-LVA-Gb: أيام المهمة «مهمة» في الحضور.
  - AC-LVA-Gc: تقرير لجنة ← إجازة مرضية مرتبطة.
  - AC-LVA-Gd: إعلان عيد متأخر يصحح الأرصدة والحضور قبل اعتماد الراتب.
  - AC-LVA-Ge: جهة 24×7 لا تُغلق.

**LVA-H — وقت العمل (بقرار D-LVA-8).**
- الوردية تحدد الحضور المتوقع.
- استيراد حركات idempotent، وقواعد تأخير من الكتالوج.
- طلب إضافي باعتماد يُسلَّم إلى PAY.
- قيد إسناد واحد لكل موظف ويوم، ورمز وردية لكل جهة.
- حذف الأجر الثابت من شاشة الجداول.
- سيناريوهات القبول:
  - AC-LVA-Ha: ملف أجهزة مكرر لا يكرر.
  - AC-LVA-Hb: وردية تعبر منتصف الليل.
  - AC-LVA-Hc: إضافي معتمد يصل مرة واحدة.
  - AC-LVA-Hd: إسناد ثانٍ لنفس اليوم مرفوض.

**LVA-I — القبول الحي والتوثيق.**
- بذرة قبول حتمية للإجازة والحضور: أرصدة، وطلبات بكل الحالات، وفترة.
- `[RequiredAcceptance]` لسيناريوهات AC-LVA-*.
- رحلة E2E شبيهة بالإنتاج: ESS ← مدير ← HR ← حضور ← إغلاق ← رواتب.
- حذف `SharedKernel.Time` أو ربطه.
- تصحيح الوثائق: HCM_002، والبطاقات HCM-002 وSYR-014 وESS-001 وMSS-001، وINV-09، ومصفوفتا QA، وتناقض RELEASE_PRODUCT_SCOPE مع PILOT.
- سيناريوهات القبول:
  - AC-LVA-Ia: الرحلة الشبيهة بالإنتاج ناجحة.
  - AC-LVA-Ib: لا اختبار قبول ينجح بلا بيانات.

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين:
- تأكيد G-LVA-01 بالتشغيل.
- توقيت D-LVA-1، إذ يعتمد على نص مصدّق.
- تنسيق LVA-C مع الرواتب.

---

## 7. معيار «اكتمال الإجازات والحضور» المقترح

- [ ] **1.** تُغلق G-LVA-01…G-LVA-11، أو يؤجَّل ما يؤجَّل بقرار موثّق.
- [ ] **2.** تنجح رحلة شبيهة بالإنتاج على PostgreSQL (`IsLabScale=false`):
  - طلب ESS، ثم اعتماد المدير ثم HR، ثم خصم رصيد بأيام العمل حسب التقويم.
  - حضور «في إجازة»، ثم إغلاق فترة مقفلة، ثم تصحيح بفرق.
  - تشغيل رواتب يطبّق غياب الفترة وخصم الإجازة بدون أجر مرة واحدة.
  - قطع إجازة ينتج فرقاً صحيحاً في الرصيد والحضور والراتب.
- [ ] **3.** لا قيمة إجازة ولا عطلة في الشيفرة، ويثبت ذلك اختبار معماري. كل قاعدة إجازة مأخوذة من كتالوج مؤرخ لكل نظام قانوني ولها `legal_source_id`، وقرار إجازة عشوائي يُتتبَّع إلى نصه.
- [ ] **4.** توجد أدلة `acceptance-evidence/AC-LVA-*` لكل سيناريو موجب وسالب، تحت `ACCEPTANCE_REQUIRE_ALL=true` وبيانات بذرة قبول. ولا اختبار للإجازة أو الحضور يعمل على SQLite وحدها لمسار يعتمد على `xmin`.
- [ ] **5.** تُصحَّح الوثائق لتطابق الشيفرة: `HCM_002_ATTENDANCE.md`، وبطاقات 09 و13، و`DATA_INVARIANTS_INVENTORY.md`، و`QA_*`، و`prod-ess`، و`RELEASE_PRODUCT_SCOPE.md`.
