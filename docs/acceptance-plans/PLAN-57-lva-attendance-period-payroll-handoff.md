# PLAN-57 — فترة حضور مقفلة وتصحيح بفرق وتسليم موثوق للرواتب (LVA-C)

**Scope:** LVA — [دراسة الفجوات](../HCM_002_LEAVE_ATTENDANCE_GAP_STUDY.md): G-LVA-03, G-LVA-10؛ وجانب الحضور من G-PAY-19 في [دراسة الرواتب](../PAY_001_GAP_STUDY.md) (بالتنسيق مع كاتبها)  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-56 (آلة حالات التشغيل: `ResultsFrozen`، الإلغاء يحرر المدخلات). ويستهلك: PLAN-37 (ربط الملف الشبيه بالإنتاج، ومنه `payroll-web` و`hr-core-web`)، وPLAN-52 (نطاق الفرع على `DailyAttendanceRecords.Create`)، وPLAN-51 (سياسات صريحة لكل إجراء)  
**Consumed by:** PLAN-59 (إثبات الدورة الكاملة: إغلاق فترة حضور قبل الحساب) — انظر «عقد التسليم» في §4  
**Related cards:** HCM-002 و ESS/MSS في [completion-backlog/09](../completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)، SYR-014 ([13-SYRIA](../completion-backlog/13-SYRIA-PRODUCTION-CARDS.md))، مؤشر الإطلاق التجريبي «إغلاق فترة حضور واحدة مرتبطة بالرواتب» ([PILOT_DEPLOYMENT.md:21](../PILOT_DEPLOYMENT.md))، AC-16 (رسالة مكررة)، AC-18 (لحاق المستهلك)

## Goal

أن يصبح «إغلاق فترة الحضور» إغلاقاً حقيقياً، وأن يصل أثره إلى الرواتب مرة واحدة وبصورة صحيحة:

- فترة حضور محفوظة (شهر تقويمي)، وملخص لكل موظف بنسخة مرقّمة. أيام الموظف في فترة مغلقة **مقفلة** على كل كاتب.
- التعديل بعد الإغلاق تصحيح بسبب ومعتمد مختلف. ينتج نسخة جديدة من الملخص، وتحوّلها الرواتب إلى **فرق** يُطبَّق مرة واحدة، في فترته إن لم تُجمَّد، وإلا في الدورة التالية.
- الغياب يُحسب على أيام العمل فقط. الإدخال الجماعي لا يكتب العطل ولا المستقبل. والإغلاق يشمل الوحدات الفرعية والمنتهية خدمتهم خلال الفترة.
- سجل الحضور لا ينتقل إلى موظف آخر عند التعديل. و«في إجازة» يأتي من الإجازة المعتمدة وحدها.
- الرواتب تعرف قبل الحساب أي الموظفين لم يُغلق حضورهم، وتحذّر أو ترفض بحسب الضبط.
- عقد تسليم مكتوب (حدث + واجهتا قراءة/إغلاق) يبني عليه PLAN-59.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-LVA-3 — هل الحضور وإغلاقه المرتبط بالرواتب ضمن v1؟ (تناقض `RELEASE_PRODUCT_SCOPE.md:28` مع `PILOT_DEPLOYMENT.md:21`) | **نعم** بالحد الأدنى في هذه الخطة. وبوابة الرواتب `Payroll:Attendance:Gate=Warn`، فلا يتوقف صرف جهة لا تستعمل الحضور | يحدد صحة البوابة ونطاق الوثائق |
| D-LVA-4 — من يُدخل الحضور ويطلب التصحيح، ومن يعتمده؟ | الإدخال وطلب التصحيح: `hr-core` في نطاقه، ويشمل مدير الفرع لشجرة وحدته. الاعتماد: `hr-core` **غير مقيد بفرع** (مركزي)، والمعتمد غير الطالب، ويُفحص ذلك في الخادم | سياسة `HrCore.AttendanceCorrectionApprove` |
| D-LVA-9 (جديد) — يوم عمل بلا سجل عند الإغلاق | `Attendance:Close:UnrecordedWorkingDays=RequireComplete`: يُرفض الإغلاق مع قائمة الناقص. البديلان: `CountAsAbsent` و`Ignore` | يمنع غياباً صامتاً أو مفقوداً |
| D-LVA-10 (جديد) — شدة بوابة الرواتب | `Warn` في كل البيئات، والمالك يرفعها إلى `Block` | لا يُكسر قبول PLAN-58 الذي يحسب تشغيلات قبل PLAN-59 |
| D-LVA-11 (جديد) — وحدة الفترة | شهر تقويمي (`Attendance:Periods:Mode=CalendarMonth`). وفترات مخصصة غير متداخلة تبقى خلف الإعداد `Custom` | يطابق فترات تشغيل الرواتب الشهرية |
| D-LVA-12 (جديد) — إجازة معتمدة أو ملغاة تمس يوماً مقفلاً | `Attendance:Corrections:LeaveEventsAfterClose=AutoApply`: تصحيح نظامي معتمد تلقائياً، لأن قرار الإجازة نفسه هو التفويض. البديل `Pending` | الأثر المالي يصل دون تدخل يدوي |
| D-LVA-13 (جديد) — تقويم الأيام إلى أن ينفَّذ التقويم المركزي (LVA-G، الموجة 2) | `Attendance:Calendar:WeeklyRestDays=Friday,Saturday`، و`Attendance:Calendar:Holidays` قائمة تواريخ في الإعداد **فارغة افتراضياً**. صفحة الإغلاق تعرض تحذيراً إذا لم تُضبط عطل للفترة. لا قائمة عطل في الشيفرة | يحترم `forbidHardcodedHolidayList` في `syr/holiday-calendar-policy.json:7` |
| D-LVA-14 (جديد) — أجر يوم الفرق المتأخر | أجر التشغيل الذي يُطبَّق فيه الفرق (`ComputeAbsenceAmount` الحالي). الأجر التاريخي للفترة الأصلية تملكه PAY-H (الموجة 2) | تقريب موثّق، لا يظهر في القبول (راتب ثابت) |

---

## المرحلة 0 — خط الأساس (Baseline) — نصف يوم

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-57-lva-attendance-period-payroll-handoff` | — |
| 0.2 | التأكد من الاعتماديات | PLAN-56 مدموجة (`PayrollRun` بحراس الانتقال والإلغاء المحرِّر)، PLAN-37 (الملف الشبيه بالإنتاج يشغّل `hr-core-web` و`payroll-web` بربط الإنتاج)، PLAN-52 (`DailyAttendanceRecords.Create` يفحص نطاق `vm.EmployeeId`) | سطر لكل اعتمادية في `reports/plan57-baseline.md` مع SHA |
| 0.3 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 errors |
| 0.4 | اتساق الترحيلات | `dotnet ef migrations has-pending-model-changes` لـ HR.Core وPayroll | لا تغييرات معلّقة |
| 0.5 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Attendance\|Payroll\|Architecture"`، ثم IntegrationTests بالمرشح `Attendance\|OutboxAtomicity\|Payroll` | أخضر، أو قائمة إخفاقات موروثة في التقرير |
| 0.6 | جرد البيانات (قراءة فقط) | `scripts/lva057-validate.ps1 -Inventory`: عدد سجلات الحضور على الجمعة والسبت بحسب الحالة، وعدد سجلات `OnLeave` اليدوية، وعدد `AbsenceDeductions` بسبب `AttendancePeriodClosed:%` | الأرقام في التقرير، وتُستعمل لخطة الترحيل في §2 |

---

## In scope

### 1. فترة الحضور وملخص الموظف والقفل (G-LVA-03)

**المشكلة:**
- لا كيان فترة ولا قفل: `DailyAttendanceRecord.cs:7-23`.
- `ClosePeriodAndPublishAsync` ينشر لكل موظف بـ `TransactionId = Guid.NewGuid()` ولا يحفظ شيئاً: `AttendanceIntegrationService.cs:75-124`.
- يشمل الموظفين المباشرين للوحدة وحدهم (`:83`)، ويستبعد كل `Terminated` (`:81`).
- يعدّ الغياب على كل الأيام (`:98-101`).
- لا تحقق من الحدود ولا من التداخل: `OrgBranchControllers.cs:262-273`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `AttendancePeriod` | `src/Services/HRCore/HR.Core.Domain/Entities/AttendancePeriod.cs` | `PeriodStart`، `PeriodEnd`، `Status` (`Open`، `PartiallyClosed`، `Closed`)، `ClosedAt/By`. قيد فريد `(TenantId, PeriodStart)` |
| `AttendancePeriodClosure` | `…/Entities/AttendancePeriodClosure.cs` | سجل كل عملية إغلاق: `AttendancePeriodId`، `OrgUnitId` (`null` = كل الجهة)، `ClosedBy/At`، `EmployeeCount`، `SkippedAlreadyClosed` |
| `AttendanceEmployeeSummary` | `…/Entities/AttendanceEmployeeSummary.cs` | لكل `(AttendancePeriodId, EmployeeId)` صف واحد فريد يحمل `xmin`. الحقول: `Version`، `WorkingDays`، `PresentDays`، `AbsentDays`، `OnLeaveDays`، `RemoteDays`، `UnrecordedWorkingDays`، `LastReason`، `LastCorrectionId`، `PublishedAt` |
| `AttendancePeriodStatus`، `AttendanceRecordSource` | `HR.Core.Domain/Enums/` | المصدر: `Manual=0`، `Bulk=1`، `Leave=2`، `Correction=3` |
| `AttendanceSummaryCalculator` | `HR.Core.Domain/Services/AttendanceSummaryCalculator.cs` | دالة صرفة: (سجلات الموظف، الفترة، التقويم، تاريخ التعيين، تاريخ انتهاء الخدمة) ← الملخص. تُعدّ الحالات على **أيام العمل** فقط |
| `AttendanceCalendarOptions` + `AttendanceCalendar` | `HR.Core.Infrastructure/Services/Attendance/` | `Attendance:Calendar:WeeklyRestDays`، `Attendance:Calendar:Holidays` (D-LVA-13)، و`IsWorkingDay(date)` |
| `AttendanceLockService` | ↑ | `IsLockedAsync(employeeId, date)` = يوجد ملخص للموظف في فترة تغطي التاريخ. ونسخة دفعية `GetLockedDaysAsync(employeeIds, from, to)` |
| `AttendancePeriodService` | ↑ | `CloseAsync(month, orgUnitId?, actor)`، `GetStatusAsync(month)`، `EnsurePeriodAsync(month)` |
| `AttendancePeriodsController` | `HR.Core.Web/Controllers/AttendancePeriodsController.cs` | MVC: `Index` (الفترات وحالتها)، `Close` (GET/POST). وAPI: `POST api/attendance/periods/{yyyy-MM}/close`، `GET api/attendance/periods/{yyyy-MM}` |
| Views | `HR.Core.Web/Views/AttendancePeriods/Index.cshtml`، `Close.cshtml` | اختيار الشهر والوحدة. عرض الناقص (أول 50 مع العدد الكلي) وتحذير «لا عطل مضبوطة للفترة» |
| ترحيل | `HR.Core.Infrastructure/Migrations/<ts>_LVA057_AttendancePeriods.cs` | الجداول الثلاثة، وحقول §2 على `DailyAttendanceRecords` |
| حذف | `AttendanceIntegrationService.ClosePeriodAndPublishAsync`، `DailyAttendanceRecordsController.ClosePeriod` (GET/POST)، `Views/DailyAttendanceRecords/ClosePeriod.cshtml` | الرابط القديم يُحوَّل إلى `AttendancePeriods/Close` |

**القواعد:**

1. الفترة شهر تقويمي (D-LVA-11). `EnsurePeriodAsync` تنشئها `Open` عند أول إغلاق. لا فترتان متداخلتان في جهة واحدة (القيد الفريد + فحص الخدمة في الوضع `Custom`).
2. **موظفو الإغلاق:**
   - من كانت وحدته الأساسية في شجرة الوحدة المختارة، عبر `MaterializedPath` كما في `OrgScopeResolver`، أو كل الجهة إن لم تُختر وحدة.
   - و`HireDate <= PeriodEnd`.
   - وحالته ليست `Terminated`، أو انتهت خدمته داخل الفترة. تاريخ الانتهاء هو آخر `EmploymentStatusHistories.ChangedAt` إلى `Terminated`.
3. مدير الفرع يغلق شجرة وحدته فقط. والمركزي يغلق أي وحدة أو الجهة كلها. أي وحدة خارج النطاق تعيد 403.
4. **أيام العمل** = أيام الفترة ضمن `[max(PeriodStart, HireDate), min(PeriodEnd, TerminationDate)]`، ناقص أيام الراحة الأسبوعية والعطل المضبوطة. السجل على يوم غير عمل يبقى، ولا يدخل أي عدّ.
5. `UnrecordedWorkingDays` = أيام العمل التي لا سجل لها. مع `RequireComplete` (D-LVA-9) يُرفض الإغلاق كله إذا كان لأي موظف في النطاق يوم ناقص. يعيد 409 (API) أو صفحة بالقائمة (MVC)، ولا يُكتب شيء. مع `CountAsAbsent` تُضاف الأيام الناقصة إلى `AbsentDays`، ومع `Ignore` تُعرض فقط.
6. الإغلاق idempotent: الموظف الذي له ملخص في الفترة يُتخطّى ويُعدّ في `SkippedAlreadyClosed`. والإغلاقان المتزامنان يتصادمان على القيد الفريد للملخص، فيعود الثاني برسالة «أُغلقت الفترة لبعض الموظفين بالتوازي؛ أعد المحاولة» دون أثر جزئي.
7. **الذرية:** كل ملخصات الإغلاق، وسجل `AttendancePeriodClosure`، وأحداث `IAttendanceSummaryPublished` (§4)، تُكتب في `SaveChanges` **واحد**: النشر عبر الـ outbox قبل الحفظ ثم حفظ واحد، فلا «نشر بعد آخر حفظ» (نمط PLAN-40).
8. تصير الفترة `Closed` عندما لا يبقى في الجهة موظف مستحق بلا ملخص، وإلا تكون `PartiallyClosed`. وحالة الفترة للعرض فقط، أما القفل فيُحسب لكل موظف (القاعدة 9).
9. **القفل:** يوم `(E, D)` مقفل إذا وُجد ملخص للموظف `E` في فترة تغطي `D`. لا يكتب على يوم مقفل إلا `AttendanceDayWriter.ApplyApprovedCorrectionAsync` (§3).
10. لا «إعادة فتح» للفترة في هذه الخطة. التغيير بعد الإغلاق تصحيح دائماً.

### 2. كاتب واحد ليوم الحضور، وسلامة التعديل (G-LVA-03، G-LVA-10)

**المشكلة:**
- الكتابة مبعثرة: `OrgBranchControllers.cs:128-198, 213-249`، و`AttendanceIntegrationService.cs:13-73`.
- الإدخال الجماعي يكتب كل أيام الشهر (`OrgBranchControllers.cs:233-242`).
- قائمة الموظفين أول 50 أبجدياً (`:275-281`)، وشاشة التعديل بلا خيار فارغ (`Views/DailyAttendanceRecords/Edit.cshtml:9-10`)، فيصير `record.EmployeeId = vm.EmployeeId` (`OrgBranchControllers.cs:177`) نقلاً صامتاً.
- `LeaveScheduleReconciler.cs:26-56` يعدّ أي سجل `OnLeave` مصدراً للحقيقة.
- المستهلك يحفظ الحالة ثم الحضور في حفظين منفصلين (`LeaveAndAdvanceApprovedConsumer.cs:46-53`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `DailyAttendanceRecord` + `Source` (`AttendanceRecordSource`، الافتراضي `Manual`)، `SourceReference` (`string?`، 100)، `ReplacedStatus` (`DailyAttendanceStatus?`) | `HR.Core.Domain/Entities/DailyAttendanceRecord.cs` | معرفة مصدر السجل، وإمكان عكس أثر الإجازة |
| `AttendanceDayWriter` | `HR.Core.Infrastructure/Services/Attendance/AttendanceDayWriter.cs` | المدخل الوحيد للكتابة: `CreateManualAsync`، `UpdateManualAsync`، `DeleteManualAsync`، `BulkFillAsync`، `ApplyLeaveAsync`، `RevertLeaveAsync`، `ApplyApprovedCorrectionAsync`. **لا يحفظ**؛ المستدعي يحفظ مرة واحدة |
| `DailyAttendanceRecordsController` | `HR.Core.Web/Controllers/OrgBranchControllers.cs:69-287` | يستدعي الكاتب، وحذف `PopulateEmployeesAsync` بقائمته المحدودة |
| `DailyAttendanceRecordEditViewModel` | `HR.Core.Web/ViewModels/OrgOperationsViewModels.cs` | بلا `EmployeeId` و`AttendanceDate` قابلين للربط. التعديل يغيّر الحالة والملاحظة فقط |
| Views | `Views/DailyAttendanceRecords/Create.cshtml`، `Edit.cshtml`، `BulkEntry.cshtml`، `Details.cshtml` (جديد) | منتقي `data-hr-picker="employee"` على `api/lookup/employees` (المقيّد بالنطاق) بدل القائمة. التعديل يعرض الموظف والتاريخ للقراءة. `Details` يُضاف (G-LVA-17) |
| `AttendanceIntegrationService` | `HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs` | `UpsertLeaveAttendanceAsync`/`RemoveLeaveAttendanceAsync` تمر عبر الكاتب |
| `LeaveApprovedConsumer`/`LeaveCancelledConsumer` | `HR.Core.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs:11-96` | سجل الحالة والحضور (والتصحيح النظامي إن وُجد) في `SaveChanges` واحد |
| `LeaveScheduleReconciler` | `HR.Core.Infrastructure/Services/LeaveScheduleReconciler.cs:24-67` | يقرأ السجلات `Source=Leave` فقط، ويعيد إلى `Active` فقط من كان آخر انتقال له إلى `OnLeave` بسبب يبدأ بـ `LeaveApproved:` أو `LeaveScheduleReconciler:` |
| اختبار معماري | `src/Tests/HRManagement.UnitTests/Architecture/AttendanceSingleWriterArchitectureTests.cs` | يمسح مصادر `src/Services/HRCore/**/*.cs` ويرفض `DailyAttendanceRecords.Add/AddRange/Remove/RemoveRange/Update` و`.Status =` على سجل حضور خارج `AttendanceDayWriter`. الاستثناء الوحيد اليوم `DemoDataSeeder`، في قائمة صريحة (الكتّاب الحاليون: `OrgBranchControllers.cs`، `AttendanceIntegrationService.cs`، `DemoDataSeeder.cs`) |
| ترحيل بيانات | ضمن `LVA057_AttendancePeriods` | `Source=Leave` للسجلات `Status=OnLeave AND Notes LIKE 'إجازة معتمدة:%'`، وما عداها `Manual`. لا حذف لسجلات قديمة |

**القواعد:**

1. أي كتابة يدوية أو جماعية على يوم مقفل تُرفض بـ «اليوم ضمن فترة حضور مغلقة؛ قدّم طلب تصحيح»، ولا يتغير شيء.
2. `EmployeeId` و`AttendanceDate` لا يتغيران بعد الإنشاء. تغيير الموظف = حذف (إن لم يكن اليوم مقفلاً) ثم إنشاء.
3. الحالة `OnLeave` لا تُكتب يدوياً ولا جماعياً، وتأتي من `ApplyLeaveAsync` وحدها بمرجع `LeaveRequestId=<id>`. والسجل `Source=Leave` لا يُعدَّل ولا يُحذف يدوياً.
4. **الإدخال الجماعي:**
   - أيام العمل فقط (التقويم)، ولا أيام بعد اليوم الحالي بتوقيت `Asia/Damascus`.
   - موظفو شجرة الوحدة حسب قاعدة §1-2.
   - يتخطى الأيام المقفلة والأيام التي لها سجل.
   - يعرض: المكتوب، والمتخطّى لأنه مقفل، والمتخطّى لأنه موجود.
5. تكرار يوم في الإنشاء يعطي رسالة تحقق عربية لا 500. يُفحص مسبقاً، ويُلتقط استثناء القيد الفريد (`HRCoreDbContext.cs:392`) احتياطاً.
6. **`ApplyLeaveAsync`:**
   - على يوم غير مقفل: يكتب `OnLeave` ويحفظ الحالة السابقة في `ReplacedStatus`.
   - على يوم مقفل: ينشئ تصحيحاً نظامياً (§3، D-LVA-12).
7. **`RevertLeaveAsync`:**
   - يمس السجلات ذات `SourceReference` المطابق وحدها.
   - يعيد `ReplacedStatus`، أو يحذف السجل إذا كانت `null`.
   - وعلى يوم مقفل يمر عبر تصحيح نظامي.
8. نطاق `vm.EmployeeId` في الإنشاء يأتي من PLAN-52. هذه الخطة تنقله إلى الكاتب (فحص واحد للإنشاء والإدخال الجماعي والتصحيح) ولا تزيله.

### 3. التصحيح بعد الإغلاق (G-LVA-03، G-LVA-10)

**المشكلة:** لا سبب ولا معتمد ولا أثر للتصحيح، والحذف حر (`OrgBranchControllers.cs:186-198`). والتصحيح بعد الإغلاق لا يصل الرواتب (`PayrollEventConsumers.cs:350-359`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `AttendanceCorrection` | `HR.Core.Domain/Entities/AttendanceCorrection.cs` | `AttendancePeriodId`، `EmployeeId`، `AttendanceDate`، `OldStatus?`، `NewStatus`، `Reason`، `Source` (`Manual`/`LeaveEvent`)، `SourceReference`، `Status` (`Pending`/`Approved`/`Rejected`)، `RequestedBy/At`، `DecidedBy/At`، `DecisionNote`، `ResultingVersion`. فهرس فريد جزئي: طلب `Pending` واحد لكل `(EmployeeId, AttendanceDate)` |
| `AttendanceCorrectionService` | `HR.Core.Infrastructure/Services/Attendance/AttendanceCorrectionService.cs` | `RequestAsync`، `ApproveAsync`، `RejectAsync`، `ApplySystemCorrectionAsync` (لأحداث الإجازة) |
| `AttendanceCorrectionsController` | `HR.Core.Web/Controllers/AttendanceCorrectionsController.cs` | MVC: `Index` (بانتظار اعتمادي / طلباتي)، `Create` (من يوم مقفل)، `Details` + `Approve`/`Reject`. وAPI: `POST api/attendance/corrections`، `POST api/attendance/corrections/{id}/approve`، `POST …/{id}/reject` |
| Views | `HR.Core.Web/Views/AttendanceCorrections/*` | رابط «طلب تصحيح» في `Index` للحضور على اليوم المقفل |

**القواعد:**

1. التصحيح لليوم المقفل فقط. اليوم غير المقفل يُعدَّل مباشرة (§2).
2. السبب إلزامي، 15 حرفاً على الأقل. `NewStatus ≠ OldStatus`. و`NewStatus = OnLeave` ممنوع في التصحيح اليدوي (§2 القاعدة 3).
3. الطالب يحمل `HrCore.AttendanceWrite` ويكون الموظف في نطاقه.
4. المعتمد يحمل `HrCore.AttendanceCorrectionApprove` (D-LVA-4: `hr-core` غير مقيد بفرع). ولا يكون هو الطالب، ويُفحص ذلك في الخادم بمقارنة `sub` بـ `RequestedBy`. والرفض يتطلب ملاحظة.
5. **الاعتماد ذري،** في `SaveChanges` واحد مع إعادة المحاولة عند تعارض `xmin` على الملخص:
   1. الكاتب يطبّق اليوم (`Source=Correction`، `SourceReference=AttendanceCorrection:<id>`).
   2. يُعاد حساب ملخص الموظف للفترة بالحاسب نفسه، و`Version += 1`، و`LastReason = Correction`.
   3. يُنشر `IAttendanceSummaryPublished` بالنسخة الجديدة.
   4. يُكتب `Approved` و`ResultingVersion`.
6. التصحيح الذي لا يغيّر أرقام الملخص (مثل `Present`←`Remote`) يرفع النسخة وينشرها أيضاً، والرواتب تسجلها بفرق صفري (§5).
7. التصحيح النظامي من حدث إجازة (D-LVA-12 = `AutoApply`) يُنشأ ويُعتمد في المعاملة نفسها التي يعالج فيها المستهلك الحدث. يحمل `RequestedBy = DecidedBy = null`، و`Source = LeaveEvent`، والسبب «إجازة معتمدة/ملغاة `<LeaveRequestId>`». ومع `Pending` يبقى بانتظار اعتماد بشري.
8. كل طلب واعتماد ورفض يُسجَّل في `EntityAuditLog` عبر `SaveChanges` العادي، فلا `ExecuteUpdate` في هذا المسار.

### 4. عقد التسليم مع الرواتب (يستهلكه PLAN-59)

**القناة:** **أحداث** عبر الـ outbox من HR.Core إلى الرواتب (لا استدعاء S2S، فلا يعتمد التسليم على ربط `Integration__HrCoreBaseUrl`). ومعها **واجهتا HTTP** للتشغيل والإثبات دون SQL.

**الحدث الجديد** `src/Shared/HRManagement.MessageContracts/Events/IAttendanceSummaryPublished.cs`:

| الحقل | النوع | الدلالة |
|-------|-------|---------|
| `SummaryId` | `Guid` | ثابت لكل (فترة، موظف)، وهو مفتاح الهوية في الرواتب |
| `Version` | `int` | 1 عند الإغلاق، و+1 لكل تصحيح معتمد |
| `AttendancePeriodId`، `PeriodStart`، `PeriodEnd` | `Guid`، `DateOnly` | الفترة الأصلية |
| `EmployeeId` | `Guid` | — |
| `WorkingDays`، `PresentDays`، `AbsentDays`، `OnLeaveDays`، `RemoteDays`، `UnrecordedWorkingDays` | `int` | **قيم مطلقة** لهذه النسخة. `AbsentDays` على أيام العمل فقط، وتشمل الناقص إذا كان الضبط `CountAsAbsent` |
| `Reason` | `string` | `Close` \| `Correction` \| `LeaveEvent` |
| `CorrectionId` | `Guid?` | للتتبع |
| `TenantId` | `Guid?` | مضبوط صراحةً، وترويسة الجهة كبقية أحداث HR.Core |
| `OccurredAt`، `CorrelationId`، `SourceSystem` | — | كالعادة |
| `SchemaVersion` | `int` (=1) | — |

**دلالة الاستهلاك في الرواتب** (تُنفَّذ في §5، ويعتمد عليها PLAN-59):

1. **أحدث نسخة تكسب.** القيم مطلقة. والرواتب تسجّل لكل نسخة **فرقاً** = `AbsentDays(v)` ناقص مجموع ما سُجّل سابقاً للـ `SummaryId` نفسه.
2. **التكرار:** النسخة نفسها لا تُسجَّل مرتين. يضمن ذلك صندوق الوارد (`ShouldSkipAsync`/`TryRecordAsync`) والفهرس الفريد على `SourceReference = "ATT:{SummaryId}:v{Version}"`.
3. **الترتيب:** نسخة أقدم من آخر نسخة مسجلة تُهمَل، ولا تُفقد قيمة لأن الأحدث مطلق.
4. **قفل الرواتب:** الفترة مجمّدة للرواتب إذا وُجد لها تشغيل `Regular` غير ملغى ولا معكوس، ونتائجه `ResultsFrozen` أو حالته `UnderReview`/`Approved`/`Posted` (بحراس PLAN-56).
   - الفرق الذي يصل قبل حساب تشغيل الفترة يُطبَّق في تشغيل الفترة نفسها.
   - الفرق الذي يصل بعد حسابها يُعلَّم `CarryForward`. يُطبَّق في إعادة حسابها إن حدثت قبل التجميد، وإلا في **أول تشغيل `Regular` لاحق** يبدأ بعد نهاية الفترة الأصلية، ببند يحمل الفترة الأصلية.
   - لا يُعدَّل أي صف مطبَّق في تشغيل.
5. **الفرق السالب** (غياب صُحّح إلى حضور) يُطبَّق استرداداً في التشغيل نفسه أو اللاحق: مكوّن خصم بقيمة موجبة يُنقص `TotalDeductions`. ولا تُنشأ صفوف بفرق صفري، ويُحدَّث الإيصال فقط.
6. **الإيصال:** لكل `SummaryId` صف `AttendanceSummaryReceipt` في الرواتب، هو المرجع لبوابة «الحضور مغلق لهذا الموظف في هذه الفترة».
7. **البوابة:** قبل حساب تشغيل `Regular`، كل موظف في نطاق التشغيل بلا إيصال يتقاطع مع فترة التشغيل يُعدّ «حضوره غير مغلق».
   - `Warn` (الافتراضي، D-LVA-10): يُحسب التشغيل ويُكتب `PayrollAuditRecord` بالعدد والقائمة.
   - `Block`: يُرفض بدء الحساب برسالة فيها العدد.
   - `Off`: لا فحص.
8. الحدث القديم `IAttendancePeriodClosed` يتوقف نشره ويُعلَّم `[Obsolete]`، ويُحذف مستهلكه. لا ينشر النظام بعد اليوم إلا الحدث الجديد للحضور.

**واجهات HTTP (عبر البوابة):**

| الواجهة | السياسة | الاستجابة |
|---------|---------|-----------|
| `POST api/hr-core/api/attendance/periods/{yyyy-MM}/close` (جسم: `{ "orgUnitId": null \| guid }`) | `HrCore.AttendanceClose` | 200: `{ periodId, status, closedEmployees, skippedAlreadyClosed }`. 409: `{ incomplete: [{ employeeId, unrecordedWorkingDays }], total }`. 403 خارج النطاق |
| `GET api/hr-core/api/attendance/periods/{yyyy-MM}` | `HrCoreReadOnly` + نطاق | الحالة، وعدد المغلق والمتبقي، وآخر نسخة لكل ملخص في النطاق |
| `POST api/hr-core/api/attendance/corrections` و`…/{id}/approve` و`…/{id}/reject` | `HrCore.AttendanceWrite` / `HrCore.AttendanceCorrectionApprove` | الحالة و`resultingVersion` |
| `GET api/payroll/runs/{id}/attendance-status` | `ModulePayroll` | `{ gateMode, employeesInScope, missingAttendance: [...], carryForwardApplied, carryForwardPending }` |
| `GET api/payroll/attendance/receipts?periodStart=yyyy-MM-dd` | `ModulePayroll` | الإيصالات بنسخها (للقبول ولـ PLAN-59) |

**ما يفعله PLAN-59 (للتنسيق، لا للتنفيذ هنا):**
1. يُدخل حضور شهر الدورة ويغلقه بـ `POST …/close`.
2. ينتظر `receipts` بعدد الموظفين.
3. يحسب التشغيل، ويتحقق من `attendance-status.missingAttendance = []`.
4. اختيارياً يرفع البوابة إلى `Block` في ملفه.

ولا يحتاج PLAN-59 أي SQL للحضور.

### 5. جانب الرواتب: الإيصال والفرق والبوابة (جانب الحضور من G-PAY-19؛ G-LVA-03)

**المشكلة:**
- المستهلك الحالي يأخذ أول إغلاق فقط ويتجاهل الصفر: `PayrollEventConsumers.cs:343-373`.
- المحمّل يقصر الغياب على تقاطع الفترة: `PayrollPartitionInputLoader.cs:152-162`.
- القاعدة تتخطى المبالغ غير الموجبة: `PayrollRules.cs:66-91`، والسطر `if (amount <= 0) continue;` عند `:80`.
- لا شرط إغلاق قبل الحساب: `PayrollRunJobHandlers.cs:38`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `AttendanceSummaryReceipt` | `src/Services/Payroll/Payroll.Domain/Entities/AttendanceSummaryReceipt.cs` | `SummaryId` (فريد)، `AttendancePeriodId`، `EmployeeId`، `PeriodStart/End`، `LatestVersion`، `LatestAbsentDays`، `RecordedAbsentDays`، `ReceivedAt`، `UpdatedAt`؛ و`xmin` |
| `AbsenceDeduction` + `OriginalPeriodStart/End` (`DateOnly?`)، `CarryForward` (`bool`)، `SourceVersion` (`int?`) | `Payroll.Domain/Entities/AbsenceDeduction.cs` | تمييز صفوف الحضور وحملها إلى الدورة التالية. الفهرس الفريد على `SourceReference` قائم (`PayrollDbContext.cs:245-252`) |
| ترحيل | `Payroll.Infrastructure/Migrations/<ts>_LVA057_AttendanceHandoff.cs` | الجدول والأعمدة وفهرس `(EmployeeId, CarryForward, AppliedInRunId)` |
| `AttendanceSummaryPublishedConsumer` | `Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs` (بدل `AttendancePeriodClosedConsumer`) | ينفّذ دلالة §4 في transaction واحدة: صندوق الوارد، قفل الإيصال، تحديد التجميد، صف الفرق، تحديث الإيصال |
| `PayrollAttendanceFreeze` | `Payroll.Application/Services/PayrollAttendanceFreeze.cs` | `IsFrozenAsync(period)` و`HasCalculatedRunAsync(period)` حسب §4 القاعدة 4 |
| المحمّل | `Payroll.Application/Services/PayrollPartitionInputLoader.cs:152-162` | يضيف للتشغيل `Regular`: `\|\| (a.CarryForward && a.OriginalPeriodEnd < periodStart && (a.AppliedInRunId == null \|\| a.AppliedInRunId == run.Id))` |
| `AbsenceRule` | `Payroll.Domain/Services/Engine/Rules/PayrollRules.cs:66-91` | `AbsentDays < 0`: استرداد `ComputeAbsenceAmount(context, -days)` بمكوّن `Deduction` موجب يُنقص `TotalDeductions`، واسمه «استرداد خصم غياب — فترة yyyy-MM». والبند المحمول اسمه «فرق غياب عن فترة yyyy-MM» |
| `PayrollAttendanceGate` + `PayrollAttendanceOptions` | `Payroll.Application/Services/PayrollAttendanceGate.cs`؛ `Payroll:Attendance:Gate` | يُستدعى في `StartPayrollRunJobHandler` بعد `ValidateCanCalculate` (`PayrollRunJobHandlers.cs:38`). `Warn` يكتب `PayrollAuditRecord`، و`Block` يعيد خطأ |
| API | `Payroll.Web/Controllers/PayrollApiController.cs` | `runs/{id}/attendance-status`، `attendance/receipts` |
| عرض | `Payroll.Web/Views/PayrollRuns/Details.cshtml` | لوحة «الحضور»: وضع البوابة، الناقص، الفروق المحمولة المطبقة والمعلقة |

**القواعد:**

1. المستهلك idempotent بطبقتين (صندوق الوارد + الفهرس الفريد)، ولا يعتمد على `TransactionId` عشوائي.
2. صف الفرق: `PeriodStart/End = OriginalPeriodStart/End` = الفترة الأصلية، و`SourceVersion = v`، و`CarryForward = HasCalculatedRunAsync(period)` لحظة الاستهلاك.
3. يُقفل صف الإيصال (`SELECT … FOR UPDATE`) داخل المعاملة، فنسختان متزامنتان لا تحسبان الفرق من الأساس نفسه.
4. البوابة لا تُطبَّق على تشغيلات `Correction` و`FinalSettlement`، فأحكامها لـ PAY-H وPLAN-64.
5. الصفوف القديمة (`Reason LIKE 'AttendancePeriodClosed:%'`) تبقى كما هي بلا `OriginalPeriod` وبالسلوك الحالي، ولا تُرحَّل.
6. لا تغيير على خصم الإجازة بدون أجر (`LeaveRequestId=`). أحكامها لـ PAY-H وLVA-D (الموجة 2).

### 6. الصلاحيات والقائمة المجمّدة

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `HrCore.AttendanceWrite` | `HrManagementRoles.cs` (`HrManagementPolicies`) + `HrManagementAuthorizationExtensions.cs` | `admin` \| `hr-core`؛ والنطاق في الكاتب |
| `HrCore.AttendanceClose` | ↑ | `admin` \| `hr-core`؛ والفرع يغلق شجرته فقط (§1-3) |
| `HrCore.AttendanceCorrectionApprove` | ↑ | (`admin` \| `hr-core`) و`!HrOrgScopeVisibility.IsBranchScoped(user)` (D-LVA-4) |
| موقف النطاق (PLAN-52) | `AttendanceCorrectionsController`، `AttendancePeriodsController` | الأول `[OrgScoped(typeof(AttendanceCorrection))]` بمفتاح الموظف في `HrCoreOrgScopeKeys`. والثاني `[OrgScopeNotApplicable("orgUnitId checked in AttendancePeriodService")]` لأن الخدمة تفحص الوحدة (§1 القاعدة 3). فيبقى `OrgScopeCoverageTests` أخضر |
| السياسات الصريحة | إجراءات `DailyAttendanceRecords.*` الخمسة | أعطاها PLAN-51 `HrCore.Write` صريحاً وحذف `hrcore-post-policy-allowlist.txt` واختباره. هنا تُستبدل بالسياسات الثلاث أعلاه. وكل POST جديد يحمل سياسة صريحة، ويفرض ذلك `WriteActionsHaveExplicitPolicyTests` في `HRManagement.ArchitectureTests` (PLAN-51، قائمة HR.Core فارغة) |

عرّف PLAN-51 سياستي `{Module}.Write`/`{Module}.Approve` من `Authorization:Policies` بالمساعد `RolesPolicy` (D-51-2)، فتُعرَّف السياستان الأوليان بالمساعد نفسه. أما سياسة اعتماد التصحيح فتبقى خاصة لأنها تستثني الفرع.

---

## Out of scope

| البند | الخطة |
|-------|-------|
| ربط `hr-contracts-web` بالترميز في الإنتاج (G-LVA-18) | PLAN-37 |
| نطاق الفرع على `Rosters` وبقية HR.Core (G-IAM-04) | PLAN-52 |
| ذرية اعتماد الإجازة وإلغائها على PostgreSQL (G-LVA-01)، ولذلك يُحقَن حدث الإجازة في القبول | LVA-A (الموجة 2) |
| الأرصدة، والكتالوج القانوني، والحوكمة، والتداخل، وESS (G-LVA-02، 04–09) | LVA-B/D/E/F (الموجة 2) |
| حالة «مهمة» في الحضور، والتقويم الرسمي المركزي المؤرخ (G-LVA-11، 15) | LVA-G (الموجة 2) |
| الورديات والتوقيت والتأخير والأجهزة والإضافي (G-LVA-14، والجزء غير الحضوري من G-PAY-19) | LVA-H / PAY-H (الموجة 2) |
| فترة الرواتب وتاريخ القطع لكل المدخلات، والأجر التاريخي للفرق، وإرجاع خصم إجازة ملغاة بعد التطبيق (G-PAY-07) | PAY-H (الموجة 2) |
| آلة حالات التشغيل والإلغاء المحرِّر للمدخلات | PLAN-56 |
| الدورة الكاملة بإعدادات الإنتاج حتى الصرف | PLAN-59 |
| النشر الذري العام بين الخدمات وحارسه المعماري | PLAN-40 |
| تصحيح وثائق «Done» على مستوى البرنامج | PLAN-65 |

## Acceptance scenarios

على الملف الشبيه بالإنتاج (PLAN-37)، تحت `ACCEPTANCE_REQUIRE_ALL=true`، بـ `EvidenceRecorder` و`[RequiredAcceptance]` و`AcceptanceGate.RequireData`. الشهور محجوزة لهذه الخطة في `testdata/acceptance/lva057-periods.json` (افتراضياً 2026-01..2026-04، جهة A). كل سيناريو يبدأ بإعادة ضبط اختبارية لشهره في HR.Core والرواتب (SQL اختباري مسجل في `steps.md`)، فيعطي التشغيل المتكرر النتيجة نفسها. الشخصيات: `br1-mgr`، `br2-mgr`، `central-a`، `central-a2`، `payroll-processor`، `payroll-approver`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-LVA-57a | `br1-mgr` يملأ شهراً بالإدخال الجماعي (`Present`)، ويعلّم يومَي غياب للموظف E، ويغلق وحدته. النتيجة: ملخصات v1 لكل موظفي الشجرة، ومنهم موظف وحدة فرعية وموظف انتهت خدمته داخل الشهر (يُهيأ بـ SQL اختباري)، وإيصالات في الرواتب، و`AbsenceDeduction` بيومين لـ E | `br1-mgr` يعدّل يوماً من الشهر المغلق ← رفض والسجل كما هو. و`br2-mgr` يغلق وحدة `br1` ← 403 |
| AC-LVA-57b | الإدخال الجماعي بحالة `Absent` لا يكتب أي جمعة أو سبت أو عطلة مضبوطة ولا يوماً مستقبلياً، والإغلاق يعدّ أيام العمل فقط | إغلاق وموظفٌ له يوم عمل بلا سجل ← 409 بالقائمة، ولا ملخص ولا حدث (`RequireComplete`) |
| AC-LVA-57c | قبل حساب تشغيل الشهر: `br1-mgr` يطلب تصحيح يوم لـ E (`Absent`←`Present`) بسبب، و`central-a` يعتمد ← v2، وفرق −1 في الرواتب. وحساب التشغيل يخصم يوماً واحداً صافياً | الطالب يعتمد طلبه ← رفض. و`br1-mgr` (فرع) يعتمد ← 403. وسبب قصير ← رفض |
| AC-LVA-57d | بعد تجميد تشغيل الشهر M (تحقق ثم إرسال للمراجعة، بحراس PLAN-56): تصحيح معتمد (`Present`←`Absent`) ← صف فرق `CarryForward`. وتشغيل M+1 يضم «فرق غياب عن فترة M» بيوم واحد، وتشغيل M لم يتغير | إعادة نشر النسخة نفسها (المساعد `AcceptanceMessagingHelper`) لا تُنشئ صفاً ثانياً. ونسخة أقدم تصل بعد الأحدث تُهمَل |
| AC-LVA-57e | تشغيل لشهر مغلق بالكامل: `attendance-status.missingAttendance = []` ولا سجل تدقيق تحذيري | تشغيل لشهر غير مغلق لبعض الموظفين، والوضع `Warn`: الحساب ينجح، وسجل تدقيق، والقائمة تساوي الموظفين غير المغلقين. (وضع `Block` في اختبار التكامل) |
| AC-LVA-57f | تعديل حالة سجل يُبقي صاحبه وتاريخه، ويُسجَّل في `EntityAuditLog`. ويُختار موظف خارج أول 50 أبجدياً إن وُجد، وإلا أي موظف | POST تعديل يحمل `EmployeeId`/`AttendanceDate` مختلفين (overposting) ← يُتجاهلان. وإنشاء أو تعديل بحالة `OnLeave` يدوياً ← رفض. وحذف سجل `Source=Leave` يدوياً ← رفض |
| AC-LVA-57g | حدث `ILeaveApproved` (يُحقن بالمساعد إلى أن تُنفَّذ LVA-A) ليوم في شهر مغلق ← تصحيح نظامي `Approved` ونسخة جديدة وفرق في الرواتب. وإلغاؤه ← تصحيح عكسي ويعود اليوم إلى حالته السابقة | الحدث نفسه مكرراً لا ينتج تصحيحاً ثانياً. و`LeaveScheduleReconciler` لا يضع في `OnLeave` موظفاً سجله اليدوي القديم `OnLeave` بلا إجازة |
| AC-LVA-57h | API: `POST …/periods/{month}/close` و`GET …/periods/{month}` و`GET api/payroll/attendance/receipts` تعطي النتيجة نفسها التي تعطيها الواجهة، وهو ما يعتمد عليه PLAN-59 | `outsider-b` (جهة أخرى) يطلب إغلاق شهر جهة A ← 403/404 بلا أثر |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web

# وحدة ومعمارية
dotnet test src/Tests/HRManagement.UnitTests --filter "Lva057|Architecture|AbsenceRule|Attendance"
dotnet test src/Tests/HRManagement.ArchitectureTests

# تكامل على PostgreSQL (Testcontainers)
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Lva057"

# انحدار
dotnet test src/Tests/HRManagement.UnitTests --filter "Payroll|Leave|Hcm002"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "OutboxAtomicity|Payroll|InboundEvent"

# قبول على الملف الشبيه بالإنتاج (أمر التشغيل كما يعرّفه PLAN-37)
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcLva57"
pwsh scripts/lva057-validate.ps1
```

## Definition of done

- لا أي `DailyAttendanceRecords.Add/Remove/Update` خارج `AttendanceDayWriter` (اختبار معماري أخضر).
- إجراءات `DailyAttendanceRecords.*` بالسياسات الجديدة، وكل POST جديد بسياسة صريحة (`WriteActionsHaveExplicitPolicyTests` من PLAN-51 أخضر).
- `ClosePeriodAndPublishAsync` و`AttendancePeriodClosedConsumer` محذوفان، و`IAttendancePeriodClosed` بلا ناشر.
- الإغلاق والتصحيح يكتبان الملخص والحدث في حفظ واحد. يثبت ذلك اختبار PostgreSQL بفشل محقون بعد إضافة الحدث: لا ملخص ولا رسالة في الـ outbox.
- `Lva057PayrollAttendanceHandoffIntegrationTests` على PostgreSQL أخضر. يغطي: التكرار، والترتيب المعكوس، والحمل إلى الدورة التالية، والاسترداد السالب، والبوابة `Block`، وتزامن نسختين على الإيصال نفسه.
- AC-LVA-57a..h = Pass على الملف الشبيه بالإنتاج، مع مجلد `acceptance-evidence/AC-LVA-57/` بالعناصر الثمانية، و`scenarios` في `result.json`.
- «الحالة» في الدراسة لـ G-LVA-03 وG-LVA-10، وسطر جانب الحضور في G-PAY-19 بدراسة الرواتب، محدَّثة بالدليل.
- `HCM_002_ATTENDANCE.md` يصف النموذج الفعلي. وسطر الحضور في `RELEASE_PRODUCT_SCOPE.md:28` يطابق D-LVA-3. و`EVENT_PUBLISHER_INVENTORY.md` و`MESSAGE_EVENT_MATRIX.md` فيهما الحدث الجديد.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md` |
| Domain (HR.Core) | `AttendancePeriod.cs`، `AttendancePeriodClosure.cs`، `AttendanceEmployeeSummary.cs`، `AttendanceCorrection.cs`، `AttendanceSummaryCalculator.cs`، تعدادات `AttendancePeriodStatus`/`AttendanceRecordSource`/`AttendanceCorrectionStatus` |
| Infrastructure (HR.Core) | `Services/Attendance/` (`AttendanceCalendar`، `AttendanceLockService`، `AttendanceDayWriter`، `AttendancePeriodService`، `AttendanceCorrectionService`)، ترحيل `LVA057_AttendancePeriods` |
| Web (HR.Core) | `AttendancePeriodsController.cs`، `AttendanceCorrectionsController.cs`، تعديل `OrgBranchControllers.cs`، Views `AttendancePeriods/*`، `AttendanceCorrections/*`، `DailyAttendanceRecords/{Create,Edit,BulkEntry,Details}.cshtml` |
| Contract | `HRManagement.MessageContracts/Events/IAttendanceSummaryPublished.cs`؛ `[Obsolete]` على `IAttendancePeriodClosed.cs` |
| Payroll | `AttendanceSummaryReceipt.cs`، حقول `AbsenceDeduction`، `AttendanceSummaryPublishedConsumer`، `PayrollAttendanceFreeze.cs`، `PayrollAttendanceGate.cs`، تعديل `PayrollPartitionInputLoader.cs` و`PayrollRules.cs` (`AbsenceRule`)، API و`Details.cshtml`، ترحيل `LVA057_AttendanceHandoff` |
| Config | `Attendance:*` في `HR.Core.Web/appsettings.json` و`.Production.json` و`.Staging.json`؛ `Payroll:Attendance:Gate` في `Payroll.Web/appsettings*.json`؛ قيم العطل لشهور القبول في `docker-compose.acceptance.yml` (طبقة القبول فقط) |
| Unit | `Lva057AttendanceSummaryCalculatorTests`، `Lva057AttendanceDayWriterTests`، `Lva057AttendanceCorrectionRulesTests`، `Lva057AbsenceRuleRefundTests` (مع `PayrollRunReconciliation`)، `Lva057PayrollAttendanceGateTests`، `AttendanceSingleWriterArchitectureTests` |
| Integration (PostgreSQL) | `Lva057AttendanceCloseIntegrationTests` (إغلاق متزامن، ذرية الـ outbox، تصحيح بـ `xmin`)، `Lva057PayrollAttendanceHandoffIntegrationTests`؛ وإعادة كتابة `OutboxAtomicityIntegrationTests.CloseAttendancePeriod_SavesAfterPublishLoop` (`:403`) على الخدمة الجديدة |
| Acceptance | `src/Tests/HRManagement.AcceptanceTests/AcLva57AttendancePeriodTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، `testdata/acceptance/lva057-periods.json`، ومساعدات `PublishAttendanceSummaryAsync`/`PublishLeaveApprovedAsync` في `AcceptanceMessagingHelper.cs` |
| Script | `scripts/lva057-validate.ps1` (يتحقق من الملفات وسياسات إجراءات الحضور وغياب الناشر القديم، ومعه `-Inventory`) |
| Docs | `HCM_002_ATTENDANCE.md` (إعادة كتابة)، `docs/runbooks/attendance-period-close.md` (جديد: الإغلاق، والتصحيح، وضبط العطل، وتصريف طابور الحدث القديم قبل النشر)، `EVENT_PUBLISHER_INVENTORY.md`، `MESSAGE_EVENT_MATRIX.md`، `RELEASE_PRODUCT_SCOPE.md`، أسطر «الحالة» في `HCM_002_LEAVE_ATTENDANCE_GAP_STUDY.md` (G-LVA-03، G-LVA-10) و`PAY_001_GAP_STUDY.md` (G-PAY-19، جزء الحضور) |
| Evidence | `acceptance-evidence/AC-LVA-57/` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| رسائل `IAttendancePeriodClosed` عالقة في الطابور لحظة النشر، فيُحذف مستهلكها | خطوة في الـ runbook: تصريف الطابور قبل النشر، أو إبقاء المستهلك القديم إصداراً واحداً بوضع «سجل وتجاهل». يُختار الثاني افتراضياً إذا لم يكن الطابور فارغاً في المرحلة 0 |
| تغيير PLAN-56 لمعنى `ResultsFrozen` أو الحالات | `PayrollAttendanceFreeze` نقطة واحدة تقرأ حراس PLAN-56. واختبار تكامل يغطي كل حالة تشغيل |
| `RequireComplete` يعطّل الإغلاق في جهات لا تُدخل الحضور يومياً | الإدخال الجماعي لأيام العمل يملأ الشهر بنقرة. والمالك يستطيع `CountAsAbsent`/`Ignore` (D-LVA-9). والبوابة `Warn` لا توقف الرواتب |
| الاسترداد السالب يجعل `TotalDeductions` سالباً لموظف بلا خصومات أخرى | اختبار وحدة مع `PayrollRunReconciliation` (`ValidateLineIntegrity` يقبل ذلك حسابياً). والصيغة النهائية للصافي تملكها PLAN-56. يُوثَّق في القسيمة كبند استرداد |
| الجهة على الحدث تأتي من الترويسة. فإن لم تُضبط، قد تُكتب صفوف الرواتب بجهة فارغة | الناشر يضبط ترويسة الجهة و`TenantId`. واختبار التكامل يتحقق من `TenantId` على الإيصال والفرق (يمس PLAN-42 «جهة مشتقة من الخادم») |
| العطل في الإعداد مؤقتة ولا مصدر قانوني لها | تحذير في صفحة الإغلاق، وD-LVA-13، والبديل الدائم LVA-G. والقيم تُضبط في ملف الإعداد لا في الشيفرة |
| أداء الإغلاق لجهة بـ 2000 موظف | استعلام واحد للسجلات في الفترة وللقفل. هدف < 10 ثوانٍ في اختبار التكامل بـ 2000 موظف × 31 يوماً |

## Effort estimate

9–10 أيام عمل:

| البند | الأيام |
|-------|--------|
| المرحلة 0 | 0.5 |
| النموذج والترحيلات | 1 |
| الكاتب الواحد والقفل وتعديل المتحكم والواجهات والـ reconciler | 1.5 |
| الإغلاق والملخص والحدث | 1.5 |
| التصحيح وواجهته | 1.5 |
| جانب الرواتب (المستهلك والمحمّل والقاعدة والبوابة والـ API) | 1.5–2 |
| اختبارات PostgreSQL | 1 |
| القبول والأدلة والوثائق | 1–1.5 |

هذه الخطة في الحد الأعلى للحجم. إذا ضاق الوقت يُنقل الـ API الخاص بالتصحيح (لا الإغلاق) إلى LVA-I دون مساس بعقد PLAN-59.
