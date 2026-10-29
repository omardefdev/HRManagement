# PLAN-61 — كتالوج أنواع إنهاء الخدمة لكل نظام ومدخل واحد للفصل (SEP-B)

**Scope:** SEP / FUN-002 — [دراسة الفجوات](../FUN_002_SEPARATION_GAP_STUDY.md): الجزء القانوني من G-SEP-02، وG-SEP-10. ويحتوي احتواءً من [دراسة ER](../HCM_006_EMPLOYEE_RELATIONS_GAP_STUDY.md): G-ER-02 وG-ER-03  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-37 (شحن `service-regimes.json` مع كل خدمة تقرؤه، وعنوان HR.Core ونطاقه لـ HR.Contracts)، PLAN-60 (آلة الحالات، `SeparationRecordFactory`، الاعتماد)  
**Related cards:** FUN-002، SYR-003 (`SYR_003_SERVICE_REGIMES.md`)، SYR-001 (`legislation-register.json`)، DATA-002 (`EMPLOYEE_LIFECYCLE.md`)، HCM-006/FUN-004 (التأديب والقضايا)

## Goal

1. **كتالوج أنواع لكل نظام خدمة:** لكل نظام قائمة بالأنواع المسموحة. لكل نوع مهلة إشعار، ودليل مطلوب، ومسار اعتماد، ومرجع قانوني. ومنها نوع **الوفاة**. يُفحص النوع عند **الإنشاء**، لا بعد إخلاء طويل.
2. **مدخل واحد لإنهاء الخدمة:** كل مصدر يُنشئ «طلب فصل» `Draft` يمر بالاعتماد (PLAN-60). والمصادر: HR، وقرار طبي، وإجراء تأديبي، وحكم قضائي، واستقالة واردة. ولا شيء آخر يغيّر حالة الموظف إلى حالة نهائية أو يوقف راتبه.
3. **احتواء G-ER-02:** يتوقف Payroll عن تعطيل ملف الأجر عند الإجراء التأديبي، ويتحول «الفصل» التأديبي إلى طلب فصل.
4. **احتواء G-ER-03:** لا يُنشر حكم لموظف فارغ. ويرفض المستهلك أي طلب بلا موظف صالح. وتُلغى السجلات الزائفة القائمة.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-SEP-1 الأنواع المسموحة لكل نظام ومهلها | `law50` و`law53` والخاصّان: `resignation`، `retirement`، `termination`، `medical`، `disciplinary`، `judicial`، `death`، ولـ`law53` أيضاً `contract_end`. مهلة 30 يوماً للاستقالة فقط (60 لـ `special_judiciary` كالحالي). المرجع `SYR-LAW-50-2004`/`SYR-LAW-53-2021` و`article: null` وحالة `ApprovedInterim` | §1 |
| حالة القيد القانوني في الإنتاج | يُسمح بـ `Certified` و`ApprovedInterim`. و`PendingLegalReview` مرفوض في `Production` (`Separation:LegalCatalog:AllowPendingLegalReview=false`) | §1 |
| D-ER-4 مالك الفصل التأديبي والقضائي | Compliance يقرر، وHR.Contracts ينفّذ الفصل بمرجع القرار، وPayroll لا يتصرف إلا عند `ISeparationCompleted` | §3، §4 |
| نهائية القرار التأديبي (G-ER-01: ربط الاعتماد بالمحرك في PLAN-46، أما النهائية والاستئناف فلم يُصلحا) | الطلب الناتج `Draft` بعلامة «تحقق من نهائية القرار». ويمر بالمسار الحساس (فيه `compliance`)، ويُلزِم المقدّم بإقرار نصي بالنهائية ومرجعها | §3 |
| ملفات الأجر المعطّلة تأديبياً قبل هذه الخطة | تقرير فقط. والإعادة حالة بحالة بيد `payroll-approver` بسبب | §3 |
| النقل إلى جهة أخرى (`transfer_out`) | الموجة 2 (SEP-G). لا يُضاف هنا | — |
| D-TBH-1 مصدر الفصل الطبي | قرار لجنة طبية فقط. توصية الطبيب المنفرد لا تفتح فصلاً ولا تغيّر حالة الموظف (يُزال أثرها الحالي في HR.Core الذي يضع `Retired` فوراً) | §2، AC-SEP-61f |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-61-sep-legal-types-single-entry` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test … --filter "Sep060\|Syr009\|Separation\|Medical\|Disciplinary\|Data002"` | أخضر أو موثق في `reports/plan61-baseline.md` |
| 0.3 | التحقق من PLAN-37 | داخل حاوية `hr-contracts-web` المبنية: `seed-packs/syr003-service-regimes-v1/regimes.json` موجود، و`Integration__HrCoreBaseUrl` مضبوط | موجودان؛ وإلا توقف وأبلغ |
| 0.4 | جرد | عدد `SeparationRecords` بـ `EmployeeId = Guid.Empty`. وعدد `EmployeePayProfiles` المعطلة بعلامة `DisciplinaryAction-*` في `PayrollAdjustments`. وعدد الموظفين بحالة نهائية دون سجل فصل `Completed` | الأرقام في `reports/plan61-baseline.md` |

---

## In scope

### 1. كتالوج الأنواع لكل نظام خدمة (G-SEP-02، الجزء القانوني)

**المشكلة:**
- الحزمة الحالية تسمح لـ `law50` بثلاثة أنواع فقط ([service-regimes.json:99-104](../syr/service-regimes.json)). ولا نظام يسمح بـ `medical` أو `judicial` أو `disciplinary` ([ServiceRegimeSeparationRules.cs:78-89](../../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)).
- لا نوع «وفاة» ([SeparationType.cs:5-21](../../src/Services/HRContracts/HR.Contracts.Domain/Enums/SeparationType.cs)).
- `NoticeDays` يُحمَّل ولا يُستخدم ([ServiceRegimeSeparationRules.cs:12-16](../../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)).
- والفحص يجري عند التنفيذ بعد أن يكون العقد قد أُنهي ([SeparationRecordHandlers.cs:163-178](../../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)، [SeparationRegimeValidationService.cs:8-27](../../src/Services/HRContracts/HR.Contracts.Application/Services/SeparationRegimeValidationService.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `separation_rules` v2 | `docs/syr/service-regimes.json` (يشحنه PLAN-37) | لكل نظام `types[]`، ولكل نوع: `code`، `notice_days`، `approval` (`standard`/`sensitive`)، `evidence` (`resignation_letter`، `decision_ref`، `judgment_ref`، `medical_report_ref`، `death_certificate`)، `legal_source_id`، `article`، `status` (`Certified`/`ApprovedInterim`/`PendingLegalReview`). ويبقى `allowed_types` v1 مقروءاً للتوافق |
| `ServiceRegimeSeparationRules` | الملف القائم (`LoadRules` `:91-115`، ويقرأ عبر `ServiceRegimePack` منذ PLAN-37) | قراءة v2 مع رجوع إلى v1. و`GetTypeRule(regime, code)` يعيد القيد كاملاً. وعدّاد `separationRules` في `ServiceRegimePack`/`ServiceRegimePackHealthCheck` (PLAN-37) يعدّ صيغة v2 |
| `SeparationType.Death` | `SeparationType.cs` | قيمة جديدة في النهاية، `Display = "وفاة"`. والرمز `death` في `ToSeparationTypeCode` |
| `SeparationTypeCatalog` | `HR.Contracts.Application/Services/SeparationTypeCatalog.cs` | يجمع نظام الموظف (`IHrCoreActiveRegimeClient`) مع القيد: `GetAllowedTypesAsync(employeeId)`، و`ValidateAsync(employeeId, type, effectiveDate, requestDate, evidence)` |
| حقول الدليل والمصدر | `SeparationRecord` + ترحيل `SEP061_SeparationSourceAndEvidence` | `SourceKind` (`Manual`/`Ess`/`Compliance`/`Health`/`Judicial`)، `SourceEntityId`، `SourceReference`، `EvidenceFileId`، `NoticeExceptionReason`، `LegalRuleStatus`، `SourceFinalityConfirmation` (§3). وفهرس فريد مشروط على `(TenantId, SourceKind, SourceEntityId)` |
| فحص عند الإنشاء والتقديم | `SeparationRecordFactory` (PLAN-60) و`SubmitSeparationCommand` | يستدعي `SeparationTypeCatalog.ValidateAsync`. وعند التنفيذ يُعاد الفحص لاحتمال تغيّر النظام |
| الواجهة | `Step1.cshtml`، `Create.cshtml` | قائمة الأنواع تُملأ من `GET SeparationRecords/AllowedTypes?employeeId=` (القيد وحالته القانونية)، وحقول الدليل حسب النوع، وشارة «اعتماد مؤقت — بانتظار التصديق القانوني» للقيد `ApprovedInterim` |
| `SeparationApprovalRouting` | PLAN-60 | `approval = sensitive` أو `NoticeExceptionReason ≠ null` ← `SEPARATION-APPROVAL-SENSITIVE` |
| HR.Core | `EmploymentStatusRules.MapSeparationType` ([:77-89](../../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)) | `Death` ← `Deceased` (المطابقة موجودة نصياً)، مع اختبار |

**القواعد:**

1. نوع غير مسموح لنظام الموظف ← رفض عند الإنشاء: «نوع الإنهاء (X) غير مسموح لنظام الخدمة (Y)». لا يُنشأ سجل ولا معاملة.
2. القيد `PendingLegalReview` في `Production` ← رفض «النوع بانتظار التصديق القانوني». وفي غير الإنتاج يُسمح مع شارة.
3. المهلة: `effectiveDate ≥ requestDate + notice_days` بتوقيت دمشق. وإن قصرت فيلزم `NoticeExceptionReason` (≥ 20 حرفاً) ويصبح المسار حساساً.
4. الدليل حسب النوع إلزامي عند التقديم. `death`: رقم شهادة الوفاة وتاريخها، وتاريخ النفاذ = تاريخ الوفاة. `disciplinary`: `SourceReference` لقرار تأديبي. `judicial`: مرجع الحكم. `medical`: مرجع تقرير اللجنة.
5. لا رقم مادة يُخترع. `article` يبقى `null` حتى التصديق (`SYD-001`)، ويظهر للمستخدم «المادة: بانتظار التصديق».
6. عدم توفر HR.Core لجلب النظام ← رفض بخطأ صريح (fail-closed)، لا سماح.

### 2. مدخل واحد لتغيير الحالة إلى حالة نهائية (G-SEP-10)

**المشكلة:**
- تعديل الموظف يغيّر الحالة إلى `Terminated/Retired/Resigned/Deceased` مباشرة ([EmployeesController.cs:389-416](../../src/Services/HRCore/HR.Core.Web/Controllers/EmployeesController.cs)). وكذلك التصحيح المعتمد بتجاوز إداري ([EmployeeCorrectionService.cs:101-108](../../src/Services/HRCore/HR.Core.Domain/Services/EmployeeCorrectionService.cs)).
- المسار الطبي يضع `Retired` فوراً ([MedicalDecisionIssuedConsumer.cs:53-68](../../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalDecisionIssuedConsumer.cs)، [MedicalRecommendationIssuedConsumer.cs:51-60](../../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalRecommendationIssuedConsumer.cs)، [MedicalEmploymentEffectRules.cs:10-28](../../src/Services/HRCore/HR.Core.Domain/Services/MedicalEmploymentEffectRules.cs)).
- المستهلك الوحيد لـ `IEmployeeStatusChanged` هو Auth.Directory، فيستمر الراتب ويبقى المنصب مشغولاً.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `StatusChangeSource` | `HR.Core.Domain/Enums/StatusChangeSource.cs` | `ManualEdit`، `Correction`، `Medical`، `SeparationCompleted`، `LegalLifecycle` (إخفاء الهوية/الدمج)، `Rehire` (تستخدمه PLAN-63) |
| `EmployeeLifecycleService.TryChangeStatus(..., StatusChangeSource source)` | `HR.Core.Domain/Services/EmployeeLifecycleService.cs` | معامل إلزامي (تغيير توقيع يجبر كل مستدعٍ على التصريح). الأهداف `Terminated/Retired/Resigned/Deceased` لا تُقبل إلا من `SeparationCompleted`. و`Anonymized/Merged` من `LegalLifecycle` فقط. ورسالة الرفض: «الحالات النهائية تأتي من رحلة إنهاء الخدمة فقط» |
| تعديل الموظف | `EmployeesController.Edit` + `Views/Employees/Edit.cshtml` | قائمة الحالة لا تعرض الحالات النهائية، والخادم يرفضها برسالة فيها رابط لإنشاء طلب فصل |
| التصحيح | `EmployeeCorrectionService` | تصحيح يطلب حالة نهائية يُرفض عند الإنشاء والاعتماد |
| المستهلكان الطبيان في HR.Core | الملفان أعلاه | لا `Retired` مباشرة. قرار اللجنة يُسجَّل في `EmploymentStatusHistory` بملاحظة «قرار طبي بالتقاعد — بانتظار إنهاء الخدمة»، والتوصية المنفردة بملاحظة «توصية طبية — بانتظار قرار اللجنة»، دون تغيير الحالة في الحالتين. وتبقى `FitnessForDuty`/`RETURN_TO_WORK` ← `Active` (غير نهائية) |

**القواعد:**

1. اختبار وحدة لكل `StatusChangeSource` × كل حالة هدف.
2. اختبار معماري `TerminalStatusSourceTests`: كل مستدعٍ لـ `TryChangeStatus` يمرر مصدراً حرفياً، ولا يمرر `SeparationCompleted` إلا `SeparationCompletedConsumer`. والفحص بقراءة الشجرة النحوية بـ Roslyn في الاختبار، أو بالبحث النصي إن تعذر.
3. لا تغيير في انتقالات `EmploymentStatusRules` نفسها هنا.

### 3. الإجراء التأديبي «فصل» ← طلب فصل، لا إيقاف راتب (احتواء G-ER-02)

**المشكلة:**
- `HandleDisciplinaryActionHandler` يعطّل ملف الأجر لحظة استلام `IDisciplinaryActionTaken` من نوع `Dismissal` ([InboundEventHandlers.cs:610-614](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). والحدث يُنشر عند الإنشاء في Compliance ([ComplianceExtendedControllers.cs:207-219](../../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs))، وبعد PLAN-46 يُنشر من `DisciplinaryApprovalOutcomeConsumer` عند اعتماد الإجراء في المحرك. فالتعطيل يبقى قائماً عند الاعتماد.
- الفصل اللاحق لا يجد ملفاً نشطاً، فلا تسوية ([InboundEventHandlers.cs:205-206](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| حذف التعطيل | `InboundEventHandlers.cs:610-614` | لا يمس Payroll `IsActive`/`EffectiveTo` بسبب حدث تأديبي. خصم الأيام للإيقاف يبقى كما هو (يملكه ER) |
| `DisciplinaryDismissalSeparationConsumer` | `HR.Contracts.Infrastructure/Consumers/DisciplinaryDismissalSeparationConsumer.cs` | `ActionType == "Dismissal"` ← `SeparationRecordFactory` يُنشئ `Draft` من نوع `Disciplinary`، بـ `SourceKind=Compliance` و`SourceEntityId=ActionId` و`SourceReference="DisciplinaryAction:{ActionId}"`، وتاريخ نفاذ مقترح = `IssuedAt` بتوقيت دمشق. idempotent بالفهرس الفريد للمصدر |
| إقرار النهائية | `SubmitSeparationCommand` (PLAN-60) | للمصدر `Compliance`: حقل إلزامي «أقر بأن القرار نهائي — المرجع/التاريخ» يُحفظ في `SourceFinalityConfirmation` |
| تقرير وإعادة | `Payroll.Web/Controllers/PayrollAdminController.cs` (`PayrollApprove`) + `scripts/sep061-disciplinary-profiles.ps1` | `GET api/payroll/admin/disciplinary-deactivations`: الملفات المعطلة بعلامة `DisciplinaryAction-*` بلا فصل مكتمل. و`POST …/{profileId}/restore` بسبب (≥ 20 حرفاً)، مع لقطة تاريخ (`CreateHistorySnapshot`) وتدقيق. والسكربت يعرض فقط ما لم يُمرر `-Apply -ProfileIds`. والإعادة كاتب جديد لـ `EmployeePayProfiles`، فتُضاف إلى `PayProfileWritersTests` (PLAN-53) بسببها |

**القواعد:**

1. إعادة تسليم الحدث نفسه لا تُنشئ سجلاً ثانياً.
2. وجود سجل فصل مفتوح آخر للموظف ← لا يُنشأ ثانٍ. يُضاف المرجع إلى ملاحظات السجل القائم، ويُسجَّل تحذير.
3. الإعادة لا تتم آلياً أبداً، ولا تُنفَّذ لموظف له سجل فصل `Executing`/`Completed`.

### 4. الحكم القضائي: لا حدث لموظف فارغ (احتواء G-ER-03)

**المشكلة:**
- `Close` ينشر `ILegalJudgmentIssued` بـ `EmployeeId = Guid.Empty` لكل قضية تُغلق ([ComplianceExtendedControllers.cs:108-131](../../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)، `:118-126`). ولا حارس حالة على الخادم.
- المستهلك يُنشئ سجلاً `Judicial` ومعاملة دون فحص ([InboundEventConsumers.cs:125-137](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)، [InboundEventCommands.cs:360-397](../../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `Close` | `ComplianceExtendedControllers.cs` | حارس على الخادم: قضية `Closed` ← لا شيء. وحذف نشر `ILegalJudgmentIssued` من الإغلاق، لأن `LegalCase` بلا موظف ولا نتيجة. ربط القضية بموظف ونتيجة ذات أثر ← ER-F (الموجة 2) |
| `ProcessSeparationRequestHandler` | يُعاد بناؤه فوق `SeparationRecordFactory` | يرفض `Guid.Empty` والموظف غير الموجود أو المنتهية خدمته. والرفض = سجل تحذير وعدّاد `separation_inbound_rejected{source,reason}`، بلا استثناء يعيد المحاولة |
| الإصلاح | ترحيل `SEP061_CancelEmptyEmployeeSeparations` (SQL) | السجلات بـ `EmployeeId = '00000000-…'` غير الملغاة ← `Cancelled` بسبب «PLAN-61: حكم بلا موظف»، ومعاملاتها المربوطة (PLAN-60) ← `Cancelled` |

### 5. القرار الطبي والاستقالة الواردة (G-SEP-10)

**المشكلة:**
- `LimitedDuties` ينشر `IMedicalDecisionIssued` مثل `Retirement` ([MedicalDecisionWorkflow.cs:15-17](../../src/Services/HRHealth/HR.Health.Domain/Services/MedicalDecisionWorkflow.cs)، [HealthExtendedControllers.cs:85](../../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
- فيُنشأ فصل «طبي» لكل منهما ([InboundEventConsumers.cs:139-151](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)).
- `IResignationSubmitted` بلا منتج ([InboundEventConsumers.cs:153-164](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `MedicalDecisionIssuedConsumer` (العقود) | الملف أعلاه | `MedicalRef == "MEDICAL_RETIREMENT"` فقط ← `Draft` `Medical` بـ `SourceKind=Health` و`SourceEntityId=CorrelationId` (معرّف التقرير). وغير ذلك ← تجاهل مسجَّل |
| `IMedicalRecommendationIssued` (توصية طبيب منفرد) | — | **لا يفتح فصلاً ولا يغيّر حالة الموظف**: يُسجَّل فقط. الفصل الطبي يُفتح من قرار لجنة طبية وحده (الصف السابق)، اتساقاً مع G-TBH-02؛ وحوكمة القرار الطبي نفسها ← الموجة 2 (TBH-A) |
| `ResignationSubmittedConsumer` | يبقى | يمر عبر المصنع. والمنتج (ESS) ← الموجة 2 (G-SEP-15) |

**القواعد:** كل سجل وارد `Draft`، يمر بالاعتماد (PLAN-60)، ويعرض في القائمة شارة المصدر ورابطه.

---

## Out of scope

| البند | الخطة |
|-------|-------|
| شحن الحزمة مع HR.Contracts وHR.Movements، وعنوان HR.Core ونطاقه | PLAN-37 |
| آلة حالات القرار التأديبي ونهائيته وسلطة الإيقاع (G-ER-01) | ER-A (الموجة 2) |
| ربط القضية بموظف ونتيجة حكم ذات أثر وظيفي | ER-F (الموجة 2) |
| سلّم العقوبات وآثاره المالية (G-ER-04) | ER-B (الموجة 2) |
| الاستقالة من ESS، والنقل إلى جهة أخرى، والإحالة الآلية عند السن | الموجة 2 (SEP-G، G-SEP-15) |
| معادلة التسوية حسب النوع والنظام | PLAN-64 |
| تصديق المواد القانونية (`SYD-001`) | قرار المالك، خارج الشيفرة |

## Acceptance scenarios

على الملف الشبيه بالإنتاج بعد PLAN-37 وPLAN-60. الشخصيات: `a-contracts-maker`، `a-contracts-checker`، `central-a`، `a-compliance`، `a-compliance-2`، `a-management-approver`، `a-clinician`، `payroll-approver`، وموظفا الاختبار `a-leaver` (`law50`) و`a-leaver-53` (`law53`، جديد)، كلاهما قالب يُنسخ لكل سيناريو بـ `AcceptanceLeaverFixture` (PLAN-60).

| ID | Positive | Negative |
|----|----------|----------|
| AC-SEP-61a | فصل `death` لـ `a-leaver` برقم شهادة وتاريخها ← `Draft` ← تقديم ← مسار قياسي | `death` بلا رقم شهادة ← رفض عند التقديم |
| AC-SEP-61b | `contract_end` لـ `a-leaver-53` ← يُقبل | `contract_end` لـ `a-leaver` (`law50`) ← رفض عند الإنشاء برسالة النظام، ولا سجل ولا معاملة، والعقد `Active` |
| AC-SEP-61c | استقالة بمهلة 30 يوماً ← مسار قياسي | استقالة بنفاذ بعد 5 أيام بلا سبب ← رفض. ومع سبب ← المسار الحساس (`compliance` خطوة) |
| AC-SEP-61d | — | `central-a` يغيّر حالة موظف إلى `Terminated` من شاشة التعديل ← رفض برسالة الرحلة، والحالة كما هي |
| AC-SEP-61e | `a-compliance` يُنشئ إجراء `Dismissal` ويقدّمه، و`a-compliance-2` ثم `a-management-approver` يعتمدانه في المحرك (PLAN-46) ← سجل `Draft` `Disciplinary` بمرجع الإجراء خلال 30 ثانية من الاعتماد، وملف الأجر **نشط** ويظهر في تشغيل الحساب | إعادة تسليم الحدث (أداة الاختبار) ← لا سجل ثانٍ |
| AC-SEP-61f | قرار لجنة طبية `MEDICAL_RETIREMENT` ← سجل `Draft` `Medical`، وحالة الموظف في HR.Core `Active` حتى التنفيذ | `a-clinician` يسجل توصية `Retirement` منفردة ← لا سجل فصل وحالة الموظف `Active`؛ وتوصية `LimitedDuties` ← لا سجل فصل |
| AC-SEP-61g | — | إغلاق قضية ← لا صف `ILegalJudgmentIssued` في Outbox الخاص بـ Compliance، ولا سجل فصل جديد خلال 30 ثانية. وإعادة POST للإغلاق ← لا شيء. وحدث `ILegalJudgmentIssued` بـ `Guid.Empty` من الأداة ← لا سجل، والعدّاد زاد |
| AC-SEP-61h | الترحيل يلغي سجلات `Guid.Empty` المزروعة بسبب. والتقرير يسرد ملفاً معطلاً تأديبياً، و`payroll-approver` يعيده بسبب | `payroll-processor` يحاول الإعادة ← 403 |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Sep061|ServiceRegimeSeparationRules|EmployeeLifecycle|TerminalStatusSource|MedicalEmploymentEffect"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Sep061|Sep060|PayrollDisciplinary|InboundSeparation"
dotnet ef migrations has-pending-model-changes -p src/Services/HRContracts/HR.Contracts.Infrastructure -s src/Services/HRContracts/HR.Contracts.Web
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcSep61"
```

**اختبارات جديدة:**
- **Unit:**
  - `Sep061TypeCatalogTests`: v1/v2، والمهلة، والدليل، و`PendingLegalReview` في Production.
  - `Sep061StatusSourceTests`.
  - `Sep061InboundMappingTests`: طبي، وتأديبي، وقضائي فارغ.
  - `TerminalStatusSourceTests`.
- **Integration (PostgreSQL، `PostgreSqlFixture`)**، `Sep061InboundSeparationPostgresTests`:
  - الفهرس الفريد للمصدر تحت تسليمين متزامنين للحدث نفسه.
  - ترحيل `Guid.Empty`.
- **Payroll:** `PayrollDisciplinaryNoDeactivationTests`: إجراء `Dismissal` يترك الملف نشطاً.

## Definition of done

- لا يوجد في الشيفرة مسار يضع حالة نهائية لموظف إلا `SeparationCompletedConsumer`. ولا مسار يعطّل ملف أجر إلا مسار الفصل.
- كل مصدر وارد يُنشئ `Draft` واحداً يمر بالاعتماد.
- الأنواع تُفحص عند الإنشاء من الكتالوج v2. والوفاة مدعومة من الإنشاء حتى `Deceased` (الاكتمال يُثبت في PLAN-63).
- AC-SEP-61a..h = Pass، ومجلد `acceptance-evidence/AC-SEP-61/`.
- سطر «الحالة» لـ G-SEP-02 (الجزء القانوني) وG-SEP-10 في دراسة SEP، ولـ G-ER-02/G-ER-03 (احتواء) في دراسة ER، محدّثة.
- العمل في commits محلية على الفرع: لا دفع (PLAN-35 للمالك)، ولا أسرار ولا `global.json` ولا `testdata/*/out/*.json` معاد كتابتها.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-61-sep-legal-types-single-entry.md` |
| Legal pack | `docs/syr/service-regimes.json` (`separation_rules` v2)، تحديث `SYR_003_SERVICE_REGIMES.md` |
| SharedKernel | `ServiceRegimeSeparationRules.cs` (v2) |
| HR.Contracts | `SeparationType.Death`، `SeparationTypeCatalog.cs`، `DisciplinaryDismissalSeparationConsumer.cs`، تعديل `InboundEventConsumers.cs`/`InboundEventCommands.cs`، ترحيلا `SEP061_SeparationSourceAndEvidence` و`SEP061_CancelEmptyEmployeeSeparations`، الواجهة |
| HR.Core | `StatusChangeSource.cs`، `EmployeeLifecycleService.cs`، `EmployeesController.cs`، `EmployeeCorrectionService.cs`، المستهلكان الطبيان |
| Payroll | حذف التعطيل في `InboundEventHandlers.cs`، `PayrollAdminController.cs` (التقرير والإعادة) |
| Compliance | `ComplianceExtendedControllers.Close` |
| Tests | أعلاه، و`AcSep61LegalTypesSingleEntryTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، والشخصية `a-leaver-53` في `personas.json` و`AcceptancePersonaCatalog.cs` (+1 على العدد وقت التنفيذ) |
| Scripts | `scripts/sep061-validate.ps1`، `scripts/sep061-disciplinary-profiles.ps1` |
| Docs | `EMPLOYEE_LIFECYCLE.md` (مصادر الحالة النهائية)، `FUN_002_EXCEPTION_PATHS.md` (المصادر الواردة)، `FUN_004_EXCEPTION_PATHS.md` (لا أثر رواتب للتأديب خارج الفصل)، وسطور «الحالة» في الدراستين |

## Risks

| الخطر | التخفيف |
|-------|---------|
| تغيير توقيع `TryChangeStatus` يمس مستدعين كثيرين | التغيير مقصود: المترجم يكشف كل مستدعٍ. القائمة الحالية: 8 مواضع في 6 ملفات |
| موظفون معطلو الأجر تأديبياً يتوقعون استمرار الإيقاف | لا إعادة آلية. التقرير يُعرض على الرواتب وHR، والإعادة بقرار وسبب |
| الكتالوج المؤقت يسمح بأنواع قبل التصديق | شارة ظاهرة، وحالة القيد محفوظة في السجل (`LegalRuleStatus`)، وإعداد Production يرفض `PendingLegalReview` |
| إيقاف نشر الحكم يحجب سيناريو قضائي حقيقي | الفصل القضائي ممكن يدوياً بمرجع الحكم (`judicial` + `judgment_ref`) حتى ER-F |

## Effort estimate

7–8 أيام عمل: الكتالوج v2 والفحص والواجهة (2.5)، مصدر الحالة النهائية في HR.Core (1.5)، التأديبي والتقرير والإعادة (1.5)، القضائي والطبي والإصلاح (1)، القبول والتوثيق (1).
