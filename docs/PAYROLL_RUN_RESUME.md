# تشغيل رواتب ذري قابل للاستئناف (PAY-003)

## المراحل (Job Phase)

```text
Snapshot → Calculate → Validate → (Approve يدوي) → Post → Pay
```

| Phase | المعنى |
|---|---|
| Snapshot | تجميد قائمة الموظفين النشطين + `ProfileVersion` + pin إعدادات PAY-002 |
| Calculate | أقسام (افتراضي 200 موظف) مع checkpoint لكل موظف |
| Validate | تسوية المكوّنات/الصافي مقابل `TotalNetPay` وتجميد النتائج |
| Post | قفل تشغيلي + دفعة `PaymentBatch` بحالة `Pending` (ليست صرفاً) |
| Pay | `ProcessPaymentBatch` → `Processed` + `ProcessedAt` |

حالات `PayrollRun` البشرية تبقى: Draft / Calculated / UnderReview / Approved / Posted / Reversed، مع Calculating / Validating / Cancelled أثناء الـjob.

## Job / Partition / Checkpoint

- `PayrollRunJob` 1:1 مع التشغيل الحي أثناء المعالجة؛ حالات: Pending / Running / Paused / Completed / Failed / Cancelled.
- `LeaseOwner` + `LeaseUntil`: طلب حساب مكرر مع lease ساري يُرفض برسالة عربية؛ lease منتهٍ أو Paused يستأنف نفس الـjob.
- فهرس فريد جزئي: job نشط واحد لكل `PayrollRunId` (`Status` in Pending/Running/Paused).
- `PayrollRunPartition`: نطاق موظفين بحجم قابل للتهيئة؛ بعد كل موظف ناجح يُحدَّث `CursorEmployeeId` و`ProcessedCount`/`LastCheckpointAt`.
- ذرّية الموظف: معاملة واحدة = `SalaryLine` + مكوّنات + ربط استقطاعات/تسويات/غياب. `IX_SalaryLines_Run_Employee` يمنع التكرار عند الاستئناف.

## Pause / Resume / Cancel

- Pause: يضع الـjob في `Paused` بجملة شرطية واحدة (فقط وهو `Running`)؛ الحلقة تخرج عند الفحص التالي، وما في يد العامل من
  حزم لا يُكتب (حفظ العامل يتحقق داخل معاملته أن المهمة ما زالت `Running`).
- Resume: يأخذ المهمة بجملة شرطية واحدة داخل معاملة الحفظ (`ClaimJobForCalculationAsync`: فقط إن كانت `Paused` أو `Failed`
  أو `Pending` أو `Running` بلا lease ساري)، ثم يستأنف من partitions غير المكتملة؛ الأسطر الموجودة تُتخطى. استئناف قرأ المهمة قبل
  أن يُثبَّت إلغاء أو إعادة بدء يُرفض ولا يكتب شيئاً (PLAN-56).

### الإلغاء وإعادة البدء وتحرير المدخلات (PLAN-56)

- **الإلغاء إلغاءٌ للتشغيل** (`CancelPayrollRunCommand`؛ `POST api/payroll/runs/{id}/cancel`، و`POST api/payroll/jobs/{id}/cancel`
  يفوّض إليه): لأي تشغيل غير مرحّل ولا معكوس ولا ملغى، بسبب إلزامي (400 بلا سبب). D-PAY-18: المعالج يلغي ما لم يُرسل
  للمراجعة، والتشغيل «قيد المراجعة» أو «معتمد» يلغيه حامل `Payroll.Approve` (403 لغيره). في معاملة واحدة: تُوقَف مهام
  التشغيل وأقسامها (`StopRunJobsAsync`، وينتظر حفظ العامل الجاري ثم يمسك صف المهمة)، تُحرَّر المدخلات، ويصير التشغيل
  `Cancelled` مع مَن ألغاه ومتى ولماذا، وصفّ قرار `Cancelled` في `PayrollRunApprovals`. أسطر التشغيل الملغى تبقى سجلاً له،
  وفترته تصير حرة لتشغيل جديد.
- **سكربتات القبول تلغي عبر الواجهة (PLAN-59):** `scripts/acceptance-payroll-determinism.ps1` (AC-21) و`acceptance-payroll-resume-chaos.ps1`
  (AC-22) تلغيان تشغيلاتهما بـ`POST api/payroll/runs/{id}/cancel` بسبب ورمز مكافحة التزوير، فيتحرر الشهر والمدخلات كما أعلاه.
  لم يبق فيهما SQL يكتب (`Invoke-PayrollSql` حُذف)؛ وكل سكربت يأخذ شهره من صنفه (`-PeriodStart`) من سجل الأشهر
  `testdata/acceptance/payroll-months.json`.
- **تحرير المدخلات** (`PayrollInputRelease`): يفرّغ `AppliedInRunId` للاستقطاعات والغياب و`PayrollRunId` للتسويات لذلك التشغيل،
  ويحذف استقطاعات «استرداد التصحيح» التي أنشأها التشغيل ولم يطبّقها تشغيل. لا يمس غير علامات التطبيق. يستدعيه الإلغاء والرفض
  وإعادة البدء وإعادة حساب التصحيح والعكس، داخل معاملة المستدعي، وصفّ تدقيق واحد يلخّص ما حُرّر.
- **إعادة بدء الحساب** (`RestartCalculationCommand`؛ `POST api/payroll/jobs/{id}/restart`): لمهمة `Failed` أو `Paused` على تشغيل
  غير مجمَّد. تُلغى المهمة، تُحذف الأسطر، تُحرَّر المدخلات، يعود التشغيل `Draft` (بنسخة الإطار المثبتة نفسها)، ثم يبدأ حساب
  بلقطة جديدة عبر `StartPayrollRunJobHandler` وبوابة PLAN-54. هذا المخرج من «تعارض بصمة المدخلات»: الاستئناف يعيد اللقطة
  نفسها فيتكرر التعارض. إن رُفض الحساب الجديد (بوابة) فالجواب يقول إن إعادة التهيئة تمّت ولماذا لم يبدأ الحساب.
- **رفض المراجع** يعيد التشغيل `Draft` بنسخة `Revision + 1` بعد حفظ النسخة المرفوضة في `PayrollRunRevisions` وحذف أسطرها
  وتحرير مدخلاتها؛ ثم يُحسب التشغيل نفسه من جديد.

## Validate والتجميد

بعد نجاح Validate: `ValidatedAtCalculate` + `ResultsFrozen`؛ `ValidateCanModifyLines` يرفض التعديل. Post يرفض إن لم يمر Validate أو بقيت partitions غير Completed.

## Post مقابل Pay وعكس تعويضي

- **Post** لا يضع `ProcessedAt`.
- **Pay** عبر `ProcessPaymentBatchCommand`.
- **Reverse** (PLAN-56): طلب بسبب (`Payroll.Reverse`) يقرر فيه شخص آخر ليس الطالب ولا المرحِّل (`Payroll.Approve`). الاعتماد
  يُبطل الدفعة التي لم تخرج (`Voided`) ويلغي أوامرها المسودة ويحرّر المدخلات ويجعل التشغيل `Reversed` وينشر `IPayrollReversed`
  عبر الـoutbox في المعاملة نفسها؛ الأسطر الأصلية تبقى. لا تُكتب دفعة `BatchKind=Reversal` بعد الآن. تشغيل خرجت دفعته إلى
  المصرف لا يُعكس (D-PAY-16): يُصحَّح بتشغيل تصحيح.

## حدود PERF-001 / INT-002

- توازي العمال وbulk IO = **PERF-001** — انظر [PAYROLL_BATCH_PERFORMANCE.md](PAYROLL_BATCH_PERFORMANCE.md).
- Inbox موحّد عبر المنصة = INT-002 (مكتمل)؛ PAY-003 يحتفظ بـidempotency محلي للرواتب كدفاع مكمّل.

## اختبارات البوابة

`Pay003ResumablePayrollRunTests` + `Pay003ReconciliationTests`: ذرّية، استئناف، double-start، pause/resume، validate، partial post، تعارض إصدار، عكس تعويضي.

**PLAN-06 (AC-21 / AC-22):**

| AC | سكربت / اختبار | أدلة |
|---|---|---|
| AC-21 | `scripts/acceptance-payroll-determinism.ps1` + `Pay021DeterminismPostgreSqlTests` | `acceptance-evidence/AC-21/` — provenance، diff فارغ |
| AC-22 | `scripts/acceptance-payroll-resume-chaos.ps1` (`RUN_CHAOS=true`) + `Pay022ResumePostgreSqlTests` | `acceptance-evidence/AC-22/` — partitions، line counts، lease |

- **Provenance API:** `GET /api/payroll/runs/{id}/provenance` و `GET /api/payroll/jobs/{jobId}/input-snapshots` للمدقق (PAY-002 hash + PAY-003 snapshots).
- **Chaos runbook:** `nfr-chaos-safe-abort.ps1` — checklist؛ الاستئناف الحي AC-22 عبر `acceptance-payroll-resume-chaos.ps1`.
