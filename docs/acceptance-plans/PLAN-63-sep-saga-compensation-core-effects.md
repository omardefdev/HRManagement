# PLAN-63 — saga الفصل: إلغاء وتعويض صحيحان، وآثار HR.Core، وإعادة التعيين (SEP-D)

**Scope:** SEP / FUN-002 — [دراسة الفجوات](../FUN_002_SEPARATION_GAP_STUDY.md): G-SEP-05، G-SEP-07، بقية G-SEP-13، G-SEP-14  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-40 (نشر ذري للأحداث وحارس المعماري)، PLAN-60 (آلة الحالات والتنفيذ)، PLAN-61 (`StatusChangeSource`)، PLAN-62 (طرف Auth ومستهلك البراءة)  
**Related cards:** FUN-002، INT-003 (`PROCESS_SAGAS.md`)، EST-001/002 (المنصب الممول ونسخه)، DATA-002، HCM-001 (التعيين بعد الفصل)

## Goal

1. **المنصب يُخلى** في تاريخ النفاذ بنسخة `Vacated`، فيُفتح طلب الشاغر البديل.
2. **الإلغاء والتعويض صحيحان:**
   - إلغاء فصل قيد التنفيذ يعيد كل طرف إلى حالته قبل هذا الفصل **حرفياً**، ولا يمس ما سبقه. ويشمل ذلك ملف الأجر والعقد والمنصب.
   - السجل والمعاملة والـ saga تتفق على النتيجة.
   - الاستئناف بعد فشل التعويض يكمل التعويض، ولا يُكمل الفصل أبداً.
3. **لا فشل صامت في HR.Core:**
   - طرف HR.Core يتحقق من إمكان تغيير الحالة قبل الإقرار.
   - أي رفض بعد الاكتمال يفتح تدخلاً.
   - الطلبات المعلقة للموظف (إجازات، سلف) تُغلق عند التنفيذ.
4. **إعادة التعيين ممكنة بالقاعدة:** انتقال صريح من حالة نهائية إلى `PendingActivation` بأهلية مسجّلة، مع إعادة تفعيل الحساب.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-SEP-6 نافذة الإلغاء | يُسمح بإلغاء فصل `Executing`/`AwaitingIntervention` بسبب (≥ 20 حرفاً) ودور `HrContractsApprove`. أما `Completed` فلا يُلغى إلا بقرار تصحيحي (الموجة 2) | §2 |
| تعويض تسوية معتمدة أو مصروفة | لا عكس آلي للمال: الطرف `Faulted` بسبب «تسوية معتمدة — يلزم عكس مالي» ← تدخل | §2 |
| المنصب أُسند لغيره قبل الإلغاء | لا كتابة فوقه: طرف HR.Core `Faulted` ← تدخل | §1، §2 |
| الإجازات المعتمدة المستقبلية عند الإلغاء | الطلبات `Pending` تعود `Pending`. والإجازات المعتمدة الملغاة تُسرد في دليل التعويض لإعادة إدخالها يدوياً، ولا يُعاد نشر `ILeaveApproved` آلياً | §3 |
| D-SEP-7 أهلية إعادة التعيين | `Resignation`/`Retirement`/`EndOfContract`/`Medical` ← `Eligible`. و`Termination`/`Judicial`/`Disciplinary` ← `RequiresReview`: يلزم سبب وموافقة `hr-management-approver` غير المُعيِّن. و`Death` ← لا إعادة | §4 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-63-sep-saga-compensation-core-effects` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet test … --filter "Int003\|Fun002\|G3Separation\|SagaPartyCompensation\|SeparationSagaCompensation\|Sep06"` | أخضر أو موثق |
| 0.3 | جرد | على القبول: المناصب `Occupied` بموظف حالته نهائية، والموظفون بحالة نهائية ولهم عقد `Active` | الأرقام في `reports/plan63-baseline.md` (تغذي سكربت الإصلاح §1) |

---

## In scope

### 1. طرف HR.Core حقيقي: إخلاء المنصب وفحص الحالة (G-SEP-05، G-SEP-13)

**المشكلة:**
- طرف HR.Core إقرار فارغ ([ProcessPartyAckConsumers.cs:36-52](../../src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs)).
- مستهلك الاكتمال لا يمس المنصب، ويسجّل تحذيراً فقط إن رُفض الانتقال، ونوع التوظيف ثابت `Permanent` ([SeparationCompletedConsumer.cs:44-68](../../src/Services/HRCore/HR.Core.Infrastructure/Consumers/SeparationCompletedConsumer.cs)).
- `VacatePosition` ([EstablishmentService.cs:36-44](../../src/Services/HRCore/HR.Core.Domain/Services/EstablishmentService.cs)) لا يُستدعى في الفصل، وطلب الشاغر يشترط `Vacant` ([RequisitionWorkflowRules.cs:77-78](../../src/Services/HRCore/HR.Core.Domain/Services/RequisitionWorkflowRules.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SeparationEffect` | `HR.Core.Domain/Entities/SeparationEffect.cs` + ترحيل `SEP063_SeparationEffects` | صف لكل فصل: `SeparationRecordId` (فريد)، `EmployeeId`، `VacatedPositionId`، `PreviousEmploymentStatus`، `ExecutedAt`، `CompensatedAt` |
| `SeparationHrCoreEffectsService` | `HR.Core.Infrastructure/Services/Separation/SeparationHrCoreEffectsService.cs` | `ExecuteAsync`: الموظف موجود؛ `EmploymentStatusRules.CanTransition(current, MapSeparationType(type))` وإلا استثناء برسالة عربية؛ إخلاء المنصب المشغول بالموظف + `AppendFromPositionAsync(Vacated, EffectiveDate, decisionReference: DecisionNumber)`؛ حفظ `SeparationEffect`. idempotent بالمفتاح |
| طرف HR.Core | `ProcessPartyAckConsumers.Consume(IExecuteSeparationParty)` | يستدعي الخدمة، والدليل `hrcore:vacated:{positionId}` أو `hrcore:no-position`. التعويض ← §2 |
| حقل `DecisionNumber` | `IStartSeparationSaga` / `IExecuteSeparationParty` / حالة الـ saga | خاصية اختيارية (`=> null`) يملؤها `ExecuteSeparationCommand` من القرار (PLAN-60) |
| `SeparationCompletedConsumer` | الملف القائم | نوع التوظيف من آخر `EmploymentRecord`. وتسجيل `RehireEligibility` (§4). والرفض لا يُبلَع: يُنشر `ISeparationPostCompletionFaulted` (`SeparationRecordId`، `Reason`) |
| `SeparationPostCompletionFaultedConsumer` | `HR.Contracts.Infrastructure/Consumers/` | يفتح `ProcessIntervention` بنوع `Separation` وسبب «تعذر تطبيق الحالة في HR.Core»، ويعلّم السجل `PostCompletionIssue=true` |
| سكربت إصلاح | `scripts/sep063-vacate-orphan-positions.ps1` + `POST api/establishment/admin/vacate-separated` (`HrCoreDelete`، ومتحكمه `[OrgScopeCentralOnly]` وفق `OrgScopeCoverageTests` في PLAN-52) | مناصب `Occupied` بموظف حالته نهائية ← سرد (`-DryRun` افتراضي) ثم إخلاء بنسخة `Vacated` وسبب «PLAN-63: إصلاح فصل سابق» |

**القواعد:**

1. الإخلاء في طرف التنفيذ لا عند الاكتمال، كي يشمله التعويض.
2. موظف بلا منصب ممول ← إقرار `hrcore:no-position` (ليس خطأ).
3. لا تُمس `OrgUnit.HeadEmployeeId` ولا التفويضات هنا (الموجة 2، G-SEP-17).

### 2. الإلغاء والتعويض والتدخل (G-SEP-07)

**المشكلة:**
- **مسار الإلغاء بعد النشر لا يُوصل إليه:** النشر يشترط `EffectiveDate ≤ اليوم` والإلغاء بعده `> اليوم` ([SeparationWorkflowRules.cs:15-20](../../src/Services/HRContracts/HR.Contracts.Domain/Services/SeparationWorkflowRules.cs)). والاختبار يفرض الحالة ([Fun002SeparationRecordIntegrationTests.cs:98](../../src/Tests/HRManagement.IntegrationTests/Fun002SeparationRecordIntegrationTests.cs)).
- **إلغاء التدخل لا يحدّث السجل ولا المعاملة** ([ProcessInterventionsController.cs:91-127](../../src/Services/HRContracts/HR.Contracts.Web/Controllers/ProcessInterventionsController.cs)).
- **التعويض غير محصور:**
  - Admin.Logistics يعيد فتح كل عهدة تاريخية ([PartyAckConsumers.cs:34-53](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - IT يعيد كل صلاحية مسحوبة ([PartyAckConsumers.cs:34-49](../../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - IDCard يعيد البطاقة المفقودة `Active` ([PartyAckConsumers.cs:14-54](../../src/Services/IDCard/IDCard.Infrastructure/Consumers/PartyAckConsumers.cs)).
  - Payroll يلغي المسودة فقط ([PayrollEventConsumers.cs:93-104](../../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)) ويترك الملف معطّلاً ([InboundEventHandlers.cs:212-213](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- **الـ saga:**
  - فشل التعويض يقود إلى `AwaitingIntervention` ([SeparationStateMachine.cs:197-204](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs))، ثم `Resume` يرسل `Compensate=false` (`:163-176`، `:239-258`).
  - الإقرارات المتأخرة تُهمل (`:183`).
  - لا `ISeparationSagaFinalized(Success=false)` عند `Cancelled` (`:278-282`).

**التصميم — الـ saga:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SeparationSagaState.IsCompensating` | `SeparationStateMachine.cs` + ترحيل | يُضبط في `StartCompensation`، ويحكم `Resume`: `Compensate = IsCompensating`، والعودة إلى `Compensating` لا `AwaitingParties` |
| `ISeparationPartyCompleted.Compensated` | `ProcessSagaCommands.cs` (خاصية `=> false`) | يضبطها `SeparationPartyAckConsumerBase` ومستهلك Payroll. والـ saga تتجاهل إقراراً لا يطابق وضعها |
| الإقرار المتأخر | `During(AwaitingIntervention, When(PartyCompleted))` | يُسجَّل (`MarkCompleted`) بدل التجاهل. وإن لم يبق معلّق يُنهى مباشرة (اكتمال أو إلغاء حسب الوضع) |
| النهاية | `Cancelled` | تنشر `ISeparationSagaFinalized(Success=false, Cancelled=true)`. وتُحذف الحالة `Faulted` غير المستخدمة |
| `SeparationSagaFinalizedConsumer` | [SagaSupportConsumers.cs:56-94](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs) | `Cancelled=true` ← السجل `Cancelled` والمعاملة `Cancelled` (بـ `SeparationRecordId`)، وبنود البراءة المفتوحة `Cancelled` |

**التصميم — أوامر الإلغاء:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `SeparationStatus.Compensating` | PLAN-60 + ترحيل | `Executing`/`AwaitingIntervention → Compensating → Cancelled`. ومن `Compensating` إلى `AwaitingIntervention` عند الفشل |
| `CancelSeparationCommand` | PLAN-60 | للحالتين أعلاه: سبب إلزامي، و`ICancelSeparationSaga` عبر Outbox، والسجل `Compensating`. و`Completed` ← رفض «يلزم قرار تصحيحي» |
| `ProcessInterventionsController.Cancel` | الملف القائم | لنوع `Separation`: يمر عبر `CancelSeparationCommand`، فلا مسار إلغاء ثانٍ |
| حذف المسار الميت | `SeparationWorkflowRules.CanCancel` القديم والاختبار `Cancel_AfterPublish_PreEffective_PublishesCancelSaga` | يستبدلهما اختبار المسار الحقيقي |

**التصميم — تعويض محصور بعلامة في كل طرف:**

| الطرف | العلامة | التعويض |
|-------|---------|---------|
| IDCard | `IssuedCard.RevokedBySeparationId`، `StatusBeforeSeparation` | يعيد الحالة السابقة (`Lost` تبقى `Lost`) للبطاقات المعلّمة بهذا الفصل فقط |
| IT.Infra | `SystemPermissionRecord.RevokedBySeparationId` | يعيد الصلاحيات المعلّمة فقط. والأجهزة: إغلاق بنود البراءة بنتيجة `Cancelled` ومسح `PendingSeparationRecordId` |
| Admin.Logistics | `AssetAssignment.PendingSeparationRecordId` (PLAN-62) | مسح العلامة وإغلاق البنود `Cancelled`. ويُحذف منطق «إعادة فتح كل ما أُعيد» |
| Payroll | `EmployeePayProfile.DeactivatedBySeparationId` | إعادة تفعيل الملف المعلّم إن لم يوجد ملف أحدث (`IsActive=true`، `EffectiveTo=null`، مع لقطة تاريخ). وهذا كاتب لـ `EmployeePayProfiles` يُضاف إلى `PayProfileWritersTests` (PLAN-53) بسببه. وإلغاء خصومات الفصل بمرجعها، والتسوية المسودة ← `Cancelled`. أما `Approved`/`Paid` ← استثناء (`Faulted`) |
| HR.Core | `SeparationEffect` | إعادة الإشغال إن بقي المنصب `Vacant` بلا طلب شاغر مفتوح (نسخة `Occupied` بسبب «تعويض فصل ملغى»)، وإلا استثناء |
| HR.Contracts (طرف جديد، §3) | `Contract.TerminatedBySeparationId`، `CancelledBySeparationId` على الطلبات | إعادة العقد `Active` وتاريخ نهايته السابق، وإعادة الطلبات `Pending` |
| Auth.Directory | PLAN-62 | — |
| HR.Health، Records.Archive | قائمتان | يُتحقق منهما فقط |

**القواعد:**

1. كل تعويض idempotent ومحصور بـ `SeparationRecordId`. وأي سجل لم يغيّره هذا الفصل لا يُمس (يُثبت باختبار لكل طرف بسجل تاريخي مزروع).
2. ترتيب التعويض عكس ترتيب الإكمال كما هو، مع الإقرار بالعلم `Compensated=true`.
3. لا يُنشر `ISeparationCompleted` من `Compensating` أو بعده (اختبار حالة).

### 3. طرف HR.Contracts: الطلبات المعلقة (G-SEP-13)

**المشكلة:**
- HR.Contracts ليس طرفاً، فالمستهلك `ContractsSeparationPartyConsumer` ميت ([SagaSupportConsumers.cs:122-163](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs)).
- لا شيء يغلق الإجازات والسلف المعلقة للموظف المنفصل.
- `ISeparationStepCompleted` يُنشر بلا مستهلك ([MassTransitEventPublisher.cs:265-280](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Messaging/MassTransitEventPublisher.cs)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `ProcessParties.Contracts` طرفاً إلزامياً | `SeparationSagaOptions` + `appsettings.json` | أول الأطراف |
| `ContractsSeparationPartyConsumer` | الملف القائم | التنفيذ: الإجازات والسلف بحالة `Pending` ← `Cancelled` بسبب «إنهاء خدمة» و`CancelledBySeparationId`. والإجازات المعتمدة التي تبدأ بعد تاريخ النفاذ ← `Cancelled` + `PublishLeaveCancelledAsync` (يستهلكه Payroll). والدليل `contracts:leave={n}:advance={m}`. والتعويض ← الجدول أعلاه |
| `ISeparationStepCompleted` | المنشر والواجهة | يُوقف النشر، وتُعلَّم الواجهة `[Obsolete]` |

### 4. إعادة التعيين (G-SEP-14)

**المشكلة:**
- الحالات النهائية لا تنتقل إلا إلى `Anonymized` ([EmploymentStatusRules.cs:42-45](../../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)).
- ربط موظف قائم عند التعيين لا يغيّر حالته ([RecruitmentHireClosureService.cs:224-229](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs)).
- طرف الدليل في التعيين يشترط حساباً نشطاً ([Auth PartyAckConsumers.cs:40-52](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs)).

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `RehireEligibility` على `Employee` | `HR.Core.Domain/Entities/Employee.cs` + ترحيل | `Eligible`/`RequiresReview`/`Ineligible`، و`LastSeparationRecordId`، و`LastSeparationType`. يضبطها `SeparationCompletedConsumer` حسب D-SEP-7 (إعداد `Separation:Rehire:EligibilityByType`) |
| `EmployeeLifecycleService.Rehire` | `HR.Core.Domain/Services/EmployeeLifecycleService.cs` | انتقال `Terminated/Resigned/Retired → PendingActivation` بالمصدر `StatusChangeSource.Rehire` وحده (PLAN-61). و`Deceased/Merged/Anonymized` ممنوعة |
| مسار الربط في التعيين | `RecruitmentHireClosureService.cs:224-229` | موظف بحالة نهائية: `Eligible` ← `Rehire`. و`RequiresReview` ← يلزم `RehireReviewReason` ومعتمد `hr-management-approver` ≠ المُعيِّن (حقلان مُدقَّقان). و`Ineligible` ← رفض برسالة |
| إعادة تفعيل الحساب | `IdentityLeaverService.ReactivateForRehireAsync` (Auth.Directory، يبني على PLAN-62) + `EmployeeStatusChangedConsumer` | من حالة نهائية إلى `PendingActivation`/`Active`: `enabled=true`، والدور الافتراضي وحده (لا الأدوار القديمة)، ومسح حقول التعطيل |

**القواعد:** الحلقة السابقة (سجل الفصل والتسوية) لا تُحذف ولا تُعدَّل. وإغلاق خصومات الحلقة السابقة في التسوية ← PLAN-64.

---

## Out of scope

| البند | الخطة |
|-------|-------|
| معادلة التسوية ودفتر السلف الواحد وعدم تكرار الخصم بعد إعادة التعيين | PLAN-64 |
| رئاسة الوحدة والتفويضات للمنفصل، والتقارير، وDSAR | الموجة 2 (G-SEP-17) |
| مهل الأطراف من المصفوفة ومتابعة SLA، والتنفيذ المجدول | الموجة 2 (G-SEP-18، SEP-G) |
| الإلغاء بعد `Completed` (قرار تصحيحي) | الموجة 2 |
| نشر الأحداث بعد آخر حفظ عموماً | PLAN-40 |

## Acceptance scenarios

على الملف الشبيه بالإنتاج. الشخصيات: `a-leaver` (له منصب ممول، وبطاقة مفقودة قديمة، وعهدة أُعيدت سابقاً، وصلاحية IT مسحوبة سابقاً، وإجازة معلقة، وإجازة معتمدة بعد النفاذ)، `a-contracts-checker`، `central-a`، `a-management-approver`، `a-leaver-2` (مستقيل قديم لإعادة التعيين، جديد)، `a-leaver-disc` (مفصول تأديبياً، جديد). وكل مغادر نسخة جديدة من قالبه لكل سيناريو بـ `AcceptanceLeaverFixture` (PLAN-60)، تُوسَّع هنا بالتجهيزات التاريخية أعلاه، فلا يعتمد سيناريو على حالة تركها غيره.

| ID | Positive | Negative |
|----|----------|----------|
| AC-SEP-63a | تنفيذ فصل `a-leaver` حتى `Completed` ← المنصب `Vacant` بنسخة `Vacated` تحمل رقم القرار ← `central-a` ينشئ طلب شاغر على المنصب | — |
| AC-SEP-63b | إلغاء وهو `Executing` بسبب ← `Compensating` ثم `Cancelled`. والتحقق: ملف الأجر نشط، العقد `Active` بتاريخ نهايته السابق، المنصب مشغول، البطاقة المفقودة القديمة `Lost`، العهدة التاريخية مُعادة كما كانت، الصلاحية المسحوبة سابقاً مسحوبة، الإجازة المعلقة `Pending`، المعاملة `Cancelled` | لا `ISeparationCompleted` في كامل المسار |
| AC-SEP-63c | بعد تشغيل IDCard والاستئناف ← يكمل التعويض ← `Cancelled` | إيقاف IDCard أثناء التعويض ← `AwaitingIntervention`، و`Resume` لا يرسل `Compensate=false` (فحص الرسائل) |
| AC-SEP-63d | — | موظف حالته `Retired` مسبقاً (تجهيز SQL موثق) ← طرف HR.Core `Faulted` برسالة الانتقال ← تدخل، لا تحذير صامت |
| AC-SEP-63e | التنفيذ يلغي الإجازة المعلقة والسلفة المعلقة والإجازة المعتمدة المستقبلية، وPayroll يستلم `ILeaveCancelled` | — |
| AC-SEP-63f | `a-leaver-2` يُعيَّن عبر مسار التوظيف بربط السجل ← `PendingActivation` ← `Active` بعد saga التعيين، وحسابه مفعّل بدور `hr-employee` وحده | `a-leaver-disc` بلا سبب ومعتمد ← رفض. و`Death` ← رفض دائم |
| AC-SEP-63g | — | أُسند منصب `a-leaver` لغيره ثم أُلغي الفصل ← طرف HR.Core `Faulted` ← تدخل، والإسناد الجديد سليم |
| AC-SEP-63h | إقرار متأخر بعد انتهاء المهلة يُسجَّل، والاستئناف لا يعيد إرسال الطرف المكتمل | — |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "Sep063|SeparationStateMachine|EmploymentStatusRules|Rehire|SagaPartyCompensation"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Sep063|Int003|Fun002|G3Separation|RecruitmentHireClosure"
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --build --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcSep63"
```

**اختبارات جديدة:**
- **Unit:**
  - `Sep063SagaCompensationTests` في `ITestHarness`: الاستئناف في وضع التعويض، والإقرار المتأخر، وإقرار غير مطابق للوضع، ونهاية `Cancelled` تنشر `Finalized(false)`.
  - `Sep063RehireRulesTests`.
  - تعويض كل طرف بسجل تاريخي مزروع، بستة اختبارات: `Sep063IdCardCompensateTests`، `…ItInfra…`، `…AdminLogistics…`، `…Payroll…`، `…HrCore…`، `…Contracts…`.
- **Integration على PostgreSQL** (`PostgreSqlFixture`):
  - `Sep063SeparationEffectPostgresTests`: الإخلاء وحفظ `SeparationEffect` في معاملة واحدة، وتكرار الرسالة ← صف واحد.
  - `Sep063CompensationPayrollPostgresTests`: إعادة تفعيل الملف مع ملف أحدث موجود ← لا إعادة.
  - المعاملة `Cancelled` مع السجل في الحفظ نفسه.

## Definition of done

- لا يوجد تعويض في أي طرف يمس سجلاً لم يغيّره الفصل نفسه.
- لا مسار إلغاء للفصل غير `CancelSeparationCommand`.
- سجل الفصل والمعاملة وحالة الـ saga متسقة في كل نهاية (اكتمال، إلغاء، تدخل).
- كل فصل مكتمل يترك المنصب `Vacant`. وسكربت الإصلاح شُغّل على القبول وسُجّلت نتيجته.
- إعادة التعيين تعمل بالقاعدة وبلا تعديل يدوي على القاعدة.
- AC-SEP-63a..h = Pass، ومجلد `acceptance-evidence/AC-SEP-63/`.
- سطور «الحالة» لـ G-SEP-05 وG-SEP-07 وG-SEP-13 (الباقي) وG-SEP-14 محدّثة.
- العمل في commits محلية على الفرع: لا دفع (PLAN-35 للمالك)، ولا أسرار ولا `global.json` ولا `testdata/*/out/*.json` معاد كتابتها.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-63-sep-saga-compensation-core-effects.md` |
| Saga/Contracts | `SeparationStateMachine.cs`، `ProcessSagaCommands.cs` (`Compensated`، `DecisionNumber`)، `ISeparationPostCompletionFaulted`، `SagaSupportConsumers.cs`، `ProcessInterventionsController.cs`، ترحيل حالة الـ saga |
| HR.Contracts | `SeparationStatus.Compensating`، `CancelSeparationCommand`، طرف العقود، العلامات على `Contract`/`LeaveRequest`/`AdvanceRequest`، ترحيل `SEP063_ContractsCompensationMarkers` |
| HR.Core | `SeparationEffect`، `SeparationHrCoreEffectsService`، الطرف، `SeparationCompletedConsumer`، `RehireEligibility`، `EmployeeLifecycleService.Rehire`، `RecruitmentHireClosureService`، ترحيل `SEP063_SeparationEffects`، نقطة الإصلاح |
| أطراف | IDCard، IT.Infra، Admin.Logistics، Payroll: العلامات والترحيلات والتعويض المحصور |
| Auth.Directory | `ReactivateForRehireAsync` |
| Tests | أعلاه، و`AcSep63SagaCompensationTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، والشخصيتان `a-leaver-2` و`a-leaver-disc` في `personas.json` و`AcceptancePersonaCatalog.cs` (+2 على العدد وقت التنفيذ)، وتجهيزات `a-leaver` التاريخية في `AcceptanceLeaverFixture` |
| Event inventory | `docs/EVENT_PUBLISHER_INVENTORY.md`: `ISeparationPostCompletionFaulted` (من مستهلك HR.Core عبر outbox المستهلك)، وناشر `ILeaveCancelled` الجديد في `ContractsSeparationPartyConsumer` (شرطا R3/R4 في حارس PLAN-40) |
| Scripts | `scripts/sep063-validate.ps1`، `scripts/sep063-vacate-orphan-positions.ps1` |
| Docs | `PROCESS_SAGAS.md` (التعويض والاستئناف والأطراف)، `FUN_002_RECONCILIATION_CHECKLIST.md` (جدول التعويض الجديد)، `FUN_002_EXCEPTION_PATHS.md` (الإلغاء حسب الحالة وإعادة التعيين)، `EMPLOYEE_LIFECYCLE.md` (انتقال `Rehire`)، وسطور «الحالة» |

## Risks

| الخطر | التخفيف |
|-------|---------|
| ترحيل أعمدة العلامة لا يغطي فصولاً نُفّذت قبله | التعويض بلا علامة يرفض (`Faulted`: «فصل سابق لـ PLAN-63 — تعويض يدوي») بدل التعويض الواسع القديم |
| تغيير الـ saga يمس نسخاً قائمة في `SeparationSagaStates` | العمود الجديد افتراضه `false`، والنسخ في `Compensating` تُعلَّم `true` في الترحيل |
| إعادة الإشغال تتعارض مع طلب شاغر بدأ بعد الإخلاء | القاعدة: لا إعادة مع طلب مفتوح ← تدخل يقرر فيه HR |
| حقل `Compensated` الجديد في رسالة إقرار تنشرها خدمات قديمة | القيمة الافتراضية `false`، والـ saga في وضع التعويض لا تقبل إقراراً بلا العلم إلا من طرف مُدرج في قائمة توافق مؤقتة (`Sagas:Separation:LegacyCompensationAckParties`) |

## Effort estimate

9–10 أيام عمل: طرف HR.Core والإخلاء والإصلاح (1.5)، الـ saga والإلغاء الموحد (2)، تعويض ستة أطراف بالعلامات (3)، طرف العقود (1)، إعادة التعيين (1)، القبول والتوثيق (1.5).
