# PLAN-56 — آلة حالات تشغيل الرواتب: صافٍ واحد، رفض يعيد الفتح، إلغاء، وعكس معتمد (PAY-C)

**Scope:** PAY — [دراسة الفجوات](../PAY_001_GAP_STUDY.md): G-PAY-03، G-PAY-04، G-PAY-06، وجزء G-PAY-07 (تحرير المدخلات عند الإلغاء والرفض والعكس، وإعادة الحساب بعد تعارض البصمة)  
**Status:** Done — 2026-11-25 (نُفّذت؛ التقرير: [plan56-final-report.md](../../reports/plan56-final-report.md))  
**Priority:** P0  
**Depends on:** PLAN-54 (الإطار المثبت، واعتماد المدخلات اليدوية)، PLAN-40 (نشر الأحداث الذري؛ تستهلكه هذه الخطة لحدث العكس)  
**Related cards:** PAY-001، PAY-003، PAY-004، NAT-003

## Goal

- أن يكون للصافي تعريف واحد يستعمله المحرك والتحقق والترحيل والقسيمة والملف، فتمر دورة فيها تسويات موجبة وسالبة، ويساوي المرحَّلُ المعتمدَ.
- أن تعمل حلقة «مراجعة ← رفض ← تصحيح ← إعادة إرسال».
- أن يُلغى أي تشغيل غير مرحّل فتتحرر فترته ومدخلاته.
- ألا ينتقل تشغيل بين حالتين إلا بحارس، وأن يكون العكس طلباً بسبب يعتمده شخص آخر.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-PAY-16 — عكس تشغيل خرج ماله إلى المصرف | العكس مسموح ما دامت الدفعة لم تُرسل (`Pending`/`Failed`/`Rejected`)، فتُبطَل الدفعة وأوامرها. وبعد الإرسال أو التسوية يُرفض العكس برسالة توجّه إلى تشغيل تصحيح (فرق سالب ← خصم استرداد). استرجاع المال المسدَّد عبر القناة في الموجة 2 (**PAY-I**) | لا «دفعة عكس» تدّعي استرداد مال لم تطلبه أي قناة |
| D-PAY-17 — الضريبة والتأمين على فروق التصحيح | التسوية خارج وعاء الضريبة والتأمين، في التشغيل الاعتيادي والتصحيح على السواء، كما يعمل المحرك اليوم ([PayrollRules.cs:231-249](../../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)). وخضوع الفروق الرجعية للضريبة في الموجة 2 (D-PAY-8، **PAY-H**) | يُحذف الحساب المزدوج للفرق في التصحيح، ولا يُخترع وعاء جديد |
| D-PAY-18 — من يلغي تشغيلاً | المعالج يلغي ما لم يُرسل للمراجعة. وما كان `UnderReview` أو `Approved` يلغيه حامل `Payroll.Approve`. والسبب إلزامي دائماً | الإلغاء بعد الاعتماد قرار المعتمد لا المعالج |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-56-pay-run-state-machine` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay003\|FullyQualifiedName~PayrollFinancialInvariants\|FullyQualifiedName~Correction\|FullyQualifiedName~Pay001"` | أخضر أو قائمة في `reports/plan56-baseline.md` |
| 0.3 | جرد الصافي | كل قراءة أو حساب لـ `NetPay` من `GrossPay`/`TotalDeductions` في Payroll (المحرك، والتحقق، والترحيل، و`PayrollRunDetailsDto`، و`PayrollReportsController`، و`Ess/Payslips.cshtml`، و`PayrollExternalExportService`، و`PayrollExportJobService`، و`PayrollDeterminismComparer`) | الجدول في التقرير. كل بند يُحوَّل في البند 1 |
| 0.4 | جرد الانتقالات | كل إسناد لـ `PayrollRun.Status` خارج `PayrollRun.cs` (مثل [PayrollRunParallelCalculator.cs:340-348, 372-386](../../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)) | الجدول في التقرير |

---

## In scope

### 1. صافٍ واحد، وترحيل يتحقق ولا يعدّل، وتصحيح يمر بالتحقق (G-PAY-03)

**المشكلة:**
- المحرك يحسب الصافي بالتسويات ([PayrollRules.cs:305-308](../../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)). والتحقق يقارنه بـ `Gross − Deductions` ([PayrollRunReconciliation.cs:22-25](../../src/Services/Payroll/Payroll.Domain/Services/PayrollRunReconciliation.cs)). فأي تسوية غير صفرية تُفشل الدورة كلها ([PayrollRunParallelCalculator.cs:372-382](../../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)).
- الترحيل يعيد كتابة صافي الأسطر المجمدة ([PayrollRunHandlers.cs:238-241](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) ← [PayrollCalculation.cs:8-11](../../src/Services/Payroll/Payroll.Domain/Services/PayrollCalculation.cs)).
- التصحيح:
  - يأخذ كل تسويات الموظف أياً كانت فترتها ([CorrectionRunHandlers.cs:77-82](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
  - يسجّل الفرق في الأساس والتسوية معاً ([PayrollRules.cs:14-24](../../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
  - يتجاوز التحقق ([CorrectionRunHandlers.cs:109-111](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SalaryLine.TotalAdjustments` | `Payroll.Domain/Entities/SalaryLine.cs` | مجموع التسويات صريحاً على السطر |
| `PayrollNet` | `Payroll.Domain/Services/PayrollNet.cs` | `Unclipped = Gross − Deductions + Adjustments`، و`Net = max(0, Unclipped)` بتقريب الإطار. ويقبل `TotalDeductions` سالباً حين يغلب استرداد غياب الحضور (PLAN-57 §4 القاعدة 5). يستعمله `RoundingRule` و`ValidateLineIntegrity` والقسيمة والتصدير. ويُحذف `ApplyNetFromGrossAndDeductions` |
| `ValidateLineIntegrity` | [PayrollRunReconciliation.cs:8-34](../../src/Services/Payroll/Payroll.Domain/Services/PayrollRunReconciliation.cs) | `NetPay == PayrollNet.Net(line)`، و`Σ المكوّنات == Unclipped` |
| `PostPayrollRunHandler` | [PayrollRunHandlers.cs:211-264](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) | يُعيد `ValidateTotals` على الأسطر المحمَّلة، ويقارن `Σ NetPay` بـ `TotalNetPay` المجمَّد عند التحقق. وأي فرق ← رفض «تغيّرت النتائج بعد التحقق». لا إسناد لأي سطر |
| `PayrollAdjustment.TargetSourceRunId` | `Payroll.Domain/Entities/PayrollAdjustment.cs` | تسوية موجّهة إلى تصحيح تشغيل بعينه. المحمّل الاعتيادي يتجاهلها، والتصحيح لا يأخذ غيرها |
| `CreateCorrectionRunHandler` | [CorrectionRunHandlers.cs:68-111](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs) | يأخذ تسويات `TargetSourceRunId == source.Id` المعتمدة (PLAN-54) فقط. لا أساس ولا ضريبة على الفرق (D-PAY-17)، ويُزال مسار `correctionDeltaGross` من `BaseSalaryRule`. ثم يمر بـ `ValidateLineIntegrity` قبل `MarkValidated`، كالاعتيادي |
| فرق سالب | `PayrollNet` + `CorrectionRunHandlers` | إن كان `Unclipped < 0` يُنشأ `DeductionRecord` بنوع `CorrectionRecovery`، و`DueDate` = بداية الفترة التالية، و`SourceReference` = السطر، فيُسترد في الدورة التالية بدل أن يصير صفراً |
| ترحيل | `PAY056_NetFormula` | يضيف العمود ويملؤه للأسطر القائمة من مكوّنات `Adjustment` |

**القواعد:**
1. لا رمز في Payroll يحسب الصافي إلا `PayrollNet`. يفرض ذلك اختبار معماري يبحث عن `GrossPay - ` خارجه.
2. الترحيل لا يكتب `NetPay` ولا `GrossPay` ولا `TotalDeductions`.
3. تقصّ الخصومات الزائدة في التشغيل الاعتيادي باقٍ كما هو. حمله إلى الدورة التالية مع سقف قانوني في الموجة 2 (**PAY-E**).

### 2. الرفض يعيد الفتح، وأمر إلغاء، وتحرير المدخلات (G-PAY-04، G-PAY-07)

**المشكلة:**
- `Reject` يُبقي النتائج مجمدة ([PayrollRun.cs:151-164](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs))، و`ValidateCanCalculate` يرفض إعادة الحساب ([:60-65](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)).
- الإلغاء الوحيد يرفض مهمة `Completed` ([PayrollRunJobHandlers.cs:275-276](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)) ولا يحرر شيئاً ([:264-296](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
- المحمّل لا يقرأ إلا مدخلاً حراً أو مطبّقاً في التشغيل نفسه ([PayrollPartitionInputLoader.cs:129, 144, 158](../../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)).
- مهمة فشلت بتعارض البصمة لا تُستأنف ([PayrollRunJobHandlers.cs:230-260](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollInputRelease` | `Payroll.Application/Services/PayrollInputRelease.cs` | `ReleaseAsync(runId, reason)` يحرر `DeductionRecord.AppliedInRunId` و`AbsenceDeduction.AppliedInRunId` و`PayrollAdjustment.PayrollRunId`، ويحذف خصوم `CorrectionRecovery` التي أنشأها التشغيل ولم تُطبَّق. يعمل داخل معاملة المستدعي. المستدعون: الإلغاء، والرفض، والعكس، وإعادة الحساب |
| `PayrollRun.ReopenAfterRejection` | `PayrollRun.cs` | من `UnderReview` فقط ← `Draft`، `ResultsFrozen=false`، `ValidatedAtCalculate=false`، `Revision++`. ملاحظة الرفض إلزامية |
| `PayrollRunRevision` | `Payroll.Domain/Entities/PayrollRunRevision.cs` | لقطة النسخة المرفوضة: الرقم، وعدد الأسطر، و`TotalNetPay`، وبصمة الأسطر، والرافض، والملاحظة. ثم تُحذف الأسطر ومكوّناتها لإعادة الحساب |
| `RejectPayrollRunHandler` | [PayrollRunHandlers.cs:149-176](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) | الرفض + لقطة النسخة + حذف الأسطر + `PayrollInputRelease`، في معاملة واحدة |
| `CancelPayrollRunCommand` | `Payroll.Application/Features/PayrollRuns/CancelPayrollRunCommand.cs` | لأي حالة عدا `Posted`/`Reversed`/`Cancelled`. سبب إلزامي، وصلاحية حسب D-PAY-18. يلغي المهمة النشطة وأقسامها، ويحرر المدخلات، ويضع التشغيل `Cancelled` بـ `CancelledBy`/`CancelledAt`/`CancellationReason`. و`CancelPayrollRunJobHandler` يفوّض إليه |
| `RestartCalculationCommand` | `Payroll.Application/Features/PayrollRuns/RestartCalculationCommand.cs` | لمهمة `Failed` أو `Paused` على تشغيل غير مجمّد: تُلغى المهمة، وتُحذف أسطرها، وتُحرَّر المدخلات، ويعود التشغيل `Draft`، ثم يُبدأ حساب بلقطة جديدة. هذا مخرج تعارض البصمة |
| API | `PayrollApiController` | `POST runs/{id}/reject` (`Payroll.Approve`، جسم `notes`)، `POST runs/{id}/cancel` (جسم `reason`)، `POST jobs/{jobId}/restart` (`Payroll.Process`). وأزرار مقابلة في `Details.cshtml` |

**القواعد:**
1. لا مدخل يبقى معلّقاً بتشغيل `Cancelled` أو بنسخة مرفوضة.
2. الإلغاء والرفض يكتبان `EntityAuditLog` وسجل `PayrollRunApproval` بالقرار والسبب.
3. `PayrollInputRelease` يمس `AppliedInRunId` وحده، ولا يمس حقول PLAN-57 (`CarryForward`، `OriginalPeriodStart/End`، `SourceVersion`). فالرفض الذي يعيد الفتح يُدخل فروق الحضور المعلّقة للفترة نفسها في إعادة الحساب، كما تنص قاعدة التجميد في PLAN-57 §4.
4. فهرس التشغيل الحي ([PayrollDbContext.cs:97-101](../../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)) يُقصر على `RunType = Regular`. ويُضاف للتصحيح فهرس فريد: تصحيح واحد غير مرحّل لكل `SourceRunId`. ومفتاح `FinalSettlement` تحدده PLAN-64.

### 3. حارس لكل انتقال، وحذف السطر اليدوي (G-PAY-06)

**المشكلة:**
- `MarkValidated` يعيد أي حالة إلى `Calculated` ([PayrollRun.cs:113-119](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)).
- `ValidatePayrollRunHandler` لا يفحص الحالة ([PayrollRunJobHandlers.cs:300-346](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs))، فيُحيي تشغيلاً `Approved` أو `Posted` أو `Reversed`.
- `AddSalaryLine` اليدوي ينشئ سطراً بلا مكوّنات ([PayrollRunsController.cs:153-203](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollRunsController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| جدول الانتقالات | `Payroll.Domain/Services/PayrollRunTransitions.cs` | `Draft→Calculating→Calculated(validated)→UnderReview→Approved→Posted→Reversed`، و`UnderReview→Draft` (رفض)، و`*→Cancelled` قبل الترحيل. كل دالة في `PayrollRun` تستدعي `EnsureTransition` |
| `MarkValidated` | `PayrollRun.cs` | من `Calculating`/`Calculated`/`Validating` فقط |
| `Status` | `PayrollRun.cs` | `private set`. ويُستبدل الإسناد المباشر في الحاسب (`:340-348, 378`) بدالة `MarkCalculationFailed()` |
| `AddSalaryLine` | `PayrollRunsController.cs` + `Views/PayrollRuns/AddSalaryLine.cshtml` | يُحذفان. والتعديل اليدوي يكون تسويةً معتمدة (PLAN-54) |
| رسالة المرجع المكرر | [PayrollRunHandlers.cs:268](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) | `DbUpdateException` على `IX_PaymentBatches_Run_BankRef` ← 409 برسالة عربية لا 500 (احتياط بعد إغلاق الإحياء) |

### 4. العكس طلب يعتمده شخص آخر (G-PAY-06)

**المشكلة:** العكس ([PayrollRunHandlers.cs:277-343](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)):
- بلا فاعل ولا سبب ولا فصل واجبات، والمُرحِّل نفسه يملك `PayrollReverse` ([HrManagementAuthorizationExtensions.cs:144-147](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- يضع الدفعة `Reversed` مباشرة ([PayrollRunHandlers.cs:313-314](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs))، متجاوزاً [PayrollRunJobEnums.cs:93-104](../../src/Services/Payroll/Payroll.Domain/Enums/PayrollRunJobEnums.cs).
- لا يمس الأوامر، ولا يحرر التسويات (`:316-325`)، ولا ينشر حدثاً.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollRun.PostedBy`/`PostedAt` | `PayrollRun.cs` | يُضبطان في الترحيل، لفحص فصل الواجبات |
| `PayrollReversalRequest` | `Payroll.Domain/Entities/PayrollReversalRequest.cs` | `PayrollRunId`، `RequestedBy`، `Reason` (إلزامي)، `Status` (`Pending`\|`Approved`\|`Rejected`)، `DecidedBy`، `DecidedAt`، `DecisionNotes`. فهرس فريد: طلب معلّق واحد لكل تشغيل |
| أوامر | `Features/PayrollRuns/Reversal/` | `RequestPayrollReversalCommand` (`Payroll.Reverse`)، `DecidePayrollReversalCommand` (`Payroll.Approve`؛ المقرر ≠ الطالب ≠ `PostedBy`)، ويُحذف `ReversePayrollRunCommand` المباشر |
| تنفيذ العكس | داخل `DecidePayrollReversalCommand` عند الاعتماد | (1) الدفعة الأصلية `Pending`/`Failed`/`Rejected` ← حالة جديدة `Voided` عبر `PaymentBatchTransitions`، وأوامرها غير المرسلة ← `Cancelled` (قيمة جديدة في `PaymentOrderStatus`). (2) `Submitted`/`Acked`/`Settled`/`Processed` ← رفض برسالة D-PAY-16. (3) `PayrollInputRelease`. (4) `run.Reverse()`. (5) `IPayrollReversed` عبر صندوق الصادر بنمط PLAN-40 |
| حدث | `HRManagement.MessageContracts/Events/IPayrollReversed.cs` + مستهلك في `Dev.Analytics` بجوار `PayrollProcessedConsumer` | يُنقص إجمالي التشغيل |
| API و MVC | `POST runs/{id}/reversal-requests`، `POST reversal-requests/{id}/decide`؛ شاشة «طلبات العكس» | — |

**القواعد:**
1. لا يغيّر العكس حالة الدفعة إلا عبر `PaymentBatchTransitions`، ويُضاف `Pending→Voided` و`Failed→Voided` و`Rejected→Voided`.
2. سجل المراجعة «المغلق» الذي يُنشئه العكس اليوم بلا فاعل ([PayrollRunHandlers.cs:327-333](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)) يُحذف. والأثر في `PayrollReversalRequest` و`EntityAuditLog`.

---

## Out of scope

- فروق الحضور بعد الإغلاق، وحملها إلى الدورة التالية، واستردادها، وبوابة الحضور قبل الحساب ← **PLAN-57** (عقد التسليم §4–5). هذه الخطة توفّر لها الحراس والتحرير فقط.
- كيان فترة الرواتب وتاريخ القطع، والإجازة بلا أجر المعتمدة بعد ترحيل فترتها، ومحرك الفروق الرجعية، وإرجاع خصم إجازة ملغاة بعد تطبيقه ([InboundEventHandlers.cs:430](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))، ← الموجة 2 (**PAY-H**).
- استرجاع مال مسدَّد عبر القناة، وإشعارات `Ack`/`Settle` ← الموجة 2 (**PAY-I**).
- مسار الدفع الموحد، والأربع عيون للحجز والتنبيه، والاعتماد المزدوج ← **PLAN-58**.
- ذرية `Save→Publish→Save` في الترحيل ([PayrollRunHandlers.cs:260-264](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)) ← **PLAN-40**.
- حساب نهاية الخدمة (G-PAY-14) ومفتاح تشغيل `FinalSettlement` ← **PLAN-64**.
- حمل الخصم الزائد عن الراتب مع سقف قانوني (G-PAY-11) ← الموجة 2 (**PAY-E**).
- إعادة كتابة سكربتات القبول التي تلغي بـ SQL (AC-21..23) ← **PLAN-59**، باستعمال `CancelRun` من هذه الخطة.

## Acceptance scenarios

على الملف الشبيه بالإنتاج، `AcPay56RunStateMachineTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-PAY-56")`، فترة فريدة لكل سيناريو):

| ID | Positive | Negative |
|----|----------|----------|
| AC-PAY-56a | دورة فيها تسوية معتمدة +50,000 لموظف و−20,000 لآخر ← تنجح في التحقق، و`TotalNetPay` بعد الترحيل = المعتمد = مجموع صافي القسائم | — |
| AC-PAY-56b | معتمد يرفض بملاحظة ← التشغيل `Draft` و`Revision=2`. المعالج يضيف تسوية، ويعيد الحساب والتحقق والإرسال، ومعتمد يعتمد | رفض بلا ملاحظة ← 400. إعادة حساب تشغيل `Approved` بلا رفض ← 400 |
| AC-PAY-56c | إلغاء تشغيل `Calculated` فيه قسط سلفة وغياب وتسوية ← الثلاثة تظهر في تشغيل جديد للفترة نفسها | إلغاء تشغيل `Posted` ← 400. المعالج يلغي تشغيلاً `Approved` ← 403 |
| AC-PAY-56d | — | `validate` على تشغيل `Approved` ← 400 والحالة كما هي |
| AC-PAY-56e | `a-payroll-reverser` يطلب عكس تشغيل مرحّل لم تُرسل دفعته بسبب، و`payroll-approver` يعتمد ← التشغيل `Reversed`، والدفعة `Voided`، والمدخلات حرة، وتشغيل اعتيادي جديد للفترة ممكن | `a-payroll-reverser` يقرر طلبه ← 403. عكس تشغيل دفعته `Submitted` (بعد `process`) ← 400 برسالة D-PAY-16. وقاعدة «المقرر ≠ الطالب ≠ المُرحِّل» يثبتها `Pay056ReversalSodTests` على المعالج، لأن الكتالوج بلا شخصية تجمع العكس والاعتماد قبل PLAN-58 |
| AC-PAY-56f | تصحيح لتشغيل مرحّل بتسوية موجّهة −30,000 ← يمر بالتحقق، وينشئ خصم استرداد 30,000 يُطبَّق في الدورة التالية | تسوية غير موجّهة لا تدخل التصحيح |

اختبارات وحدة: `Pay056NetFormulaTests` (كل المستهلكين يعطون الصافي نفسه)، `Pay056TransitionTests` (مصفوفة كل حالة × كل دالة)، `Pay056ReversalSodTests`، `Pay056NetComputationArchitectureTests`.

اختبارات تكامل على PostgreSQL:
- رفض متزامن مع اعتماد على التشغيل نفسه ← واحد ينجح والآخر تعارض `xmin`.
- إلغاء أثناء مهمة جارية ← المدخلات حرة ولا سطر يتيم بعد انتهاء العامل.
- الفهرس الجديد: تشغيل اعتيادي ثانٍ بعد عكس الأول ينجح، وتصحيحان معلّقان للمصدر نفسه يفشل الثاني.
- `IPayrollReversed` في صندوق الصادر في معاملة العكس نفسها.

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay056|FullyQualifiedName~Pay003|FullyQualifiedName~PayrollFinancialInvariants|FullyQualifiedName~Pay001|FullyQualifiedName~OutboxPublishOrdering"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Pay056|FullyQualifiedName~Pay003|FullyQualifiedName~PayrollRunLifecycle"
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcPay56"
```

## Definition of done

- دورة بتسويات موجبة وسالبة تُتحقق وتُرحَّل، والمرحَّل = المعتمد.
- الرفض يعيد الفتح، والإلغاء يحرر الفترة والمدخلات، وكلاهما بسبب مسجّل.
- لا انتقال حالة بلا حارس، ومصفوفة الانتقالات مغطاة باختبارات.
- العكس بطلب ومقرر مختلف عن الطالب والمُرحِّل، وحدثه في صندوق الصادر.
- AC-PAY-56a..f = Pass في `acceptance-evidence/AC-PAY-56/`.
- أسطر «الحالة» لـ G-PAY-03 وG-PAY-04 وG-PAY-06 محدّثة، وG-PAY-07 «جزئي — PLAN-56» مع ما بقي للموجة 2.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-56-pay-run-state-machine.md` |
| Domain | `PayrollNet.cs`، `PayrollRunTransitions.cs`، `PayrollRunRevision.cs`، `PayrollReversalRequest.cs`؛ تعديل `PayrollRun.cs`، `SalaryLine.cs`، `PayrollAdjustment.cs`، `PayrollRules.cs`، `PayrollRunReconciliation.cs`، `PaymentBatchStatus`/`PaymentOrderStatus` |
| Application | `PayrollInputRelease.cs`، `CancelPayrollRunCommand.cs`، `RestartCalculationCommand.cs`، `Features/PayrollRuns/Reversal/*`؛ تعديل `PayrollRunHandlers.cs`، `CorrectionRunHandlers.cs`، `PayrollRunJobHandlers.cs`، `PayrollPartitionInputLoader.cs`، `PayrollRunParallelCalculator.cs` |
| Contracts | `IPayrollReversed.cs`؛ مستهلك في Dev.Analytics |
| Infrastructure | ترحيلات `PAY056_NetFormula` و`PAY056_RunLifecycle` (الحقول، والفهارس، والكيانات) |
| Web | نقاط API الجديدة، وأزرار `Details.cshtml`، وشاشة طلبات العكس؛ حذف `AddSalaryLine` |
| Tests | `Pay056*Tests.cs` (وحدة)، `Pay056RunLifecyclePostgreSqlTests.cs` (تكامل)، `AcPay56RunStateMachineTests.cs` (قبول؛ مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41) |
| Docs | `docs/EVENT_PUBLISHER_INVENTORY.md` (صف `IPayrollReversed` بنمط `SingleSave`، شرط R4 في حارس PLAN-40)، `docs/PAY_001_APPROVAL_BYPASS.md` (مخطط الحالات)، `docs/PAYROLL_RUN_RESUME.md:31` (الإلغاء وإعادة الحساب)، `docs/PAYROLL_RULES.md` (الصافي والتصحيح)، أسطر «الحالة» في `docs/PAY_001_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تغيير معادلة التحقق يكشف أسطراً تاريخية «غير متطابقة» | التحقق يعمل على التشغيلات غير المجمدة فقط، والترحيل يملأ `TotalAdjustments` للأسطر القديمة من المكوّنات |
| حذف أسطر النسخة المرفوضة يمحو أثراً تدقيقياً | `PayrollRunRevision` يحفظ الإجماليات وبصمة الأسطر، و`EntityAuditLog` يسجّل الحذف |
| `private set` على `Status` يكسر بذوراً واختبارات تضبط الحالة مباشرة | دالة اختبار داخلية `ForTesting.SetStatus` في مشروع الاختبارات فقط، والجرد في 0.4 |
| الحدث يخالف حارس PLAN-40 (R1/R4) | يُكتب في صندوق الصادر داخل معاملة العكس نفسها قبل الحفظ الواحد، وله صف في `EVENT_PUBLISHER_INVENTORY.md`، و`OutboxPublishOrderingArchitectureTests` ضمن التحقق |

## Effort estimate

8–10 أيام عمل: الصافي والترحيل والتصحيح (2.5)، الرفض والإلغاء والتحرير وإعادة الحساب (2)، الحراس وحذف السطر اليدوي (1)، العكس المعتمد والحدث (2)، الاختبارات والقبول والوثائق (1–2).

## ملاحظات التنفيذ والانحرافات

نُفذت في جلسة واحدة يوم 2026-11-25. التفاصيل في [plan56-final-report.md](../../reports/plan56-final-report.md)، والقواعد في
[PAYROLL_RULES.md](../PAYROLL_RULES.md) و[PAY_001_APPROVAL_BYPASS.md](../PAY_001_APPROVAL_BYPASS.md) و[PAYROLL_RUN_RESUME.md](../PAYROLL_RUN_RESUME.md).
لا انحراف يؤجّل جزءاً من الخطة.

**ما يخص الصافي:**
- **تقريب الصافي في التحقق والترحيل** بتقريب نسخة الإطار المثبتة على التشغيل (`PayrollNet.Net(…, round)`)، فنسخة بلا منازل عشرية
  مع تسوية بكسور تطابق نفسها. بلا نسخة مثبتة تُؤخذ الأجزاء مقرَّبة كما هي.
- **ملء `TotalAdjustments`** من مكوّنات التسوية التي تحمل مرجعاً فقط؛ البند الذي لا مكوّن تسوية له يبقى صفراً، ولا يُمسّ صافٍ
  مخزَّن. بند رُحّل قبل الخطة يبقى بصافيه الذي رُحّل به.
- **بذرة العرض** تكتب مكوّن الاستقطاع سالباً والصافي عبر `PayrollNet` للتشغيلات الجديدة؛ صفوف العرض القائمة لم تُمسّ، فتشغيلها
  «المعتمد» المبذور قبل الخطة لا يجتاز تحقق الترحيل (مكوّناته لا تطابق أرقامه)، ويُلغى ولا يُرحَّل.
- **واجهة القسيمة** `GET api/payroll/payslips/{line}` كانت تجيب 500 (حلقة في التسلسل) قبل الخطة؛ صارت تجيب أرقام البند المخزَّنة
  ومكوّناته.

**ما يخص الحالات والإلغاء:**
- **الأجوبة:** 400 لملاحظة أو سبب ناقص أو حالة لا يغادرها الفعل، 403 لمن لا تقبله حالة التشغيل، 404 لتشغيل جهة أخرى، 409 لطلب
  خسر أمام آخر. وكل كتابة في الواجهة البرمجية تتحقق من رمز antiforgery.
- **سياسة الإلغاء** `Payroll.RunCancel` (المعالج والمعتمد)، وD-PAY-18 يُطبَّق في المعالِج على حالة التشغيل.
- **`Status` بلا setter** (`init` عند الإنشاء): المنشئان يكتبان «مسودة»، والبذرتان تكتبان الحالة عند الإنشاء، والاختبارات التي
  تحتاج حالة لم يبلغها التشغيل بأفعاله تضبطها بـ`ForTesting.SetStatus` في مشاريع الاختبار وحدها.
- **التشغيل الملغى يحتفظ ببنوده** سجلاً له؛ الرفض وإعادة البدء يحذفانها (وبصمة النسخة المرفوضة محفوظة).
- **إعادة البدء تُبقي نسخة الإطار المثبتة** على التشغيل.
- **تشغيل له قرار تمويل أو دفعة جزئية لا يُلغى ولا يُعكس هنا** (يُرفض برسالة تسمّي مسار الصرف): مصير صفوف متأخراته لـPLAN-58.
- **تحرير المدخلات وحذف البنود جمل مجمَّعة** داخل معاملة المستدعي، وصف تدقيق واحد يلخّص كلاً منهما.
- **استئناف قرأ مهمته قبل إلغاء مثبَّت يُرفض** (أخذ المهمة بجملة شرطية داخل معاملة الحفظ)، والإيقاف وفشل العامل لا يغيّران
  مهمة أُلغيت: من المراجعة المستقلة.

**ما يخص التصحيح:**
- **باب واحد للتصحيح:** `POST api/payroll/runs/{id}/corrections` (أُضيف) وزر «تصحيح رجعي»؛ الإنشاء العام للتشغيل يرفض النوع
  «تصحيح».
- **استرداد الفرق السالب يُنشأ مع بند التصحيح** (كما في الخطة)، **ولا يأخذه تشغيل اعتيادي إلا بعد ترحيل التصحيح**: قبل ذلك قد
  يُرفض التصحيح أو يُلغى أو يُعاد حسابه، وكلٌّ منها يحذف استرداده الحر. من المراجعة المستقلة (كان يأخذه أول تشغيل ولو لم
  يُرحَّل التصحيح).
- **موظف له بند في التشغيل المرجعي ولا ملف أجر نافذاً له الآن** يحمل بندَه آخرُ نسخة من ملفه؛ لا تُترك تسوية معتمدة خارج التصحيح.
- **تصحيح لا يُرحَّل إن لم يعد تشغيله المرجعي مرحّلاً.**
- **استرداد أكبر من صافي الشهر يُستهلك كله** (ترحيل ما يزيد على الراتب: PAY-E، الموجة 2).

**ما يخص العكس:**
- **D-PAY-16 يُفحص عند القرار لا عند الطلب:** الدفعة قد تخرج أو تعود بين الاثنين، والطلب هو سجل أن أحداً طلب. فطلب على تشغيل
  خرج ماله يُقبل طلباً ويُرفض اعتماده ثم يُرفض بملاحظة.
- **لا دفعة عكس تُكتب** (الخطة قالت في AC-PAY-Cc «تُعالج دفعة العكس»): الدفعة التي لم تخرج تُبطَل `Voided`، والتي خرجت تمنع العكس.
- **تشغيل له تصحيح قائم لا يُعكس، وتصحيح مرحّل استُرد فرقه في تشغيل لاحق لا يُعكس:** من المراجعة المستقلة.
- **المرحِّل لتشغيل رُحّل قبل الخطة** هو آخر من كتبه (`KnownPoster`)؛ التشغيل المبذور لا مرحِّل معروفاً له، فيُستثنى الطالب وحده.

**ما يخص الخادم والتراجع:**
- **الحالات الثلاث الجديدة (دفعة `Voided`، أمر `Cancelled`، قرار `Cancelled`) تُخزَّن أسماءً لا أرقاماً،** وإصدار PLAN-55 لا يقرأ
  صفاً فيه اسم لا يعرفه (يفشل الاستعلام). فبعد أن يُلغي هذا الإصدار تشغيلاً أو يعتمد عكساً يكون طريق العودة الإصلاح إلى الأمام؛
  التفصيل في [OPS_VPS_SINGLE_HOST.md](../OPS_VPS_SINGLE_HOST.md).
- **العرض يرفض حساب تشغيل جديد** (ملفات أجره بلا نظام قانوني، PLAN-55)، فأُثبت الرفض الذي يعيد الفتح بـAC-PAY-56b، وأُثبت الإلغاء
  على الخادم على تشغيل تركه الرفض «قيد الحساب» بمهمته.
- **فحوص الخادم لا ترسل اعتماد عكس أبداً،** وقرار الطالب نفسه يُرسَل رفضاً (القاعدة واحدة للاثنين).
