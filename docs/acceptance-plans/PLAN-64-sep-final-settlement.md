# PLAN-64 — التسوية النهائية لإنهاء الخدمة: حساب قابل للتفسير وضبط واعتماد وصرف بأمر (SEP-E)

**Scope:** SEP / FUN-002 — [دراسة الفجوات](../FUN_002_SEPARATION_GAP_STUDY.md): G-SEP-08، G-SEP-09. ويحتوي حساب نهاية الخدمة من G-PAY-14 في [دراسة PAY](../PAY_001_GAP_STUDY.md) (بصيغته المصحّحة بعد التحقق)  
**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-59 (دورة رواتب مثبتة بإعدادات الإنتاج)، ومنها PLAN-54 (الإطار القانوني المرجعي لكل نظام) وPLAN-56 (آلة حالات التشغيل) وPLAN-58 (مسار دفع واحد وأوامر دفع). وPLAN-63 (علامات الفصل وتعويض Payroll)، وPLAN-62 (`ISeparationClearanceStatusChanged`)  
**Related cards:** FUN-002، PAY-002 (نسخ الإعداد)، PAY-003، EXT-003 (أوامر الدفع)، `LegalDecisionIds.EosFormula` (`LD-EOS-FORMULA`)

## Goal

1. **تسوية صحيحة:**
   - تُحسب من تاريخ بدء الخدمة الحقيقي.
   - بقاعدة مصدّقة لكل نظام خدمة ونوع إنهاء، بلا قيم صلبة.
   - تشمل راتب الأيام الأخيرة، واسترداد ما دُفع زيادة، والسلف **مرة واحدة**.
   - إن زاد الدين على الاستحقاق تُسجَّل ذمة على الموظف، ولا تُقص إلى صفر.
2. **تسوية قابلة للتفسير:** كل مكوّن سطر له أساس وكمية ومعدل ومرجع قانوني، وتُحفظ لقطة المدخلات وبصمتها.
3. **ضبط وفصل مهام:**
   - لا إنشاء بمبالغ من النموذج.
   - المحتسِب لا يعتمد.
   - الصرف عبر مسار الدفع الواحد (PLAN-58) بأمر دفع، ولا «مدفوع» يدوي.
   - الصرف محجوز حتى براءة الذمة (D-SEP-5).

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-SEP-2 / D-PAY-7 قاعدة نهاية الخدمة لكل نظام ونوع | قواعد في نسخة الإطار القانوني (PLAN-54) بحالة `ApprovedInterim`: `law50` بلا مكافأة (يستحق معاشاً)، و`law53` بمكافأة متدرجة بالقيم المؤقتة الحالية (0.5/5/1.0). وللجميع راتب الأيام الأخيرة وبدل الإجازات. ولا قاعدة في الشيفرة: غيابها في نسخة الإطار ← فشل ظاهر | §2 |
| D-SEP-3 سقف بدل الإجازات والأرصدة المرحّلة | `LeavePayoutMaxDays` حقل في القاعدة (`null` = بلا سقف، مع شارة «بانتظار التصديق»)، ويُحتسب رصيد السنة الحالية متناسباً حتى النفاذ مع رصيد سنة سابقة واحدة (`Separation:Settlement:LeaveCarryOverYears=1`) | §1، §2 |
| الضريبة على التسوية | `TaxTreatment` في القاعدة، والافتراضي المؤقت `Exempt` بحالة `ApprovedInterim` | §2 |
| D-SEP-5 الصرف مع براءة ذمة مفتوحة | ممنوع: التسوية `OnHold` حتى `ClearanceStatus=Closed`. ولا خصم تلقائي لقيمة الفقد، فهي تظهر بنداً معلوماتياً حتى قرار تحميل بسند | §4 |
| الوفاة | تُحسب التسوية وتُعلَّم `PayeeKind=Estate`. وصرفها يحتاج بيانات ورثة (الموجة 2)، فتبقى `OnHold` بسبب «بانتظار حصر الإرث» | §4 |
| الشهر الأخير | مكوّن «راتب الأيام الأخيرة» داخل التسوية، لا تشغيل `FinalSettlement` منفصل (فلا مفتاح فهرس له، وهو ما تركه PLAN-56 §2 القاعدة 4 لهذه الخطة). وتشغيل مُرحَّل غطّى أياماً بعد النفاذ ← سطر استرداد | §2 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-64-sep-final-settlement` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test … --filter "EndOfService\|Eos\|PayrollEngine_CalculateEndOfService\|ContractSeparationChain\|Pay00\|Sep063"` | أخضر أو موثق |
| 0.3 | تحقق التبعيات | PLAN-54: نسخة إطار لكل `RegimeCode` قابلة للاستيراد. PLAN-58: `PaymentBatchIssuer` (النقطة الوحيدة لإنشاء دفعة، بمصدر صريح) و`SettlePaymentBatchHandler`. PLAN-63: `DeactivatedBySeparationId` | موجودة؛ وإلا يُسجَّل المحوّل أو النقص في `reports/plan64-progress.md` قبل البدء |
| 0.4 | جرد | التسويات القائمة حسب الحالة، والخصومات `AdvanceFinalRecovery`، والتسويات بلا `SeparationRecordId` (منشأة يدوياً) | في `reports/plan64-baseline.md` |

---

## In scope

### 1. مدخلات صحيحة: تاريخ بدء الخدمة والإجازات والملف (G-SEP-08، G-PAY-14)

**المشكلة:**
- **مدة الخدمة** تبدأ من `profile.EffectiveFrom` ([InboundEventHandlers.cs:221-229](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))، وهو يُعاد ضبطه عند كل تفعيل عقد (`:58`، `:77`). والقسمة على 365.25 ([EndOfServiceCalculation.cs:15-22](../../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)).
- **الإجازات** رصيد سنة النفاذ فقط، يُحسب لحظة النشر ([SeparationRecordSupport.cs:31-45](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordSupport.cs)).
- **غياب الملف النشط** يتخطى التسوية كلها (`:205-206`)، ويُقَر مع ذلك بـ `payroll:settled` ([PayrollEventConsumers.cs:65](../../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `GET api/internal/employees/{id}/service-record` | `HR.Core.Web/Controllers/EmployeeLifecycleInternalController.cs` (سياسة المتحكم منذ PLAN-42: `ServicePolicies.HrCoreInternalS2S`. المستدعي `svc-hr-contracts` عند التنفيذ، و`svc-payroll` إن جلبت إعادة الحساب مدخلات جديدة؛ ويُضاف كل مستدعٍ إلى عملاء السياسة ومدخل `calls[]` في `deploy/wiring/service-wiring.json`، PLAN-37) | `HireDate`، `FirstAppointmentDate` (أقدم `EmploymentRecord`)، ومدد الانقطاع إن وُجدت (قائمة فارغة اليوم) |
| لقطة الخدمة في الرسائل | `IStartSeparationSaga`/`IExecuteSeparationParty` (خصائص اختيارية) + `ExecuteSeparationCommand` (PLAN-60) | `ServiceStartDate`، `LeaveDaysBalance`، `LeaveBalanceBasis` تُحسب عند التنفيذ وتُحفظ على السجل. فلا تتغير المدخلات بعد التنفيذ |
| `SeparationLeaveBalanceCalculator` | `HR.Contracts.Application/Services/` | رصيد السنة الحالية متناسباً حتى النفاذ (`TotalEntitlement × أيام السنة المنقضية ÷ أيام السنة − Used`)، مع المتبقي من `LeaveCarryOverYears` سنة سابقة. يحل محل `ComputeAnnualLeaveRemainingAsync` |
| اختيار الملف | `HandleSeparationCompletedHandler` (`:195-253`) | الملف النشط، وإلا أحدث ملف للموظف (مع سبب في السطر). ولا ملف أصلاً ← استثناء ← طرف `Faulted` (لا `payroll:settled`) |
| حذف المسارات المكررة | `EndOfServiceHandlers.CalculateEndOfServiceCommand` (بلا مستدعٍ)، و`PayrollEngine.CalculateEndOfServiceReal` ([PayrollEngine.cs:61-69](../../src/Services/Payroll/Payroll.Domain/Services/PayrollEngine.cs)) | حساب واحد في `EndOfServiceCalculator`. وتُنقل اختبارات `Phase7_BusinessLogicDepthTests` إلى الحاسب الجديد |

**القواعد:** سنوات الخدمة = الأيام من `ServiceStartDate` حتى `EffectiveDate` ناقص الانقطاعات، ÷ أيام السنة الفعلية (حساب تقويمي بالسنوات والأيام، لا 365.25)، وتُقرَّب بتقريب الإطار. وغياب `ServiceStartDate` ← `Faulted`.

### 2. قواعد التسوية ومكوّناتها (G-SEP-08، G-PAY-14)

**المشكلة:**
- نوع الإنهاء لا يدخل الحساب.
- قيم احتياطية صلبة (0.5، 5، 1.0، 30، [EndOfServiceCalculation.cs:41-45, 89](../../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)).
- رجوع صامت إلى الإعداد النشط ([InboundEventHandlers.cs:256-269](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- قص الصافي إلى صفر ([EndOfServiceCalculation.cs:153](../../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)).
- السلف تُحسم مرتين: الطرح (`:127`) وخصم `AdvanceFinalRecovery` ([InboundEventHandlers.cs:235-243](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). والأقساط الأصلية تبقى غير مطبقة، فتُسترد ثانية عند إعادة التعيين ([PayrollPartitionInputLoader.cs:127-129](../../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)).
- الشهر الأخير غير مدفوع لأن التشغيل يأخذ الملفات النشطة فقط ([PayrollRunJobHandlers.cs:90-91](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `EosRule` في نسخة الإطار | كيان ابن لـ `PayrollConfiguration` ضمن الإطار القانوني الذي يقدمه PLAN-54 (`Payroll.Domain/Entities/EosRule.cs`) + مصفوفة `eosRules[]` اختيارية تُضاف إلى مخطط الحزمة `docs/syr/payroll-framework-packs/schema.v1.json` (PLAN-54)، وتدخل البصمة بقيمة `HashSchemaVersion=3` للنسخ التي تحملها (القيمتان 1 و2 كما هما) | المفتاح `(RegimeCode, SeparationTypeCode or "*")`، والحقول: `GratuityMode` (`None`/`Tiered`)، `Tiers[]` (حد سنوات، معدل)، `GratuityBase` (`BaseSalary`/`BasePlusFixedAllowances`)، `LeavePayout` (نعم/لا)، `LeavePayoutMaxDays?`، `LeaveDailyDivisor`، `FinalPay` (نعم)، `TaxTreatment`، `PayeeKind`، `LegalSourceId`، `Article?`، `Status` |
| بذور مؤقتة | حزم الإطار في `docs/syr/payroll-framework-packs/` (بنمط PLAN-54): `eosRules[]` في `law50-interim.json`، وحزمة `law53-interim.json` جديدة | قواعد `law50`/`law53`/الخاصّين وفق الافتراضي أعلاه بحالة `ApprovedInterim`. وتُحذف القيم الافتراضية `0.5m/5/1.0m/30` من `PayrollConfiguration.cs:77-101` لحقول نهاية الخدمة (تُنقل إلى القاعدة) |
| `EndOfServiceCalculator` | `Payroll.Domain/Services/EndOfService/EndOfServiceCalculator.cs` | دالة نقية: (لقطة المدخلات، القاعدة، الإطار) ← أسطر + صافٍ + ذمة. بلا قيم افتراضية؛ والقاعدة الغائبة ← خطأ |
| المكوّنات | `EndOfServiceSettlementLine` (جديد) | `FinalPay` (بقاعدة التناسب في الإطار من بداية الفترة غير المدفوعة حتى النفاذ، مع البدلات الثابتة النشطة)، `LeavePayout`، `Gratuity`، `OverpaymentRecovery` (سالب)، `AdvanceRecovery` (سالب)، `Tax` (سالب)، `AssetLiabilityInfo` (صفر، معلوماتي). ولكل سطر: الأساس والكمية والمعدل والمبلغ والشرح العربي و`LegalSourceId` |
| `OverpaymentDetector` | `Payroll.Application/Services/EndOfService/` | من أسطر الرواتب **المرحّلة** للموظف لفترات تتقاطع مع ما بعد `EffectiveDate`: المبلغ = إجمالي الفترة × أيام العمل بعد النفاذ ÷ أيام عمل الفترة |
| دفتر السلف الواحد | `DeductionRecord.SettledBySettlementId` + ترحيل | الأقساط غير المطبقة تُجمع في سطر `AdvanceRecovery` وتُعلَّم بمعرّف التسوية في **المعاملة نفسها**. ولا يُنشأ `AdvanceFinalRecovery` بعد الآن. والمحمّل يستبعد المعلَّم |
| الذمة | `EmployeeReceivable` (جديد) | الصافي السالب ← ذمة بمبلغها وسببها ومرجع التسوية، والصافي المستحق صفر |
| لقطة وبصمة | `EndOfServiceSettlement.InputsJson`، `InputsHash`، `FrameworkVersionId`، `EosRuleId` | لإعادة الحساب حرفياً |

**القواعد:**

1. لا ناتج دون قاعدة منشورة لنظام الموظف (والنوع أو `*`). الغياب ← الطرف `Faulted` برسالة «لا قاعدة نهاية خدمة منشورة للنظام X/النوع Y».
2. لا عمليات بـ `double`. كل المبالغ `decimal` وبتقريب الإطار.
3. إعادة الحساب بالمدخلات نفسها تعطي البصمة والأسطر نفسها (اختبار حتمية).
4. الأقساط المسواة لا تظهر في أي تشغيل لاحق، ومنها تشغيل بعد إعادة التعيين.

### 3. إنشاء التسوية وإعادة حسابها واعتمادها (G-SEP-09)

**المشكلة:**
- `Create(EndOfServiceSettlement model)` يربط الكيان، فتُحفظ مبالغ يكتبها المستخدم ([EndOfServiceController.cs:69-80](../../src/Services/Payroll/Payroll.Web/Controllers/EndOfServiceController.cs)).
- لا `CreatedBy` ([BaseEntity.cs:5-16](../../src/Shared/HRManagement.SharedKernel/Entities/BaseEntity.cs)). وفحص فصل المهام يقارن بـ `ApprovedBy` الفارغ في المسودة ([EndOfServiceHandlers.cs:58](../../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs)).
- الجمع بين دوري المعالج والمعتمد ممكن، لأن `ValidateRoleAssignment` بلا مستدعٍ (ملاحظة التحقق في PAY).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `EndOfServiceSettlement : AuditableEntity` | `Payroll.Domain/Entities/EndOfServiceSettlement.cs` + ترحيل `SEP064_SettlementGovernance` | `CreatedBy`، و`CalculatedBy` (`null` = النظام)، و`RecalculatedBy/At/Reason`، و`ApprovedBy/At`، و`Revision`، و`ClearanceStatus`، و`PayeeKind`، و`HoldReason`، و`PaymentBatchId`. ورمز تزامن `xmin` |
| حذف `Create` اليدوي | `EndOfServiceController.cs` + `Views/EndOfService/Create.cshtml` | لا إنشاء من النموذج. التسوية تنشأ من طرف الفصل فقط |
| `RecalculateEndOfServiceCommand` | `EndOfServiceHandlers.cs` | `PayrollProcess`، بسبب ≥ 20 حرفاً، للحالتين `Draft`/`OnHold`: يعيد الحساب من المدخلات الحالية (مدخلات جديدة من HR.Core وHR.Contracts)، ويحفظ الأسطر السابقة مراجعةً (`Revision+1`) |
| `ApproveEosSettlementCommand` | الملف القائم | `PayrollApprove`. المعتمد ≠ `RecalculatedBy` ≠ `CreatedBy` (إن وُجدا)، والقيمة من المطالبة لا من الطلب. وتسوية لفصل ملغى ← رفض |
| الواجهة | `Views/EndOfService/Details.cshtml` | الأسطر والشرح والمراجع والمراجعات وسبب الحجز، وأزرار حسب الحالة والدور |

**آلة الحالات:** `Draft → Approved → PaymentRequested → Paid`. ومن أي حالة قبل `Paid`: `OnHold` (براءة/ورثة) ← العودة، و`Cancelled` (تعويض PLAN-63 للمسودة فقط).

### 4. الصرف عبر مسار الدفع الواحد، وحجز البراءة (G-SEP-09، G-PAY-14)

**المشكلة:** `MarkPaid` يغيّر الحالة فقط ([EndOfServiceHandlers.cs:70-88](../../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs))، و`PaymentOrderService` يختار الملفات النشطة فقط ([PaymentOrderService.cs:24](../../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderService.cs)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `RequestEosPaymentCommand` | `EndOfServiceHandlers.cs` | `PayrollPost`، لتسوية `Approved` مع `ClearanceStatus=Closed` و`PayeeKind=Employee`. ينشئ دفعة من نوع «تسوية نهائية» عبر `PaymentBatchIssuer` (PLAN-58؛ ويُضاف معالج هذا الأمر إلى المستدعين المسموحين في `Pay058SinglePaymentPathArchitectureTests`)، فتمر ببوابة الحجز والاحتيال والحساب المصرفي (PLAN-55)، ثم أمر دفع. الصارف ≠ المعتمد |
| `SeparationClearanceStatusConsumer` | `Payroll.Infrastructure/Consumers/` | يستهلك `ISeparationClearanceStatusChanged` (PLAN-62) فيحدّث `ClearanceStatus` ويرفع الحجز أو يضعه |
| تأكيد الدفع | `SettlePaymentBatchHandler` (PLAN-58 §7، يُستدعى من قرار طلب التسوية بشخصين) | `Paid` فقط من تسوية دفعة التسوية النهائية. ويُحذف `MarkPaid` |
| حجز الوفاة | `PayeeKind=Estate` | `OnHold` بسبب «بانتظار حصر الإرث»، ولا طلب دفع (الموجة 2) |

**القواعد:** لا مسار يضبط `Paid` إلا تأكيد أمر الدفع. ولا دفعة تسوية لموظف محجوز أو بلا حساب متحقق.

---

## Out of scope

| البند | الخطة |
|-------|-------|
| الإطار القانوني نفسه ونسخه واستيراد الحزمة الموقعة | PLAN-54 |
| مسار الدفع الواحد وأوامره وملف الخزينة والبوابة | PLAN-58 |
| آلة حالات التشغيل وإعادة حساب تشغيل غير مُرحَّل يشمل منفصلاً | PLAN-56 |
| تعويض التسوية (مسودة) وإعادة ملف الأجر عند الإلغاء | PLAN-63 |
| بيانات الورثة وصرف تسوية الوفاة، والإحالة إلى المعاش (GOSI/GOIP) | الموجة 2 (SEP-G، SYR-006) |
| تحميل قيمة الفقد على الموظف بقرار | الموجة 2 (بعد قرار D-SEP-5 القانوني) |
| بقية مدخلات الحركات (ترفيع، إعارة، حجز قضائي) من G-PAY-14/PAY-G | الموجة 2 (PAY-G) |

## Acceptance scenarios

على الملف الشبيه بالإنتاج بعد PLAN-59. الحالات الذهبية تُحسب أرقامها في الاختبار **من قيم القاعدة المنشورة** (لا أرقام قانونية صلبة في الاختبار)، ويُحفظ جدول الحساب المتوقع في الدليل لتوقيع القانوني والرواتب. الشخصيات: `payroll-processor`، `payroll-approver`، `a-payroll-poster`، `a-contracts-checker`، والموظفون `a-leaver` (`law50`، `HireDate` 2006-03-01، ترفيع بملف أجر من 2025-01-01، سلفة 6 أقساط سُدد منها 3)، `a-leaver-53` (`law53`، 4 عقود متتالية من 2019)، `a-leaver-debt` (سلفة أكبر من الاستحقاق، جديد). والثلاثة قوالب تُنسخ لكل سيناريو بـ `AcceptanceLeaverFixture` (PLAN-60) موسّعاً بتاريخ الخدمة والعقود وملفات الأجر والسلف أعلاه.

| ID | Positive | Negative |
|----|----------|----------|
| AC-SEP-64a | استقالة `a-leaver` نافذة 2026-05-20 ← سنوات الخدمة ≈ 20.2 من `HireDate`، وبلا مكافأة (قاعدة `law50`)، و`FinalPay` لـ 20 يوماً، وبدل إجازات حسب الرصيد والسقف، و`AdvanceRecovery` = 3 أقساط مرة واحدة، والأسطر تحمل المراجع، والبصمة محفوظة | — |
| AC-SEP-64b | `a-leaver-53` ← سنوات من أول عقد (2019)، ومكافأة متدرجة بقيم القاعدة | — |
| AC-SEP-64c | `a-leaver-debt` ← ذمة `EmployeeReceivable` بالفرق، والصافي 0 | — |
| AC-SEP-64d | رواتب مايو مُرحّلة كاملة قبل التنفيذ ← سطر `OverpaymentRecovery` لأيام ما بعد 05-20 | — |
| AC-SEP-64e | `payroll-approver` يعتمد ← `a-payroll-poster` يطلب الصرف بعد إغلاق البراءة ← دفعة تسوية ← أمر دفع ← `Paid` عند التأكيد | طلب الصرف والبراءة مفتوحة ← رفض «بانتظار براءة الذمة». والصارف = المعتمد ← رفض |
| AC-SEP-64f | — | POST إلى `EndOfService/Create` بمبالغ ← 404. و`payroll-processor` يعيد الحساب ثم يحاول الاعتماد (بدور مزدوج في تجهيز موثق) ← رفض. ولا إجراء `MarkPaid` |
| AC-SEP-64g | — | نظام بلا قاعدة منشورة (تجهيز: نسخة إطار بلا `EosRule`) ← طرف Payroll `Faulted` ← تدخل، ولا تسوية ولا `payroll:settled` |
| AC-SEP-64h | إعادة تعيين `a-leaver` بعد التسوية (مسار PLAN-63) ← أول تشغيل لا يطبق أي قسط سلفة من الحلقة السابقة | — |
| AC-SEP-64i | وفاة ← تسوية محسوبة بـ `PayeeKind=Estate` و`OnHold` | طلب صرفها ← رفض |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Sep064|EndOfServiceCalculator|EosRule|OverpaymentDetector|SeparationLeaveBalance"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Sep064|ContractSeparationChain|Pay003|Sep063"
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcSep64"
```

**اختبارات جديدة:**
- **Unit:**
  - `Sep064CalculatorGoldenTests`: الحالات أ–د والوفاة، وحتمية البصمة، وغياب القاعدة.
  - `Sep064ServiceYearsTests`: سنوات كبيسة، ونفاذ في آخر الشهر.
  - `Sep064OverpaymentTests`.
  - `Sep064LeaveBalanceTests`.
  - `Sep064SettlementStateTests`: الحالات وفصل المهام.
- **Integration على PostgreSQL** (`PostgreSqlFixture`)، `Sep064SettlementPostgresTests`:
  - حفظ التسوية وتعليم الأقساط في معاملة واحدة، وفشل منتصفها ← لا شيء.
  - اعتمادان متزامنان (`xmin`) ← واحد ينجح.
  - المحمّل لا يلتقط قسطاً مسوى.
  - الفهرس الفريد لـ `SeparationRecordId` مع المراجعات.
- **عقد:** `Sep064ServiceRecordEndpointTests` لنقطة HR.Core الداخلية: نطاق S2S ورفض المستخدم العادي.

## Definition of done

- لا يوجد في الشيفرة مسار ينشئ تسوية بمبالغ مُدخلة، ولا قيمة احتياطية لقاعدة نهاية الخدمة، ولا ضبط لـ `Paid` إلا من تأكيد أمر الدفع.
- حساب واحد للتسوية (حُذف `CalculateEndOfServiceReal` و`CalculateEndOfServiceCommand`).
- كل تسوية لها أسطر مفسَّرة وبصمة مدخلات ومرجع القاعدة ونسخة الإطار.
- جدول الحالات الذهبية في `acceptance-evidence/AC-SEP-64/golden-cases.md` جاهز لتوقيع القانوني والرواتب. وAC-SEP-64a..i = Pass.
- سطور «الحالة» لـ G-SEP-08 وG-SEP-09 في دراسة SEP، ولـ G-PAY-14 (جزء نهاية الخدمة) في دراسة PAY، محدّثة.
- العمل في commits محلية على الفرع: لا دفع (PLAN-35 للمالك)، ولا أسرار ولا `global.json` ولا `testdata/*/out/*.json` معاد كتابتها.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-64-sep-final-settlement.md` |
| Payroll Domain | `EndOfServiceCalculator.cs`، `EosRule`، `EndOfServiceSettlementLine`، `EmployeeReceivable`، تحديث `EndOfServiceSettlement`/`EosStatus`/`DeductionRecord`، حذف `EndOfServiceCalculation` الصلب و`CalculateEndOfServiceReal` |
| Payroll Application/Infra | `HandleSeparationCompletedHandler`، `OverpaymentDetector`، أوامر إعادة الحساب والاعتماد وطلب الصرف، `SeparationClearanceStatusConsumer`، ترحيل `SEP064_SettlementGovernance` |
| Payroll Web | `EndOfServiceController` (حذف `Create`/`MarkPaid`، إضافة `Recalculate`/`RequestPayment`)، والصفحات |
| HR.Core | نقطة `service-record` |
| HR.Contracts | `SeparationLeaveBalanceCalculator`، ولقطة الخدمة في التنفيذ والرسائل |
| Legal pack | قواعد `EosRule` المؤقتة في حزمة الإطار (`docs/syr/`) |
| Tests | أعلاه، و`AcSep64FinalSettlementTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، والشخصية `a-leaver-debt` في `personas.json` و`AcceptancePersonaCatalog.cs` (+1 على العدد وقت التنفيذ)، وتجهيزات الشخصيات الثلاث في `AcceptanceLeaverFixture` (Payroll وHR.Core وHR.Contracts) |
| Scripts | `scripts/sep064-validate.ps1`، `scripts/sep064-legacy-settlements-report.ps1` (التسويات اليدوية السابقة وخصومات `AdvanceFinalRecovery` القائمة) |
| Docs | `PAYROLL_CONFIG_VERSIONING.md` (قواعد نهاية الخدمة في الإطار)، `FUN_002_JOURNEY_MATRIX.md` (`PayrollSettled` ودليله)، `FUN_002_RECONCILIATION_CHECKLIST.md` (التسوية والذمة والحجز)، وسطور «الحالة» |

## Risks

| الخطر | التخفيف |
|-------|---------|
| أسماء واجهات PLAN-54/58 تختلف عمّا تفترضه الخطة | المرحلة 0.3 تسجل المحوّل. والحاسب نقي ومستقل عن التخزين |
| خصومات `AdvanceFinalRecovery` قديمة معلّقة تُسترد عند إعادة تعيين | التقرير يسردها، وترحيل يعلّمها `Settled` إن وُجدت تسوية للفصل نفسه، وإلا تُترك للمراجعة اليدوية |
| تسويات يدوية قائمة بلا `SeparationRecordId` | تبقى للقراءة بعلامة «قديمة — يدوية»، ولا تُعتمد ولا تُصرف بعد الآن إلا بإعادة حساب مربوط بفصل |
| القواعد المؤقتة تُنتج مبالغ غير مصدقة | شارة `ApprovedInterim` على كل سطر وفي التقرير، وجدول ذهبي للتوقيع قبل أي صرف إنتاجي حقيقي |
| تشغيل غير مُرحَّل يشمل المنفصل بكامل الشهر | خارج النطاق (PLAN-56). والاسترداد هنا يقرأ المرحَّل فقط. ويُوثق الخطر في دليل المصالحة |

## Effort estimate

9–10 أيام عمل: المدخلات ونقطة HR.Core والإجازات (1.5)، القاعدة والحاسب والمكوّنات والاسترداد والذمة (3)، دفتر السلف (1)، الحوكمة والحالات والواجهة (1.5)، الصرف والحجز (1)، القبول والجدول الذهبي والتوثيق (1.5).
