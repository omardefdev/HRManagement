# PLAN-59 — إثبات دورة رواتب كاملة بإعدادات الإنتاج (PAY-D)

**Scope:** PAY — [دراسة الفجوات](../PAY_001_GAP_STUDY.md): G-PAY-05 (إعادة كتابة AC-12، واختبارات التكامل الحمراء، والإثبات الختامي لمرحلة الرواتب)  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-34 (إصلاح مضيفات التكامل)، PLAN-55، PLAN-56، PLAN-57، PLAN-58. ويستهلك: PLAN-53 (معاملة تغيير الراتب)، PLAN-54 (الإطار وبذرة `LegalRegimeId`)، PLAN-37 (الملف الشبيه بالإنتاج)  
**Related cards:** PAY-001، PAY-003، PAY-004، EXT-003، FRD-001، HCM-002، PILOT_DEPLOYMENT

> **نُقل من هذه الخطة:** تسجيل عميل أنظمة الخدمة المزيف في مضيف تكامل الرواتب، ومضيف HR.Contracts في اختبار الدخان ← **PLAN-34** (لأن `main` لا يُدفع بـ CI أحمر). و`LegalRegimeId = "law50"` في بذرة القبول ← **PLAN-54** (المرحلة 0.7)، لأن كل خطط الرواتب قبل هذه تحسب في القبول. هذه الخطة لا تعيد تنفيذ شيء منهما: تتحقق من أن الاختبارات خضراء، وتضيف اختبار مسار الحجب وإعادة كتابة AC-12.

## Goal

- أن تُثبت دورة رواتب شهرية كاملة على الملف الشبيه بالإنتاج، عبر HTTP وحده وبلا SQL، وبشخص مختلف لكل دور:
  - الإطار القانوني (PLAN-54)، وحسابات مصرفية محققة (PLAN-55)، وتغيير راتب معتمد (PLAN-53)، وإغلاق فترة حضور يغذي الخصم (PLAN-57).
  - ثم حساب ← تحقق ← إرسال ← رفض ← إعادة فتح ← إعادة حساب ← اعتماد ← مراجعة مستقلة ← ترحيل ← معالجة بحجز موظف واحد ← ملف مصرفي ← إفراج ← ملف تكميلي ← تأكيد المصرف.
- أن يثبت AC-12 فصل الواجبات للسبب الصحيح.
- أن تكون اختبارات التكامل الأربعة عشر للرواتب خضراء.
- ألا يكتب أي سكربت قبول للرواتب SQL.

هذا مؤشر النجاح «دورة رواتب واحدة مكتملة» و«إغلاق فترة حضور واحدة مرتبطة بالرواتب» في [PILOT_DEPLOYMENT.md:20-21](../PILOT_DEPLOYMENT.md).

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-PAY-21 — معنى «دورة مكتملة» في الموجة 1 | مرحَّلة، ودفعتها مُرسلة بملف مصرفي مجاميعُ ضبطه مطابقة، وتأكيد المصرف مسجَّل بشخصين بمرجع الكشف (PLAN-58 البند 7)، وقسائمها مطابقة للأوامر. واستيراد إشعار القناة وكشف المصرف آلياً في الموجة 2 (**PAY-I**) | «مسدَّد» لا يُسجَّل إلا بمرجع مصرفي وشخصين |
| D-PAY-22 — شهر الدورة في القبول | أول شهر تقويمي منتهٍ بلا تشغيل اعتيادي حي في الجهة A، بدءاً من `ACCEPTANCE_PAYROLL_CYCLE_FROM` (الافتراضي: قبل 12 شهراً من التنفيذ). ويتخطى الأشهر المحجوزة في `testdata/acceptance/lva057-periods.json` (PLAN-57) وأي شهر له ملخص حضور مغلق في الجهة A (`GET …/periods/{M}`)، لأن أيامه مقفلة. الشهر منتهٍ لأن الإدخال الجماعي للحضور لا يكتب المستقبل (PLAN-57). وإن نفدت الأشهر يُطلب `scripts/acceptance-reset.ps1` برسالة صريحة | ثلاثة تنفيذات متتالية تعطي ثلاثة أشهر متتالية |
| D-PAY-3، D-PAY-4، D-PAY-5 | كما في PLAN-58 | — |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-59-pay-full-cycle-production-proof` | — |
| 0.2 | الاعتماديات | PLAN-53..58 مدموجة. تُدوَّن أسماء نقاط API التي أدخلتها PLAN-53 (طلب تغيير الراتب واعتماده)، وPLAN-54 (قراءة الإطار النافذ واستيراده)، وPLAN-57 (الإدخال الجماعي، والإغلاق، و`receipts`، و`attendance-status`) | الأسماء في `reports/plan59-baseline.md` مع SHA كل خطة |
| 0.3 | اختبارات التكامل | `dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Payroll\|FullyQualifiedName~Pay0\|FullyQualifiedName~Perf001\|FullyQualifiedName~EndToEndReleaseWorkflow"` مرة بلا `RUN_TESTCONTAINERS` ومرة به | قائمة الأحمر الباقي بعد إصلاح المضيفات في PLAN-34 وسببه |
| 0.4 | جرد SQL القبول | `Select-String -Path scripts/acceptance-payroll-*.ps1 -Pattern 'UPDATE\|INSERT\|DELETE\|Invoke-PayrollSql'` | القائمة في التقرير |

---

## In scope

### 1. اختبارات التكامل الأربعة عشر خضراء (G-PAY-05)

**المشكلة:**
- كل اختبار يحسب تشغيلاً اعتيادياً كان يفشل في مضيف بلا `IHrCoreServiceRegimeClient` ([PayrollIntegrationTestSetup.cs:16-43](../../src/Tests/HRManagement.IntegrationTests/PayrollIntegrationTestSetup.cs)؛ [PayrollRegimeGovernance.cs:19-23](../../src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs)). وكان `EndToEndReleaseWorkflowSmokeIntegrationTests` ([:94](../../src/Tests/HRManagement.IntegrationTests/EndToEndReleaseWorkflowSmokeIntegrationTests.cs)) يفشل أيضاً بـ `IHrCoreActiveRegimeClient` غير مسجّل ([plan25-baseline.md:34](../../reports/plan25-baseline.md)). أصلحت PLAN-34 المضيفين بـ `FakeHrCoreServiceRegimeClient` و`FakeHrCoreActiveRegimeClient`.
- لا اختبار يثبت أن العميل حين يعيد `Blocked` يوقف الحساب، أو أن غيابه مع `ServiceRegimes:BlocksFinalPayroll=true` يرفض. وتركته PLAN-34 لهذه الخطة.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `Pay059RegimeBlockerTests` | `src/Tests/HRManagement.IntegrationTests/` | بالعميل المزيف من PLAN-34: (1) `Payroll:Blockers:Mode=BlockRun` وعميل يحجب موظفاً ← رفض الحساب برسالة تسمّي الموظف. (2) مضيف بلا عميل و`BlocksFinalPayroll=true` ← رفض. (3) `HoldEmployee` (الافتراضي بعد PLAN-58) ← لا رفض، ويثبته `Pay058` هناك |
| ما يبقى أحمر في 0.3 | حسب السبب | يُصلح هنا إن كان في الرواتب. وإن كان في خدمة أخرى يُوثَّق ويُحال إلى خطة المالك في `reports/plan59-baseline.md` |
| `Pay059PayrollSodHandlerTests` | `src/Tests/HRManagement.IntegrationTests/` | يثبت قواعد المعالج التي لا تصل إليها شخصية حقيقية بعد حارس التعارض في PLAN-58: المنشئ لا يعتمد («لا يمكن لمنشئ التشغيل اعتماد نفس التشغيل.»، [PayrollRunHandlers.cs:105-147](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs))، والمعتمد لا يرحّل (رسالة SoD قبل رسالة سجل المراجعة، `:199-214`) |

**القواعد:** الاختبارات الأربعة على PostgreSQL (`Pay021`، و`Pay022.Pause_Resume…`، و`Pay023`، و`Perf001…PostgreSql`) تُشغَّل بـ `RUN_TESTCONTAINERS=true` وتُعدّ جزءاً من «أخضر».

### 2. AC-12 يثبت فصل الواجبات للسبب الصحيح (G-PAY-05)

**المشكلة:**
- AC-12 يرسل للمراجعة بلا حساب ولا تحقق ([Ac12PayrollSodTests.cs:34-60](../../src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs))، فيعود 400 بشرط الإرسال ([PayrollRun.cs:67-70](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)).
- ويتوقع 400 فيها «ترحيل» حين يرحّل المعتمد (`:71-76`)، والمعتمد لا يملك دور المُرحِّل فيعود 403.
- والفترة ثابتة (`:25-26`)، ولا `[RequiredAcceptance]`.

**التصميم:** إعادة كتابة `Ac12PayrollSodTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-12")`):

| الخطوة | الشخصية | المتوقع |
|--------|---------|---------|
| إنشاء لشهر فريد (D-PAY-22)، ثم `calculate` و`validate` | `payroll-processor` | 200، `ResultsFrozen=true` |
| اعتماد من المنشئ | `payroll-processor` | 403 (لا دور اعتماد) |
| اعتماد من حامل الأدوار الثلاثة | `a-payroll-multirole` (PLAN-58) | 403 برسالة التعارض |
| إرسال للمراجعة، ثم اعتماد | `payroll-processor` ثم `payroll-approver` (و`a-payroll-approver-2` إن طُلب اعتماد ثانٍ) | 200 |
| ترحيل من المعتمد | `payroll-approver` | 403 (لا دور ترحيل) |
| ترحيل قبل المراجعة المستقلة | `a-payroll-poster` | 400 «يجب إغلاق سجل مراجعة الرواتب قبل الترحيل.» |
| مراجعة مستقلة: فتح وإغلاق | `auditor` (PLAN-58، D-PAY-3) | 200 |
| ترحيل | `a-payroll-poster` | 200، والتشغيل `Posted` |

وفي `finally`: إلغاء التشغيل عبر `CancelRun` (PLAN-56) إن لم يُرحَّل، فلا يحجز الشهر. ورسائل المعالج نفسها يثبتها `Pay059PayrollSodHandlerTests` (البند 1).

### 3. سكربتات القبول بلا SQL (G-PAY-05، مع G-PAY-04 وG-PAY-07)

**المشكلة:** سكربتا الحتمية والاستئناف يلغيان التشغيل ويحرران المدخلات بـ SQL ([acceptance-payroll-determinism.ps1:77-90](../../scripts/acceptance-payroll-determinism.ps1)، [acceptance-payroll-resume-chaos.ps1:87-101](../../scripts/acceptance-payroll-resume-chaos.ps1)). وسكربت المراجعة يُدخل سجلاً مغلقاً بـ SQL ([acceptance-payroll-audit-fixture.ps1:28-46](../../scripts/acceptance-payroll-audit-fixture.ps1))، وتحذفه PLAN-58.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `Cancel-PayrollRunInDb`، `Cancel-TestRun`، `Reset-ReservedPeriod` | السكربتان | تُستبدل بـ `POST api/payroll/runs/{id}/cancel` بسبب (PLAN-56)، الذي يحرر المدخلات |
| `Invoke-PayrollSql` | السكربتان | يُحذف |
| الحارس | — | منع SQL في سكربتات القبول باختبار معماري تملكه **PLAN-65** (`AcceptanceScriptSqlWriteTests`). هذه الخطة تزيل SQL الرواتب فقط، فيمر حارس PLAN-65 دون استثناء لها |
| AC-21/22/23 | `Ac21PayrollDeterminismTests`، `Ac22PayrollResumeAfterKillTests`، `Ac23PayrollFreezeTests` | `[RequiredAcceptance]`، وشهر فريد لكل تنفيذ، وتنفيذها على الملف الشبيه بالإنتاج |

### 4. الدورة الكاملة على الملف الشبيه بالإنتاج (G-PAY-05)

**التصميم:** `AcPay59FullCycleTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-PAY-59")`، مساعد `PayrollCycleDriver` في مشروع القبول). كل خطوة طلب HTTP يُلتقط بـ `CaptureHttp`، وكل تحقق قراءة API تُكتب بـ `WriteDbChecks`. لا SQL.

| # | الخطوة | الشخصية | ما يُثبت |
|---|---------|---------|----------|
| 1 | اختيار الشهر M (D-PAY-22) وقراءة الإطار النافذ لـ `LAW_50_2004` في M | `payroll-processor` | إن لم يوجد: استيراد `law50-interim.json` بسريان أول M، ثم اعتماد (`payroll-approver`) ومصادقة (`a-payroll-approver-2`) ونشر (`payroll-approver`)، كما في AC-PAY-54a |
| 2 | حساب مصرفي محقق لكل موظف في النطاق بلا حساب نافذ. وللموظف X: إحالة حسابه النافذ إن وُجد (من تنفيذ سابق)، ثم طلب حساب جديد بلا تحقق | `payroll-processor` ثم `payroll-approver` | كل موظف بحساب `Active` عدا X، وX بحساب `PendingVerification` |
| 3 | تغيير راتب الموظف Z بسريان أول M عبر معاملة PLAN-53 | الطالب والمعتمد كما تعرّفهما PLAN-53 | الملف الفعّال لـ Z بالراتب الجديد، وبالوحدة والنظام نفسيهما (PLAN-55). السريان من أول الشهر لأن تقسيط نسختين داخل الشهر للموجة 2 (**PAY-E**) |
| 4 | حضور M: إدخال جماعي `Present` لموظفي النطاق، ويوما غياب للموظف Y، ثم `POST …/periods/{M}/close` | `central-a` (مركزي: يغلق الجهة كلها بلا `orgUnitId`، PLAN-57 §1 القاعدة 3) | 200. ثم انتظار `GET api/payroll/attendance/receipts?periodStart=M` حتى يساوي العدد موظفي النطاق |
| 5 | إنشاء التشغيل، ثم `calculate` و`validate` | `payroll-processor` | `attendance-status.missingAttendance = []`، وسطر Y يحمل خصم غياب يومين، وسطر Z بالراتب الجديد، و`provenance.configId` = إطار الخطوة 1 |
| 6 | إرسال، ثم رفض بملاحظة | `payroll-processor` ثم `payroll-approver` | التشغيل `Draft` و`Revision=2` (PLAN-56) |
| 7 | تسوية يدوية +25,000 للموظف Y، واعتمادها، ثم `calculate` و`validate` و`submit` | `payroll-processor` ثم `payroll-approver` (اعتماد المدخل، PLAN-54) | التحقق ينجح بتسوية غير صفرية (PLAN-56)، وصافي Y = إجمالي − خصومات + 25,000 |
| 8 | اعتماد (واعتماد ثانٍ إن قال `GET runs/{id}` إن المطلوب 2) | `payroll-approver`، `a-payroll-approver-2` | `Approved` |
| 9 | فتح سجل المراجعة وإغلاقه | `auditor` | مغلق |
| 10 | ترحيل | `a-payroll-poster` | `Posted`، و`TotalNetPay` = المعتمد، ودفعة `Pending` بأوامر، وأمر X `Held` بسبب `NO-ACCOUNT` (PLAN-58) |
| 11 | معالجة الدفعة | `a-payroll-poster` | أوامر الباقين `Submitted`، وملف مصرفي `ControlCount` = عددهم و`ControlTotal` = مجموعهم، وX ليس في الملف |
| 12 | التحقق من حساب X | `payroll-approver` | الحجز يُرفع ويُثبَّت الحساب على سطر X وأمره (PLAN-58) |
| 13 | `submit-held` | `a-payroll-poster` | ملف تكميلي بأمر X وحده |
| 14 | القسائم | `payroll-processor` | لكل موظف: صافي القسيمة = مبلغ أمره، وΣ الأوامر = `TotalNetPay` |
| 15 | تسجيل تأكيد المصرف: `ack`، ثم طلب تسوية بمرجع الكشف واعتماده | `a-payroll-poster` ثم `payroll-approver` | الدفعة وأوامرها `Settled` (PLAN-58 البند 7) |

**القواعد:**
1. فشل أي خطوة قبل 10 يلغي التشغيل في `finally` عبر `CancelRun`، فيبقى الشهر صالحاً لتنفيذ لاحق.
2. بوابة الحضور على `Warn` الافتراضية (D-LVA-10). والاختبار يتحقق من `missingAttendance = []` صراحة، ولا يعتمد على الشدة.
3. لا تغيير على بيانات القبول قبل الاختبار إلا ما يفعله الاختبار نفسه عبر HTTP.

### 5. الوثائق ودليل التشغيل الشهري

| Artifact | المسار | الغرض |
|----------|--------|-------|
| دليل الدورة الشهرية | `docs/runbooks/payroll-monthly-cycle.md` (جديد) | الخطوات الخمس عشرة بالأدوار والشاشات، وما يفعله المشغّل عند: رفض، أو حجز، أو حاجز نظام، أو موظف بلا حساب، أو حضور ناقص |
| مؤشرات الإطلاق | [PILOT_DEPLOYMENT.md:20-21](../PILOT_DEPLOYMENT.md) | ربط المؤشرين بـ AC-PAY-59 ومعنى «مكتملة» (D-PAY-21) |
| PAYROLL_RUN_RESUME | [PAYROLL_RUN_RESUME.md:31](../PAYROLL_RUN_RESUME.md) | الإلغاء عبر `CancelRun` |

---

## Out of scope

- الاستيراد الآلي لإشعار التسوية وكشف المصرف، والمطابقة بالمرجع، وقسائم ESS بعد الترحيل ← الموجة 2 (**PAY-I**).
- تقسيط تغيير الراتب داخل الشهر وقواعد الحساب الأخرى (G-PAY-11) ← الموجة 2 (**PAY-E**).
- حساب نهاية الخدمة (G-PAY-14) والتسوية النهائية ← **PLAN-64**، ويُضاف إلى الدورة هناك.
- الفروق الرجعية والنقل بفرق راتب ← الموجة 2 (**PAY-G**، **PAY-H**).
- إعادة تشغيل كل قبول الموجة 1 حياً، وتصحيح الوثائق المتضاربة (G-PAY-22)، وتعميم منع SQL على كل سكربتات القبول ← **PLAN-65**.
- تشغيل القبول في الـ CI ← **PLAN-41**.

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-PAY-59a | الدورة الكاملة (البند 4) Pass بثلاثة تنفيذات متتالية على ثلاثة أشهر متتالية، بالعناصر الثمانية في كل مجلد دليل | — |
| AC-PAY-59b | — | داخل الدورة: الخطوة 11 لا تُرسل أمر X، وتوليد الملف مرة ثانية للإرسال نفسه لا ينتج ملفاً ثانياً |
| AC-12 | الترحيل بعد مراجعة مستقلة ينجح | المنشئ لا يعتمد (403)، وحامل الأدوار الثلاثة يُرفض (403 تعارض)، والمعتمد لا يرحّل (403)، والترحيل بلا مراجعة يُرفض (400) |
| AC-21، AC-22، AC-23 | Pass بلا SQL | — |

اختبارات تكامل: الأربعة عشر + `Pay059PayrollSodHandlerTests` + `Pay059RegimeBlockerTests`، منها الأربعة على PostgreSQL.

## Verification

```powershell
dotnet build HRManagement.slnx
Select-String -Path scripts/acceptance-payroll-*.ps1 -Pattern 'UPDATE|INSERT|DELETE|Invoke-PayrollSql'   # يجب ألا يعيد شيئاً
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Payroll|FullyQualifiedName~Pay0|FullyQualifiedName~Perf001|FullyQualifiedName~EndToEndReleaseWorkflow|FullyQualifiedName~Pay059"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Pay021|FullyQualifiedName~Pay022|FullyQualifiedName~Pay023|FullyQualifiedName~Perf001"
# الملف الشبيه بالإنتاج (PLAN-37):
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml --profile full-stack up -d
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
1..3 | ForEach-Object { dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcPay59" }
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~Ac12|FullyQualifiedName~Ac21|FullyQualifiedName~Ac22|FullyQualifiedName~Ac23"
pwsh scripts/acceptance-evidence-summarize.ps1
```

## Definition of done

- AC-PAY-59a ثلاث مرات متتالية، وAC-12 وAC-21..23 = Pass تحت `ACCEPTANCE_REQUIRE_ALL=true` على الملف الشبيه بالإنتاج.
- اختبارات التكامل الأربعة عشر و`Pay059PayrollSodHandlerTests` و`Pay059RegimeBlockerTests` خضراء، منها الأربعة على PostgreSQL.
- لا SQL في `scripts/acceptance-payroll-*.ps1`. والحارس المعماري لكل سكربتات القبول (`AcceptanceScriptSqlWriteTests`) تملكه **PLAN-65**.
- دليل الدورة الشهرية منشور، ومؤشرا الإطلاق مربوطان بالدليل.
- سطر «الحالة» لـ G-PAY-05 محدَّث، مع روابط مجلدات الأدلة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-59-pay-full-cycle-production-proof.md` |
| Acceptance | `AcPay59FullCycleTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، `PayrollCycleDriver.cs`؛ إعادة كتابة `Ac12PayrollSodTests.cs`، وتعديل `Ac21`/`Ac22`/`Ac23` |
| Integration | `Pay059PayrollSodHandlerTests.cs`، `Pay059RegimeBlockerTests.cs` (مضيفات PLAN-34 كما هي) |
| Scripts | `acceptance-payroll-determinism.ps1`، `acceptance-payroll-resume-chaos.ps1` بلا SQL |
| Docs | `docs/runbooks/payroll-monthly-cycle.md`، `docs/PILOT_DEPLOYMENT.md`، `docs/PAYROLL_RUN_RESUME.md`، سطر «الحالة» في `docs/PAY_001_GAP_STUDY.md` |
| Evidence | `acceptance-evidence/AC-PAY-59/`، `AC-12/`، `AC-21/`..`AC-23/` (لا تُلتزم ملفات `testdata/*/out/*.json` المعاد كتابتها) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تأخر إحدى الخطط 53..58 | الدورة مبنية على أسماء API المدونة في 0.2. وكل خطوة تفشل برسالة `AcceptanceGate.RequireData` تسمّي الخطة الناقصة، لا بخطأ غامض |
| استهلاك الأشهر في قاعدة قبول طويلة العمر | D-PAY-22 مع `acceptance-reset.ps1`، والإلغاء في `finally` |
| موظفو النطاق كثيرون فيبطؤ الإدخال الجماعي والتحقق من الحسابات | الدورة تعمل على الجهة A وحدها، والإدخال الجماعي لكل الوحدة بطلب واحد (PLAN-57) |
| حجم الجهة يتجاوز مهلة الحساب في البوابة (G-PAY-21) | بيانات القبول صغيرة. ومهلة الإنتاج في الموجة 2 (**PAY-J**)، والدليل يذكرها |

## Effort estimate

5–7 أيام عمل: خط الأساس والتحقق من اختبارات التكامل واختبار مسار الحجب (1)، AC-12 واختبار المعالج (1)، السكربتات وAC-21..23 (1)، الدورة الكاملة وثلاثة تنفيذات (1.5–2.5)، الدليل والوثائق (0.5–1).
