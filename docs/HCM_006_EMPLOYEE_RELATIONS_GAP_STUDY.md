# دراسة علاقات العمل والتأديب والقضايا والامتثال (ER) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** مسار علاقات العمل من الشكوى أو الواقعة حتى تنفيذ العقوبة: الشكوى ← التحقيق ← سماع الأقوال (حق الدفاع) ← القرار بسلّم العقوبات القانوني ← الاستئناف ← التنفيذ (الرواتب، إنهاء الخدمة، الترفيع، ملف الموظف). ويشمل في خدمة Compliance: `DisciplinaryActions` و`LegalCases` و`LegalReferrals` و`Complaints` و`AuditFindings` و`ComplianceRecommendations` واللوحة والتصدير، ويشمل `FieldInspectionVisits` في HR.Core، وكل شيفرة علاقات عمل في HR.Core وHR.Contracts. خارج النطاق: DSAR (يملكه مجال الخصوصية).  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والوثائق في `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. تتبّعنا كل حدث من المنتج إلى المستهلك عبر Compliance وPayroll وHR.Contracts وHR.Movements وRecords.Archive وDev.Analytics.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات، لعدم وجود .NET SDK على الجهاز. كل فجوة مدعومة بمرجع ملف/سطر تحققنا منه بالعرض. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [HCM_006_EMPLOYEE_RELATIONS.md](HCM_006_EMPLOYEE_RELATIONS.md) · [ADR-FUN-004](ADR-FUN-004-health-training-compliance-closure.md) · [FUN_004_JOURNEY_MATRIX.md](FUN_004_JOURNEY_MATRIX.md) · [FUN_004_EXCEPTION_PATHS.md](FUN_004_EXCEPTION_PATHS.md) · [FUN_004_UAT_PACK.md](FUN_004_UAT_PACK.md) · [FUN_004_VERIFICATION.md](FUN_004_VERIFICATION.md) · [completion-backlog/09 (HCM-006)](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md) · [completion-backlog/04 (FUN-004)](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [completion-backlog/14 (SYD-001)](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md) · [syr/legal-source-binding.md](syr/legal-source-binding.md) · [syr/records-retention-matrix.json](syr/records-retention-matrix.json) · [EVENT_PUBLISHER_INVENTORY.md](EVENT_PUBLISHER_INVENTORY.md) · [THESIS_REQUIREMENTS_CATALOG_CURRENT.md](THESIS_REQUIREMENTS_CATALOG_CURRENT.md) (FR-COMP-01..03)

---

## 1. الخلاصة

1. **المسار غير مبني إلا كسجلات CRUD.** توجد شاشات للشكاوى والإجراءات التأديبية والقضايا والإحالات والملاحظات الرقابية، وقائمة تحقيق من 4 خطوات، وحدثان عابران للخدمات. لا يوجد كيان تحقيق ولا جلسة استماع ولا سلّم عقوبات ولا قرار باعتماد ولا تنفيذ مؤجل. عبارة «Done» في بطاقتَي HCM-006 وFUN-004، و«ReadyForSign-off» في حزمة UAT، لا تطابق الشيفرة. والدليل الذي تستند إليه HCM-006 نموذج `SharedKernel.Cases.EmployeeRelationCase` **غير مستخدم** في التشغيل، وهو النمط نفسه الذي وجدته دراسة التوظيف (G-23).
2. **4 عوائق P0:**
   - **(أ)** الإجراء التأديبي يصدر ويُنفَّذ أثره المالي بطلب POST واحد من أي مستخدم `compliance`: لا تحقيق مسبق، ولا اعتماد، ولا فصل واجبات. ومسار `DISCIPLINARY-APPROVAL` وقاعدة `R-DISC` يتيمان، يخفيهما سبب غير صحيح في قائمة السماح.
   - **(ب)** «الفصل» التأديبي يعطّل ملف الأجر في Payroll مباشرة دون مسار إنهاء خدمة، فيبقى الموظف «نشطاً» في HR.Core، ولا تُنشأ تسوية نهاية الخدمة لاحقاً.
   - **(ج)** إغلاق **أي** قضية قانونية ينشر `ILegalJudgmentIssued` لموظف `Guid.Empty`، فتنشئ HR.Contracts سجل إنهاء خدمة «قضائياً» ومعاملة لموظف غير موجود.
   - **(د)** سلّم العقوبات القانوني غير ممثَّل (4 أنواع عامة)، و`PenaltyDays` يُرسل دائماً `0`، فلا يحدث أي خصم أبداً.
3. **ثغرات سلامة (P1):** جدار السرية وحماية المبلِّغ لا يعملان، إذ تكشف هويته التصديرات ويراها كل محقق بعد الإسناد، ويصل نص القرار إلى التحليلات والأرشيف. والاستئناف نص حر بلا أثر ولا تعويض. والتحقيق لا يفرض خطواته، ولا يفصل بين المحقق والمقرِّر. وكل متحكمات Compliance تربط الكيانات مباشرة (overposting)، واختبار المعمارية الذي يمنع ذلك يتخطى `Compliance.Web` بصمت.
4. **فجوات قدرات (P2):** المواعيد والتقادم والتصعيد، وآثار التنفيذ خارج الرواتب (حجب الترفيع، ملف الموظف، حالة الإيقاف)، وقناة الموظف (الشكوى والدفاع والاستئناف)، والتظلم الجماعي، وإدارة القضايا والإحالات، ودورة حياة الرقابة والتفتيش.
5. **فجوة تحقق:** لا سيناريو قبول ولا مجلد أدلة لأي خطوة في المسار. اختبارات الرواتب تستخدم `PenaltyDays` قدره 2 أو 3، وهي قيمة لا ينتجها الإنتاج. ولا يوجد أي اختبار لمتحكم في Compliance، لأن أي مشروع اختبار لا يشير إلى `Compliance.Web`.

**التقدير الإجمالي:** نحو 47–57 يوم عمل لمطوّر واحد، منها 17–20 يوماً للمرحلة P0 (ER-A وER-B). ويتوقف جزء من P0 على قرار المالك D-ER-1 (النص المصدّق للقانون 50/2004، وهو `SYD-001` بحالة OPEN-NO-GO).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | تلقي الشكوى/البلاغ | `ComplaintsController.Create` يُدخله موظف `compliance` فقط. «مجهول» علم شكلي، و`FiledBy` إلزامي ونصي | ⚠️ (G-ER-05، G-ER-11) |
| 2 | إسناد المحقق | `Assign` بمعرّف GUID نصي. فصل الواجبات يقارن بمُدخِل البيانات، لا بالمبلِّغ ولا بالمشكو منه | ⚠️ (G-ER-07) |
| 3 | التحقيق وجمع الأدلة | `InvestigationChecklist` (4 خطوات بمالك وSLA) + دليل FILE-002، لكن الانتقالات لا تشترطها | ⚠️ (G-ER-07) |
| 4 | سماع الأقوال (حق الدفاع) | غير موجود: لا استدعاء ولا محضر ولا مهلة دفاع ولا تبليغ | ❌ (G-ER-07، G-ER-11) |
| 5 | قرار الشكوى | `Resolve` نص حر + `IComplaintResolved` | ⚠️ (G-ER-05، G-ER-07) |
| 6 | القرار التأديبي وسلّم العقوبات | `DisciplinaryActions.Create`: `Warning/Suspension/Dismissal/Other`، بلا مدة ولا مبلغ ولا مرجع قانوني ولا اعتماد | ❌ (G-ER-01، G-ER-04) |
| 7 | الاستئناف | `Appeal` ثم `DecideAppeal` بنص حر يُلحق بالملاحظات، بلا نتيجة ولا أثر | ❌ (G-ER-06) |
| 8 | التنفيذ — الرواتب | `IDisciplinaryActionTaken` لحظة الإنشاء. `PenaltyDays=0` فلا خصم، و«الفصل» يعطّل ملف الأجر | ❌ (G-ER-02، G-ER-04) |
| 9 | التنفيذ — إنهاء الخدمة | لا مستهلك في HR.Contracts لـ `IDisciplinaryActionTaken`، و`SeparationType.Disciplinary` بلا منتج | ❌ (G-ER-02) |
| 10 | التنفيذ — الترفيع والحالة | لا فحص تأديبي في اعتماد الترفيع، ولا انتقال إلى `EmploymentStatus.Suspended` | ❌ (G-ER-10) |
| 11 | التنفيذ — ملف الموظف | لا شيء للتأديب. الشكوى المحلولة تُؤرشف بعنوان يحوي نص القرار | ❌ (G-ER-10، G-ER-05) |
| 12 | القضايا والأحكام | `LegalCase` بلا موظف. `Close` ينشر حكماً لـ `Guid.Empty` | ❌ (G-ER-03، G-ER-12) |
| 13 | الإحالات القانونية | سجل نصي بلا مصدر ولا نتيجة | ⚠️ (G-ER-12) |
| 14 | الملاحظات الرقابية والتوصيات والتفتيش | CRUD بحالات تُعدَّل بحرية. التفتيش الميداني إنشاء فقط | ⚠️ (G-ER-13) |
| 15 | المواعيد والتقادم | `SlaHours` تُخزَّن ولا يقرؤها أي سطر، ولا مهمة خلفية في الخدمة | ❌ (G-ER-09) |
| 16 | اللوحة والتصدير | 4 عدّادات خام. التصدير يكشف `FiledBy` للشكاوى المجهولة | ⚠️ (G-ER-05، G-ER-14) |

**وضع محرك السياسات:** Compliance لا تقرأ أي مفتاح `PolicyEngine:*` ولا تستدعي `IPolicyEnforceClient`. العميل مسجَّل في `Program.cs:44` ولا يُحقن في أي متحكم. ولو استُدعي، فغياب `PolicyEngine:Compliance.Disciplinary` يعني `Off`، فيعيد الـshim قراراً بلا `TriggerWorkflow` ([PolicyEnforceClient.cs:29-40](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)). النمط هنا عكس نمط التوظيف: لا يعلق شيء بانتظار اعتماد، بل **يُطبَّق كل شيء فور الحفظ** (توأم G-02 في EstablishmentChange).

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **سجل تدقيق عام append-only:** `AuditSaveChangesInterceptor` يسجّل المستخدم والحقول المتغيرة لكل كيان، ويحجب حقول الشكوى الحساسة بـ `[AuditRedact]` (`FiledBy`، `Against`، `Description`، `Resolution`).
- **لا حذف صلب:** متحكمات Compliance بلا إجراء Delete. والشكوى تُلغى إلغاءً ناعماً بسبب (`ComplaintProtectionRules.SoftCancel`)، ولا تُلغى بعد الإغلاق.
- **المرفقات:** أدلة الشكوى والتأديب تمر عبر FILE-002 (`IFileReferenceClient`)، ويُرفض المرفق غير النظيف.
- **البنية:** Outbox مُعدّ (`AddPostgresOutbox<ComplianceDbContext>`)، وعزل المستأجر بـ RLS (TEN-002B)، ورقم القضية يُولَّد من COD-001 ولا يُعدَّل.
- **قائمة تحقيق بخطوات فريدة** (`IX_InvestigationChecklists_ComplaintId_Step`) لكل خطوة فيها مالك وSLA ودليل إلزامي عند الإنجاز.
- **في الرواتب:** مستهلك `IDisciplinaryActionTaken` غير تكراري بإيصال الرسالة (INT-002)، ومحرك الرواتب يطبّق `DisciplinaryDeduction` مرة واحدة لكل تشغيل ([PayrollRules.cs:96-108](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)). المسار المستقبِل جاهز، لكن لا يصله أي مبلغ (G-ER-04).
- **لبنات موجودة يمكن البناء عليها:** `SeparationType.Disciplinary/Judicial` في العقود، و`EmploymentStatus.Suspended` في HR.Core، و`AdministrativeDecision` برقم قرار في العقود، ونمط ربط المرجع القانوني في التوظيف (`RecruitmentLegalSourceValidator`).
- **منتقي الموظف** في نموذج التأديب، وأسماء الموظفين تُحلّ عبر S2S (`IHrCoreLookupClient`).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-ER-01 — الإجراء التأديبي يصدر ويُنفَّذ بطلب واحد: لا تحقيق ولا اعتماد ولا فصل واجبات، ومسار `DISCIPLINARY-APPROVAL` يتيم
- **الدليل:**
  - [ComplianceExtendedControllers.cs:156-224](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs): `Create` يحفظ الإجراء، ثم ينشر `IDisciplinaryActionTaken` فوراً (`:207-219`). لا حالة مسودة، ولا ربط إلزامي بشكوى محقَّق فيها، ولا سلطة إيقاع.
  - الحارس الوحيد تحصيل حاصل: `model.AppealStatus = AppealStatus.None;` (`:166`) ثم `if (!DisciplinaryWorkflowRules.CanIssue(model.AppealStatus))` (`:167`). و`CanIssue` يعيد `status == AppealStatus.None` ([DisciplinaryWorkflowRules.cs:7-8](../src/Services/Compliance/Compliance.Domain/Services/DisciplinaryWorkflowRules.cs))، فهو صحيح دائماً. أما ادعاء «إصدار تأديب أثناء Appeal Pending ← CanIssue يرفض» ([FUN_004_EXCEPTION_PATHS.md:11](FUN_004_EXCEPTION_PATHS.md)) فلا يفحص استئنافات الموظف السابقة أصلاً.
  - لا سياسة صريحة على أي POST من 29 إجراءً في `ComplaintsController` (11) و`ComplianceExtendedControllers` (18). المرشح العام يطبّق `ModuleCompliance` فقط ([Program.cs:24](../src/Services/Compliance/Compliance.Web/Program.cs))، وهي `Admin | compliance` ([HrManagementAuthorizationExtensions.cs:50, 194-199](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). لذلك يُنشئ المستخدم نفسه الشكوى، ويسند لنفسه، ويحلّها، ويصدر العقوبة، ويقدّم الاستئناف، ويبت فيه.
  - في HR.Core: التعريف `DISCIPLINARY-APPROVAL` منشور بخطوة واحدة لدور `compliance` ([WorkflowPackSeeder.cs:44-46](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs))، والقاعدة `R-DISC` تطلقه عند `Create` ([PolicyEngineDomainSeeds.cs:120-126](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)). لكن لا شيء في Compliance يستدعي المحرك أو يبدأ المسار أو يستهلك نتيجته، و`WorkflowInstanceActionService` لا ينشر نتيجة لـ `DisciplinaryAction`.
  - قائمة السماح في [WorkflowDefinitionsHaveHandlersTests.cs:21](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs) تبرر الاستثناء بعبارة `"DISCIPLINARY-APPROVAL — the compliance service owns the action"`. **هذا غير صحيح:** Compliance لا تملك أي مستهلك لنتيجة المسار (المستهلكان الوحيدان فيها `MedicalRecommendationIssuedConsumer` و`ExportJobRequestedConsumer`). والسجل `PolicyDomainRegistry` يصف المجال بأنه `"Shadow"` ([PolicyTemplatesController.cs:235](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)) دون أي مستدعٍ.
  - الوثائق تدّعي العكس: «No disciplinary action outside approved workflow (WF-001)» و`[x] … status machine enforces sequence` ([HCM_006_EMPLOYEE_RELATIONS.md:54, 63](HCM_006_EMPLOYEE_RELATIONS.md))، و«التأديب لا يُصدر خارج workflow» ([ADR-FUN-004:34](ADR-FUN-004-health-training-compliance-closure.md))، وU6 «رفض خارج workflow» ([FUN_004_UAT_PACK.md:10](FUN_004_UAT_PACK.md)).
- **الأثر:** موظف امتثال واحد يوقع عقوبة على أي موظف في الجهة، ويصل أثرها إلى الرواتب في الدقيقة نفسها، دون تحقيق موثق ولا سماع أقوال ولا سلطة مختصة. أي قرار كهذا معيب قانونياً وقابل للإبطال، ولا يوجد سجل يثبت من قرر ولماذا.
- **الإصلاح المقترح:**
  - آلة حالات للقرار التأديبي على الخادم: `Draft → PendingDecision → Decided → Notified → (Appealed) → Final → Executed | Annulled`. ولا يُنشر أي حدث أثر قبل `Final` (انظر D-ER-3).
  - شرط مسبق للقرار: تحقيق مغلق فيه محضر سماع أقوال أو تنازل موثق (الحد الأدنى هنا، والتوسعة في G-ER-07).
  - سلطة إيقاع حسب فئة العقوبة (مصفوفة من D-ER-1 وD-ER-5)، وفصل واجبات على الخادم: المُنشئ ≠ المحقق ≠ المقرِّر.
  - حسم آلية الاعتماد (D-ER-2): سلسلة محلية في Compliance مع **سحب** `DISCIPLINARY-APPROVAL` و`R-DISC` كما سُحب `RECRUITMENT-HIRE`، أو ربطها فعلاً بالمحرك مع حدث نتيجة ومستهلك. وفي الحالتين يُحذف `DisciplinaryAction` من قائمة السماح.
  - سياسات صريحة لكل POST (`ComplianceInvestigate`، `ComplianceDecide`…) بعد قرار الأدوار.
- **الخطة:** [PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) (جزئياً: ربط الإجراء التأديبي بـ `DISCIPLINARY-APPROVAL` في المحرك، ولا نشر إلى الرواتب قبل الاعتماد)، و[PLAN-51](acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md) (جزئياً: سياسات صريحة على إجراءات Compliance الحساسة). وآلة القرار والتحقيق والسلطة: الموجة 2 (ER)
- **التحقق المطلوب:** موجب: إنشاء قرار ← لا حدث ولا صف في Payroll ← محقق يغلق التحقيق بمحضر ← مقرِّر بالسلطة المناسبة يقرر ← لا أثر قبل `Final`. سالب: المقرِّر = المحقق ← رفض الخادم. قرار بلا تحقيق مغلق ← رفض. POST مباشر بدور لا يملك سلطة الفئة ← 403. `WorkflowDefinitionsHaveHandlersTests` ينجح دون `DisciplinaryAction` في قائمة السماح.

#### G-ER-02 — «الفصل» التأديبي يعطّل ملف الأجر مباشرة خارج مسار إنهاء الخدمة، ويُسقط تسوية نهاية الخدمة
- **الدليل:**
  - [InboundEventHandlers.cs:610-614](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs): `if (request.ActionType is "Dismissal" && profile is not null) { profile.IsActive = false; profile.EffectiveTo = DateTime.UtcNow; }`. يحدث هذا لحظة استلام الحدث، أي لحظة إنشاء الإجراء في Compliance (G-ER-01)، قبل أي استئناف، ودون لقطة تاريخ (`CreateHistorySnapshot`) كما يفعل مسار الانفصال.
  - تشغيل الرواتب يأخذ الملفات النشطة فقط: `.Where(p => p.IsActive)` ([PayrollRunJobHandlers.cs:90-91](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs))، فيختفي الموظف من الرواتب.
  - لا مستهلك لـ `IDisciplinaryActionTaken` في HR.Contracts ولا في HR.Core (الحدث له مستهلك وحيد في Payroll). و`SeparationType.Disciplinary` موجود ([SeparationType.cs:19-20](../src/Services/HRContracts/HR.Contracts.Domain/Enums/SeparationType.cs)) بلا أي منتج، فيبقى الموظف `Active` في HR.Core ولا تُفتح معاملة انفصال، فلا تُسحب حساباته ولا بطاقته ولا عهدته.
  - إذا أنشأ HR لاحقاً انفصالاً صحيحاً، فإن `HandleSeparationCompletedHandler` يبحث عن ملف نشط (`GetActiveProfileAsync`، [InboundEventHandlers.cs:205-206](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). ولأن الملف معطَّل، يُتخطى كامل الكتلة `:206-244` التي تنشئ `EndOfServiceSettlement` (`:222-232`) واسترداد السلف.
  - المتطلب صريح: «تنفيذ إجراءات إنهاء الخدمة بأنواعها (… قرار تأديبي …)» ([Requariments.md:37](Requariments.md)).
- **الأثر:** يُقطع راتب الموظف بقرار فردي غير نهائي. ولا يُحسب له تعويض نهاية الخدمة حتى لو اتُّبع المسار الصحيح لاحقاً. وتصبح الخدمات غير متسقة: HR.Core «نشط» وPayroll «منتهٍ» والعقود بلا معاملة. والتصحيح يدوي في قاعدة البيانات.
- **الإصلاح المقترح:**
  - يتوقف Payroll عن تعطيل الملف بسبب حدث تأديبي، ويبقى `ISeparationCompleted` الطريق الوحيد لإنهاء ملف الأجر.
  - قرار الفصل النهائي (`Final`) يُنشئ طلب انفصال `SeparationType.Disciplinary` في HR.Contracts بمرجع القرار، ثم يمر بمسار SEP المعتاد (D-ER-4).
  - احتواء فوري (ER-A، المرحلة 0): منع نوع `Dismissal` في Compliance حتى يُنجز ER-B، وسكربت يسرد ملفات الأجر المعطلة بعلامة `DisciplinaryAction-*` لمراجعتها.
- **الخطة:** [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (جزئياً، احتواء: Payroll لا يعطّل الملف بحدث تأديبي، والفصل التأديبي طلب فصل `Draft`). وقرار الفصل النهائي: الموجة 2 (ER)
- **التحقق المطلوب:** موجب: قرار فصل نهائي ← سجل انفصال `Disciplinary` في العقود يحمل مرجع القرار ← بعد إتمامه `ISeparationCompleted` ← تسوية نهاية خدمة واحدة. سالب: قرار فصل غير نهائي أو تحت الاستئناف ← ملف الأجر يبقى نشطاً ويظهر في التشغيل.

#### G-ER-03 — إغلاق أي قضية ينشئ إنهاء خدمة «قضائياً» لموظف فارغ
- **الدليل:**
  - [ComplianceExtendedControllers.cs:108-131](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs): `Close` ينشر `ILegalJudgmentIssued` بالقيمة `EmployeeId = Guid.Empty` (`:120`)، وبتاريخ نفاذ `DateTime.UtcNow`، لكل قضية تُغلق. والكيان `LegalCase` لا يملك حقل موظف أصلاً ([LegalCase.cs:8-29](../src/Services/Compliance/Compliance.Domain/Entities/LegalCase.cs)).
  - المستهلك في العقود يحوّل كل حكم إلى طلب انفصال: `ProcessSeparationRequestCommand(context.Message.EmployeeId, SeparationType.Judicial, …)` ([InboundEventConsumers.cs:125-137](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)).
  - المعالج لا يفحص المعرّف: ينشئ `SeparationRecord` و`ServiceTransaction` من نوع `Separation` برقم مرجعي وقائمة تحقق ([InboundEventCommands.cs:360-396](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)). التكرار لا يُمنع إلا في التاريخ نفسه (`:366-372`).
  - `Close` لا يفحص الحالة على الخادم، فإعادة الإرسال تنشر مرة أخرى. والحارس في الواجهة فقط (`@if (Model.Status != CaseStatus.Closed)`، [LegalCases/Details.cshtml:53](../src/Services/Compliance/Compliance.Web/Views/LegalCases/Details.cshtml)). وفي المقابل `UpdateStatus` (`:96-106`) و`Edit` (`:72`) يغلقان القضية أو يعيدان فتحها دون أي حدث.
  - لا اختبار سلوكي لـ `ILegalJudgmentIssued`. الاختبار الوحيد الذي يذكره يفحص رقم نسخة المخطط فقط ([Phase4Tests.cs:264-268](../src/Tests/HRManagement.UnitTests/Phase4Tests.cs))، ولا يمارس `Close` ولا المستهلك.
- **الأثر:** كل قضية تُغلق، ولو كانت دعوى على الجهة لا تخص أي موظف، تضيف إلى سجل الانفصال معاملة «قضائية» لموظف `00000000-…`. هذا يلوّث سجل SEP والمؤشرات والترقيم المرجعي. وفي المقابل يستحيل تنفيذ حكم قضائي حقيقي يقتضي إنهاء خدمة موظف بعينه. *(صُحّح بعد التحقق المستقل: يوجد اختبار عقد لرقم نسخة الحدث في Phase4Tests، لكن لا اختبار للمسار؛ الأثر كما هو)*
- **الإصلاح المقترح:**
  - ربط القضية بموظف أو أكثر بأدوار (مدعٍ، مدعى عليه)، ونتيجة حكم صريحة (`JudgmentRequiresSeparation`، `JudgmentRequiresReinstatement`، `NoEmploymentEffect`…) بتاريخ نفاذ ومرجع.
  - لا يُنشر `ILegalJudgmentIssued` إلا لنتيجة لها أثر وظيفي، ولموظف صالح. ويرفض المستهلك `Guid.Empty`.
  - حارس حالة على الخادم يجعل الإغلاق idempotent.
  - سكربت إصلاح يلغي سجلات الانفصال والمعاملات القائمة لـ `Guid.Empty` (إلغاءً ناعماً بسبب).
- **الخطة:** [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (جزئياً، احتواء: لا نشر للحكم عند الإغلاق، والمستهلك يرفض `Guid.Empty`، وإلغاء السجلات الزائفة). وربط القضية بموظف ونتيجة: الموجة 2 (ER-F)
- **التحقق المطلوب:** موجب: حكم «يقتضي إنهاء الخدمة» لموظف محدد ← سجل `Judicial` واحد له. سالب: إغلاق قضية بلا أثر وظيفي ← لا حدث. إعادة POST على `Close` ← لا حدث ثانٍ. حدث بـ `Guid.Empty` ← يرفضه المستهلك ويُسجَّل.

#### G-ER-04 — سلّم العقوبات القانوني غير ممثَّل، والأثر المالي لا يحدث أبداً
- **الدليل:**
  - [ActionType.cs:5-15](../src/Services/Compliance/Compliance.Domain/Enums/ActionType.cs): أربعة أنواع فقط (`Warning`، `Suspension`، `Dismissal`، `Other`). لا مدة ولا نسبة حسم ولا مبلغ ولا تاريخ نفاذ أو انتهاء ولا مرجع قانوني في [DisciplinaryAction.cs:8-36](../src/Services/Compliance/Compliance.Domain/Entities/DisciplinaryAction.cs).
  - المنتج يرسل `PenaltyDays = 0` دائماً ([ComplianceExtendedControllers.cs:218](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)). والمستهلك لا يخصم إلا إذا `request.PenaltyDays > 0` ([InboundEventHandlers.cs:586-587](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs))، فيبقى أثر `Suspension` صفراً دائماً. ما يُكتب فعلاً هو `PayrollAdjustment` بمبلغ `0` (`:616-621`).
  - قيمة مثبتة في الشيفرة: قاسم 22 يوماً للأجر اليومي إذا لم يكن الأساس `Calendar30` (`:598`)، بينما يستخدم محرك الغياب `WorkingDaysInPeriod`.
  - السجل القانوني يشير إلى «Article 137 — Disciplinary actions» فقط ([legislation-register.json:28](syr/legislation-register.json))، والنص المصدّق غير متاح (`certifiedTextHash: PLACEHOLDER…`، و`SYD-001` بحالة OPEN-NO-GO في [14-SYRIA-GO-LIVE-DECISIONS.md:9](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md)). ولا يحمل أي حقل في التأديب `LegalSourceId`، خلافاً لبروتوكول [legal-source-binding.md](syr/legal-source-binding.md) («dismiss» من القرارات المطلوب تتبعها).
  - الاختبارات تثبت مساراً لا يحدث: `PenaltyDays = 2` في [DisciplinaryActionConsumerTests.cs:58-66](../src/Tests/HRManagement.UnitTests/DisciplinaryActionConsumerTests.cs)، و`PenaltyDays = 3` في [MassTransitConsumerTests.cs:299-310](../src/Tests/HRManagement.IntegrationTests/MassTransitConsumerTests.cs).
- **الأثر:** لا تستطيع جهة حقيقية تسجيل أي عقوبة من سلّمها القانوني كما هي (مثل الحسم من الأجر بنسبة ومدة، أو حجب الترفيع، أو النقل التأديبي — **يحتاج تحقق من النص المصدّق**). والعقوبات ذات الأثر المالي لا تخصم شيئاً. والتقرير المالي يُظهر «إجراء تأديبي» بمبلغ صفر.
- **الإصلاح المقترح:**
  - كتالوج عقوبات لكل جهة/نظام خدمة (`DisciplinaryPenaltyType`): الفئة، و`LegalSourceId` والمادة، ومعاملات الأثر (نسبة أو أيام حسم وسقفها، مدة حجب الترفيع أو العلاوة، مدة المحو)، وسلطة الإيقاع، وأثر في الرواتب والترفيع والانفصال. يُتحقق منه كما في `RecruitmentLegalSourceValidator`.
  - لا قيم افتراضية في Production/Staging، وتُعبأ القيم من D-ER-1 (على نهج `Recruitment:Competition:Defaults` في PLAN-31).
  - حدث `IDisciplinaryActionTaken` v2 يحمل معاملات الأثر المحسوبة (`DeductionPercent`/`Days`، `EffectiveFrom/To`، `DecisionRef`، `PenaltyCode`) وينشأ عند التنفيذ فقط، مع عدم التكرار على `ActionId` في المستهلك.
- **التحقق المطلوب:** موجب: عقوبة «حسم» من الكتالوج ← `DeductionRecord` واحد بالمبلغ المحسوب من المعاملات، حتى لو أعيد نشر الحدث برسالة جديدة. سالب: عقوبة بلا ربط قانوني على ملف شبيه بالإنتاج ← رفض. معامل يتجاوز السقف ← رفض.

### 4.2 ثغرات السلامة والحوكمة (P1)

#### G-ER-05 — جدار السرية وحماية المبلِّغ لا يعملان
- **الدليل:**
  - [InvestigationChecklistDefaults.cs:44-52](../src/Services/Compliance/Compliance.Domain/Services/InvestigationChecklistDefaults.cs): `CanSeeReporter` يعيد `isInvestigatorRole && complaint.AssignedTo.HasValue` (`:51`). و`IsInvestigator()` هو `Admin || Compliance` ([ComplaintsController.cs:23-24](../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs))، وهو كل من يدخل الوحدة. فبعد إسناد الشكوى لأي شخص، يرى **كل** مستخدم `compliance` هوية المبلِّغ المجهول.
  - التصدير يتجاهل المجهولية تماماً: `FiledBy` في CSV ([ExportController.cs:37-39](../src/Services/Compliance/Compliance.Web/Controllers/ExportController.cs))، وفي Excel (`:58-63`)، وفي مهمة التصدير غير المتزامنة ([ComplianceExportJobService.cs:195, 206](../src/Services/Compliance/Compliance.Infrastructure/ExportJobs/ComplianceExportJobService.cs)).
  - «المجهولية» شكلية: `FiledBy` إلزامي ويُخزَّن نصاً ([Complaint.cs:11-14](../src/Services/Compliance/Compliance.Domain/Entities/Complaint.cs)). و`ReporterProtectedId` يُضبط على **مُدخِل البيانات** لا المبلِّغ، ويقبل قيمة يرسلها العميل (`if (model.IsAnonymous && !model.ReporterProtectedId.HasValue)`، [ComplaintsController.cs:46-47](../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs)).
  - نص القرار المحجوب في التدقيق (`[AuditRedact] Resolution`) يُنشر في `IComplaintResolved`، ثم:
    - يُخزَّن في Dev.Analytics (`m.Subject, m.Resolution`، [ComplaintResolvedConsumer.cs:35-44](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/ComplaintResolvedConsumer.cs))، بينما مصفوفة الاحتفاظ تنص على `"backupAnalyticsPolicy": "prohibited-in-analytics"` للشكوى والتحقيق ([records-retention-matrix.json:126-138, 141-152](syr/records-retention-matrix.json)).
    - يدخل في **عنوان** بند الأرشيف (`$"ملف شكوى — {m.Subject}: {m.Resolution} …"`، [ComplaintResolvedArchiveConsumer.cs:17](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ComplaintResolvedArchiveConsumer.cs))، ويراه دور `records-archive`.
  - التوصية الطبية (Retirement/Compensation) تنشئ ملاحظة رقابية وصفها يحوي معرّف الموظف ونوع التوصية ([MedicalRecommendationIssuedConsumer.cs:85](../src/Services/Compliance/Compliance.Infrastructure/Consumers/MedicalRecommendationIssuedConsumer.cs))، فتصل معلومة طبية إلى كل مستخدمي الامتثال.
  - قراءة تفاصيل الشكوى أو الإجراء التأديبي غير مدققة. `SensitiveDataAudit` يُستدعى عند الإنشاء فقط (`ComplaintsController.cs:55`).
  - ادعاء HCM-006 «investigator sees only assigned cases» و`[x] Case access separated` ([HCM_006_EMPLOYEE_RELATIONS.md:52, 61](HCM_006_EMPLOYEE_RELATIONS.md)) غير منفذ: القوائم تعرض كل الحالات (`ComplaintsController.cs:26-33`، `ComplianceExtendedControllers.cs:140-147`).
- **الأثر:** تنكشف هوية المبلِّغ لكل فريق الامتثال ولكل من يستلم ملف التصدير. هذا يُفشل حماية المبلغين التي يدّعيها FUN-004 ويخالف القانون 12/2024 (حقوق الشكوى). ويصل محتوى الشكاوى إلى التحليلات والأرشيف بنص حر. *(صُحّح بعد التحقق المستقل: أرقام أسطر `ExportController` فقط؛ الأثر كما هو)*
- **الإصلاح المقترح:**
  - قاعدة نطاق على مستوى الحالة: المحقق المسند والمقرِّر والمشرف المعيَّن فقط (الآلية → IAM، والقاعدة هنا).
  - هوية المبلِّغ في حقل محمي منفصل يُملأ من قناة الموظف (G-ER-11) لا من مُدخِل البيانات، و`FiledBy` اختياري للمجهول.
  - التصدير يحجب المبلِّغ والوصف دائماً، ويُتاح الاسم بإذن خاص مدقق.
  - `IComplaintResolved` بلا نص حر (رمز نتيجة فقط). عنوان الأرشيف مرجع لا محتوى.
  - تدقيق القراءة لتفاصيل الحالات. والملاحظة الناتجة عن توصية طبية تحمل مرجعاً دون نوع التوصية.
- **التحقق المطلوب:** موجب: المحقق المسند يرى المبلِّغ. سالب: مستخدم `compliance` آخر ← 403 على التفاصيل ولا يرى المبلِّغ. ملف التصدير لا يحوي اسم المبلِّغ المجهول. صف Dev.Analytics وعنوان الأرشيف بلا `Subject/Resolution`. وكل قراءة لتفاصيل الحالة تنتج سطر تدقيق.

#### G-ER-06 — الاستئناف والتعديل بلا ضوابط وبلا أثر
- **الدليل:**
  - [ComplianceExtendedControllers.cs:281-294](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs): `Appeal` يقدّمه موظف `compliance` نيابة عن الموظف، بلا نص ولا مهلة ولا تاريخ.
  - `DecideAppeal` (`:296-310`) يلحق نصاً حراً بالملاحظات (`entity.Notes = … $"\n[قرار الاستئناف]: {decision}"`، `:306`). لا نتيجة مهيكلة (تثبيت/تعديل/إلغاء)، ولا حدث، فيبقى الخصم أو تعطيل ملف الأجر قائماً حتى لو أُلغيت العقوبة. ولا فصل بين مُصدِر القرار ومن يبت في استئنافه.
  - `Edit` في GET يمنع التعديل بعد الاستئناف (`:257-258`)، أما POST (`:264-279`) فبلا أي حارس: يغيّر `EmployeeId` و`ActionType` و`IssuedAt` بعد نشر الحدث، دون تعويض ولا إعادة نشر. فيبقى الأثر المالي على الموظف الأول.
  - في الشكوى، `Appeal` يعيد الحالة إلى `Investigating` ويمسح القرار (`ComplaintsController.cs:226-247`). وبما أن `IComplaintResolved` نُشر سابقاً، فقد يُؤرشف القرار مرتين بعنوانين مختلفين — **يحتاج تحقق**.
  - FR-COMP-02 يطلب «التحقيق وإصدار القرار والاستئناف … كل انتقال حالة مسجل» ([THESIS_REQUIREMENTS_CATALOG_CURRENT.md:63](THESIS_REQUIREMENTS_CATALOG_CURRENT.md)).
- **الأثر:** الموظف بلا طريق فعلي للطعن. وكسب الاستئناف لا يعيد له حقه المالي. وتعديل قرار منفَّذ يفصل السجل عن الأثر بلا أثر تدقيقي مفهوم.
- **الإصلاح المقترح:**
  - كيان `DisciplinaryAppeal` له مقدّم (الموظف عبر ESS أو وكيله بإثبات)، وأسباب، ومهلة من تاريخ التبليغ (D-ER-1)، وجهة بت مختلفة عن المقرِّر، ونتيجة مهيكلة.
  - نتيجة الإلغاء أو التعديل تنشر حدث تعويض (`IDisciplinaryActionRevised` أو `Revoked`). يستهلكه Payroll بإلغاء الخصم غير المطبق أو بتسوية موجبة، وتستهلكه الخدمات الأخرى.
  - منع التعديل بعد `Decided` على الخادم. التصحيح يتم بقرار تعديل جديد مرتبط بالأصل، لا بتحرير الحقول.
- **الخطة:** [PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) (جزئياً: التعديل على الخادم في `Draft`/`Rejected` فقط، والاستئناف لإجراء معتمد فقط). وكيان الاستئناف وحدث التعويض: الموجة 2 (ER)
- **التحقق المطلوب:** موجب: استئناف ضمن المهلة ← قرار «إلغاء» من جهة مختلفة ← إلغاء الخصم غير المطبق، أو تسوية بقيمته إن طُبق. سالب: استئناف بعد المهلة ← رفض. البت من المقرِّر نفسه ← رفض. POST `Edit` على قرار `Decided` ← رفض دون تغيير.

#### G-ER-07 — التحقيق في الشكوى: الحالات لا تفرض الخطوات، ولا فصل بين المحقق والمقرِّر، ولا سماع أقوال
- **الدليل:**
  - [ComplaintsController.cs:137-152](../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs): `StartInvestigation` يبدأ من `Open` دون محقق مسند.
  - `Resolve` (`:154-183`) يحلّ دون التحقق من إنجاز الخطوات الإلزامية في القائمة. `EvidenceCollected` مُعلَّمة `IsRequired=true` و`SlaHours=168` ([InvestigationChecklistDefaults.cs:8-15](../src/Services/Compliance/Compliance.Domain/Services/InvestigationChecklistDefaults.cs))، لكن `IsRequired` لا يقرؤه أي سطر. و`Close` (`:185-202`) كذلك.
  - `Assign` يقبل أي GUID نصي (`:84-86`، والحقل نصي في [Complaints/Details.cshtml:42-43](../src/Services/Compliance/Compliance.Web/Views/Complaints/Details.cshtml)). ويمكن للمستخدم إسناد الشكوى لنفسه. وفحص `CanAssignInvestigator` يقارن بـ `ReporterProtectedId`، أي بمُدخِل البيانات (G-ER-05). أما المشكو منه فحقل `Against` نصي حر بلا أي فحص تعارض مصالح.
  - لا كيان تحقيق ولا استدعاء ولا محضر أقوال ولا مهلة دفاع ولا تبليغ. لا استخدام لأي خدمة إشعارات في Compliance.
  - لا ربط بين القرار التأديبي ونتيجة التحقيق: `ComplaintId` اختياري، ويمكن ربط إجراء بشكوى مفتوحة أو ملغاة (قائمة `Edit` تشمل الملغاة، `ComplianceExtendedControllers.cs:260`)، ولا يُشترط أن يكون الموظف المعاقَب هو المشكو منه.
  - سجل المتابعة بلا فاعل: `AddFollowUp` و`Appeal` و`Refer` و`AddRecommendation` لا تضبط `PerformedBy` (`:238-244`، `:259-265`، `:284-290`، `:311-317`). و`Refer` و`AddRecommendation` لا يفحصان الإلغاء أو الإغلاق (`:270-321`).
- **الأثر:** قرار الشكوى، ومن بعده العقوبة، يُتخذ دون إثبات أن المحقق جمع الأدلة واستمع للطرفين. وهذا شرط عام لصحة القرار الإداري التأديبي (النص المحدد من D-ER-1). كما يمكن أن يحقق في الشكوى زميل المشكو منه أو المستخدم الذي أدخلها.
- **الإصلاح المقترح:**
  - كيان `Investigation` مرتبط بالشكوى أو بالواقعة: محقق معيَّن بقرار مع إقرار تعارض مصالح (≠ المبلِّغ، ≠ المشكو منه، ≠ رئيسهما المباشر — **قاعدة يحسمها D-ER-5**)، وجلسات استماع، ومحاضر أقوال مرفقة (FILE-002)، ومهلة دفاع، ونتيجة وتوصية.
  - ربط المشكو منه بموظف (`SubjectEmployeeId`) بدل النص.
  - انتقالات الحالة تشترط الخطوات الإلزامية. والقرار التأديبي يشترط تحقيقاً مغلقاً على الموظف نفسه.
  - `PerformedBy` من الخادم في كل متابعة.
- **التحقق المطلوب:** موجب: تحقيق بمحقق مستقل ← جلسة بمحضر ← توصية ← قرار. سالب: `Resolve` قبل `EvidenceCollected` ← رفض. إسناد المشكو منه أو المبلِّغ محققاً ← رفض. قرار على موظف غير المشكو منه ← رفض.

#### G-ER-08 — ربط الكيانات مباشرة في كل متحكمات Compliance يتجاوز آلات الحالات، واختبار المعمارية لا يراها
- **الدليل:**
  - كل إجراءات `Create`/`Edit` تربط كيان EF مباشرة: `Create(Complaint model)` ([ComplaintsController.cs:39](../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs))، و`Create/Edit(LegalCase …)`، و`Create/Edit(DisciplinaryAction …)`، و`Create/Edit(AuditFinding …)`، و`Create/Edit(LegalReferral …)`، و`Create/Edit(ComplianceRecommendation …)` ([ComplianceExtendedControllers.cs:30, 65, 158, 266, 322, 349, 422, 448, 476, 508](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)).
  - أمثلة على أثر ذلك:
    - `Complaint.Create` يقبل `AssignedTo` و`ReporterProtectedId` و`Resolution` و`IsCancelled` من العميل، متجاوزاً فحص `CanAssignInvestigator`.
    - `LegalCase.Edit` يضبط `Status` بحرية (`:72`).
    - `AuditFinding.Edit` يضبط `Status` دون `ResolvedAt` (`:358`).
    - `ComplianceRecommendation.Edit` يضبط حالة الاعتماد (`:517`).
    - نموذج التأديب يعرض قائمة `AppealStatus` للمستخدم ([DisciplinaryActions/Create.cshtml:28-31](../src/Services/Compliance/Compliance.Web/Views/DisciplinaryActions/Create.cshtml)).
  - [MassAssignmentArchitectureTests.cs:29-31, 42-58](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs): قائمة الاستثناءات فارغة ويُفترض أن الاختبار يمنع هذا النمط، لكنه يتخطى بصمت أي تجميع غير مُشار إليه (`catch (… FileNotFoundException …) { // the scan simply skips it }`). ومشروع الاختبار يشير إلى `Compliance.Domain` و`Compliance.Infrastructure` فقط ([HRManagement.UnitTests.csproj:46-47](../src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj))، ولا يشير أي `.csproj` في المستودع إلى `Compliance.Web`.
- **الأثر:** حرّاس الحالات القليلة الموجودة يمكن تجاوزها بطلب مصاغ، بما فيها فصل الواجبات في الإسناد. والاختبار الذي يُستشهد به على إغلاق هذا الصنف أخضر دون أن يفحص الخدمة.
- **الإصلاح المقترح:** نماذج إدخال (view models) لكل إجراء بالحقول المسموحة فقط. الانتقالات عبر إجراءات مخصصة بحراس، لا عبر `Status` في `Edit`. إضافة `Compliance.Web` إلى نطاق الاختبار، وجعل غياب تجميع مُدرج في `probes` فشلاً لا تخطياً (→ QA للآلية).
- **التحقق المطلوب:** موجب: الاختبار المعماري يرى `Compliance.Web` وينجح بقائمة فارغة. سالب: POST `Complaints/Create` بحقل `AssignedTo` ← يُتجاهل الحقل. POST `LegalCases/Edit` بـ `Status=Closed` ← لا تتغير الحالة.

### 4.3 قدرات ناقصة (P2)

#### G-ER-09 — المواعيد والتقادم والتصعيد غير موجودة
- **الدليل:**
  - `SlaHours` و`IsRequired` يُخزَّنان ([InvestigationChecklistDefaults.cs:8-15](../src/Services/Compliance/Compliance.Domain/Services/InvestigationChecklistDefaults.cs)) ولا يقرؤهما أي سطر خارج الإنشاء.
  - لا `BackgroundService` ولا `AddHostedService` في خدمة Compliance كلها.
  - القيم (48/168/72/24 ساعة) مثبتة في الشيفرة بلا مرجع قانوني. وكذلك مهلة 30 يوماً للتوصية الناتجة عن توصية طبية (`MedicalRecommendationIssuedConsumer.cs:107`).
  - لا تقادم للملاحقة التأديبية، ولا مهلة استئناف، ولا محو للعقوبة بعد مدة.
  - ادعاء «Deadlines trigger alerts and automatic escalation» و`[x]` ([HCM_006_EMPLOYEE_RELATIONS.md:55, 64](HCM_006_EMPLOYEE_RELATIONS.md)) غير منفذ.
- **الأثر:** تتقادم الحالات دون علم أحد، أو تُتخذ قرارات بعد انقضاء الحق في الملاحقة، فتبطل. ولا يعرف المدير أي حالة متأخرة.
- **الإصلاح المقترح:** مهمة خلفية في Compliance تحسب المواعيد من معاملات مرتبطة بالمرجع القانوني (D-ER-1)، وتصعّد إلى مالك الخطوة ثم المشرف. وحارس تقادم يمنع فتح الملاحقة أو القرار بعد المهلة إلا باستثناء موثق. ومحو العقوبة آلياً بعد مدتها.
- **التحقق المطلوب:** موجب: تجاوز SLA خطوة ← علم وتصعيد مسجّل. سالب: قرار بعد مهلة التقادم ← رفض. عقوبة انقضت مدة محوها ← لا تظهر في فحص الأهلية (G-ER-10).

#### G-ER-10 — آثار التنفيذ خارج الرواتب مفقودة: حجب الترفيع، ملف الموظف، حالة الإيقاف
- **الدليل:**
  - اعتماد الترفيع يفحص الحالة وفصل الواجبات ونظام الخدمة فقط ([MovementsExtendedControllers.cs:952-987](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). ولا ذكر لأي عقوبة في HR.Movements.
  - لا مستهلك لـ `IDisciplinaryActionTaken` في Records.Archive، ولا في HR.Core. و`EmploymentStatus.Suspended` له انتقالات معرّفة لكن لا منتج له.
  - FR-ARC-03 يطلب أرشفة «الفصل والشكاوى» ([THESIS_REQUIREMENTS_CATALOG_CURRENT.md:61](THESIS_REQUIREMENTS_CATALOG_CURRENT.md)). ومصفوفة الاحتفاظ تعرّف `investigation-case` بـ 20 سنة ([records-retention-matrix.json:126-138](syr/records-retention-matrix.json)) دون أي مُنتِج.
  - مستخدمو `hr-core` و`hr-movements` لا يصلون إلى وحدة Compliance، فلا يرون أي عقوبة عند اتخاذ قرارات الترفيع.
- **الأثر:** يُرفَّع موظف معاقَب بحجب الترفيع. وملف الموظف لا يحوي تاريخه التأديبي. والموقوف عن العمل يظهر «نشطاً» في كل الخدمات.
- **الإصلاح المقترح:** نموذج قراءة «عقوبات سارية» لكل موظف (حدث v2 من Compliance ← إسقاط في HR.Core أو استعلام S2S بنطاق `compliance.sanctions.read`)، يقرؤه فحص الأهلية في الترفيع والعلاوة. وقيد أرشيفي لكل قرار نهائي مرتبط بالموظف، بفئة `investigation-case` ومدة احتفاظ من المصفوفة. وانتقال الحالة إلى `Suspended` والعودة منه، حسب نوع العقوبة.
- **التحقق المطلوب:** موجب: انقضاء مدة الحجب ← يُقبل الترفيع. سالب: اعتماد ترفيع لموظف بعقوبة حجب سارية ← رفض برسالة. قرار نهائي ← قيد أرشيفي واحد بلا نص حر.

#### G-ER-11 — لا قناة للموظف، والتظلم الجماعي بلا واجهة
- **الدليل:**
  - لا شكوى ولا تظلم ولا استئناف في ESS (لا ذكر لـ complaint/grievance في HR.Core أو HR.Contracts). وكل الإدخال من دور `compliance`.
  - لا تبليغ للموظف بالقرار ولا إقرار استلام.
  - `CollectiveGrievance` جدول ومهاجرة ([CollectiveGrievance.cs:8-40](../src/Services/Compliance/Compliance.Domain/Entities/CollectiveGrievance.cs)، [ComplianceDbContext.cs:41-47](../src/Services/Compliance/Compliance.Infrastructure/Data/ComplianceDbContext.cs)) بلا متحكم ولا واجهة. و`UnionEngagementGate` قاعدة نطاق بلا مستدعٍ في التشغيل.
  - المسار الحكومي في البوابة يعد بـ «تسجيل الشكوى والتحقيق وإصدار القرار» ([GatewayJourneyCatalog.cs:220-228](../src/Gateway/HRManagement.Gateway/Services/GatewayJourneyCatalog.cs)).
- **الأثر:** الموظف لا يستطيع التظلم أو الدفاع أو الطعن إلا ورقياً. والإبلاغ المجهول يمر حتماً عبر شخص يعرف هويته.
- **الإصلاح المقترح:** شاشات ESS لتقديم الشكوى (مع خيار المجهولية وهوية محمية)، وتسلّم الاستدعاء والقرار مع إقرار، وتقديم الدفاع والاستئناف ضمن المهلة. وواجهة للتظلم الجماعي بمالك ومهلة، لا تُحوَّل إلى المسار الفردي (D-ER-6).
- **التحقق المطلوب:** موجب: الموظف يقدّم شكوى مجهولة من ESS ← لا يراه إلا المحقق المسند. وتبليغ القرار ← إقرار مسجل. سالب: استئناف بعد المهلة ← رفض. تظلم جماعي بـ `RoutedToIndividualPath=true` ← رفض.

#### G-ER-12 — القضايا والإحالات القانونية سجلات نصية بلا ربط ولا متابعة
- **الدليل:**
  - `LegalCase`: موضوع وأطراف وتفاصيل محكمة نصية، و`AssignedTo` GUID حر. لا موظف ولا جلسات ولا مواعيد ولا درجة تقاضي ولا حكم ولا تنفيذ ([LegalCase.cs:8-29](../src/Services/Compliance/Compliance.Domain/Entities/LegalCase.cs)).
  - `LegalReferral`: بلا حالة ولا نتيجة ولا ربط بالملاحظة الرقابية المصدر. `CreateReferral` ينسخ الوصف نصاً ويضبط `OriginatingUnit = null`، وتكرار الإرسال يكرر الإحالة ([ComplianceExtendedControllers.cs:391-410](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)).
  - لا فهرس فريد على `LegalCases.CaseNumber` في المهاجرات (الترقيم من COD-001 وحده).
- **الأثر:** لا تتابع الشؤون القانونية مواعيد الجلسات ولا تنفيذ الأحكام (إعادة إلى العمل، تعويض، انفصال). والإحالة تضيع بعد تسجيلها.
- **الإصلاح المقترح:** ربط القضية بالموظفين وبالقرار الإداري أو التأديبي المطعون فيه. جلسات بمواعيد وتنبيهات. حكم مهيكل بأثر وظيفي يغذي G-ER-03. إحالة بمصدر (شكوى/ملاحظة/قرار) وحالة ونتيجة، وفهرس فريد يمنع التكرار.
- **التحقق المطلوب:** موجب: حكم بإعادة موظف إلى العمل ← معاملة في العقود ← إلغاء العقوبة المرتبطة. سالب: إحالة ثانية للمصدر نفسه وهي مفتوحة ← رفض.

#### G-ER-13 — الرقابة الداخلية: الملاحظات والتوصيات والزيارات الميدانية بلا دورة حياة
- **الدليل:**
  - `Mitigate` و`CloseItem` بلا حارس حالة ولا دليل ولا فصل واجبات ([ComplianceExtendedControllers.cs:364-389](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)). ويمكن العودة من `Closed` إلى `Mitigated`.
  - `ComplianceRecommendation`: لا متابعة لموعد الاستحقاق ولا إشعار للوحدة المسؤولة.
  - `FieldInspectionVisit` في HR.Core: إنشاء فقط بحالة `"planned"` لا تتغير، بلا وحدة تنظيمية ولا ملاحظات ولا ربط بـ `AuditFinding` ([OrgBranchControllers.cs:289-334](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)، [FieldInspectionVisit.cs:6-27](../src/Services/HRCore/HR.Core.Domain/Entities/FieldInspectionVisit.cs)). و`FieldInspectionVisits.Create` في قائمة POST المجمّدة بلا سياسة ([hrcore-post-policy-allowlist.txt:32](../tests/architecture/hrcore-post-policy-allowlist.txt)).
  - المتطلب يصف فرع الرقابة والتفتيش الميداني صراحة ([Requariments.md:173-183, 337](Requariments.md)).
- **الأثر:** تُغلق الملاحظة الرقابية بلا دليل وعلى يد من سجّلها. ونتائج التفتيش الميداني لا تتحول إلى ملاحظات قابلة للمتابعة.
- **الإصلاح المقترح:** دورة `Open → ActionPlanned → Mitigated → Verified → Closed`: الإغلاق بدليل ومن مستخدم غير المُسجِّل، والتوصيات بمالك وموعد وتصعيد. ودورة حياة لزيارة التفتيش (مخطط/منفذ/مغلق) بوحدة تنظيمية، تنشئ ملاحظات في Compliance (D-ER-7 للنطاق).
- **التحقق المطلوب:** موجب: زيارة منفذة ← ملاحظة ← توصية ← إغلاق بدليل من مدقق آخر. سالب: إغلاق بلا دليل أو من المُسجِّل ← رفض.

### 4.4 تحسينات (P3)

#### G-ER-14 — اللوحة والتقارير
- **الدليل:** [DashboardController.cs:15](../src/Services/Compliance/Compliance.Web/Controllers/DashboardController.cs) يعد «الشكاوى المفتوحة» دون استبعاد الملغاة (`c.Status != Closed && c.Status != Resolved`). وبقية اللوحة 3 عدّادات خام. لا مؤشرات لعمر الحالات أو تجاوز المواعيد أو العقوبات حسب النوع أو الوحدة، ولا حجب للخلايا الصغيرة.
- **الأثر:** أرقام مضللة للإدارة، ولا رؤية لأداء علاقات العمل.
- **الإصلاح المقترح:** مؤشرات بنطاق الوحدة، بحجب الخلايا الصغيرة على نهج PLAN-32، دون نص حر، واستبعاد الملغى.
- **التحقق المطلوب:** شكوى ملغاة لا تُعد. وخلية بأقل من الحد تظهر محجوبة.

### 4.5 فجوات التحقق والتوثيق

#### G-ER-15 — ادعاءات «Done» و«ReadyForSign-off» بلا دليل، واختبارات على نموذج غير مستخدم
- **الدليل:**
  - بطاقة HCM-006 بحالة `Done` ([09-NATIONAL-FUNCTIONAL-CAPABILITIES.md:163-174](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md))، وأدلتها:
    - [EmployeeRelationCase.cs](../src/Shared/HRManagement.SharedKernel/Cases/EmployeeRelationCase.cs): لا مرجع له في أي خدمة.
    - [Hcm006EmployeeRelationsTests.cs:23-101](../src/Tests/HRManagement.UnitTests/Hcm006EmployeeRelationsTests.cs): انعكاس ووجود ملفات.
    - [hcm006-validate.ps1:20-52](../scripts/hcm006-validate.ps1): يبحث عن أسماء خصائص في النص.
  - FUN-004 بحالة `Done` ([04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md:193](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md)، [FUN_004_VERIFICATION.md:13](FUN_004_VERIFICATION.md))، وحزمة UAT بحالة `ReadyForSign-off` ([FUN_004_UAT_PACK.md:14](FUN_004_UAT_PACK.md)). وU5 وU6 تدّعيان سلوكاً غير موجود (G-ER-05، G-ER-01).
  - E2E الوحيد للامتثال يتحقق أن الصفحة تطلب تسجيل الدخول ([Fun004HealthTrainingComplianceE2ETests.cs:16-26](../src/Tests/HRManagement.E2E/Fun004HealthTrainingComplianceE2ETests.cs)).
  - لا يوجد في `acceptance-evidence/` أي مجلد للشكوى أو التأديب أو القضايا، ولا خطة قبول (PLAN-00..33) تغطيها.
  - اختبارات الرواتب تثبت `PenaltyDays>0` الذي لا يُرسل (G-ER-04). ولا اختبار لـ `ILegalJudgmentIssued`. ولا اختبار لأي متحكم في Compliance (لا مشروع يشير إلى `Compliance.Web`).
  - [EVENT_PUBLISHER_INVENTORY.md:55-57](EVENT_PUBLISHER_INVENTORY.md) يصنّف ناشري Compliance الثلاثة «transactional»، والنمط `Save → Publish → Save` معاملتان منفصلتان (→ OPS).
- **الأثر:** القرار بالإطلاق يُبنى على وثائق تقول إن المسار مكتمل ومحمي، والشيفرة لا تحميه.
- **الإصلاح المقترح:**
  - سيناريوهات قبول `AC-ER-*` موسومة `[RequiredAcceptance]` على الملف الشبيه بالإنتاج، ولكل منها `result.json`.
  - حذف `SharedKernel.Cases.EmployeeRelationCase` واختباراته، أو جعله الكيان الفعلي. وتحويل `Hcm006*` إلى اختبارات سلوك على قواعد Compliance.
  - تصحيح HCM_006 وFUN-004 (ADR وExceptions وUAT وVerification) والبطاقتين 09 و04 لتطابق الشيفرة.
- **التحقق المطلوب:** كل سيناريو موجب وسالب في §6 له مجلد أدلة. ولا يوجد ادعاء «Done» دون دليل حي.

### 4.6 تقاطعات مع الدراسات الأخرى

- **→ WF:** قائمة السماح في `WorkflowDefinitionsHaveHandlersTests` تقبل سبباً نصياً لا يُتحقق منه. `DisciplinaryAction` مثال على سبب خاطئ ([WorkflowDefinitionsHaveHandlersTests.cs:21](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)). يُقترح فحص وجود مستهلك فعلي للنتيجة في الخدمة المسماة.
- **→ WF:** لا آلية عامة لبدء مسار في HR.Core من خدمة أخرى واستلام نتيجته بحدث. وهذا شرط لخيار «المحرك» في D-ER-2 ([WorkflowPackSeeder.cs:44-46](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).
- **→ POL:** القاعدة `R-DISC` مبذورة بلا مستدعٍ ([PolicyEngineDomainSeeds.cs:120-126](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)). و`PolicyDomainRegistry` يصف `Compliance.Disciplinary` بأنه `Shadow` ([PolicyTemplatesController.cs:235](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). و`AddHrPolicyEnforceClient` مسجّل بلا استخدام ([Program.cs:44](../src/Services/Compliance/Compliance.Web/Program.cs)).
- **→ IAM:** `ModuleCompliance` = `Admin | compliance` بلا سياسات دقيقة ([HrManagementAuthorizationExtensions.cs:50](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، و29 إجراء POST بلا `[Authorize(Policy=…)]`. ولا نطاق فرع/وحدة على قوائم الشكاوى والتأديب والقضايا وتفاصيلها وتصديرها ([ComplaintsController.cs:26-33](../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs)، [ExportController.cs:45-56](../src/Services/Compliance/Compliance.Web/Controllers/ExportController.cs)). الآلية لـ IAM، وقاعدة «من يرى الحالة» هنا (G-ER-05، D-ER-5).
- **→ QA:** [MassAssignmentArchitectureTests.cs:51-58](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs) يتخطى التجميعات غير المُشار إليها بصمت (`Compliance.Web`، وربما غيرها — **يحتاج تحقق**). و`OutboxPublishArchitectureTests` يجيز `ComplianceExtendedControllers.cs` و`ComplaintsController.cs` ([OutboxPublishArchitectureTests.cs:44-45](../src/Tests/HRManagement.UnitTests/OutboxPublishArchitectureTests.cs)).
- **→ OPS:** نمط `SaveChanges` ثم `Publish` ثم `SaveChanges` ليس ذرياً ([ComplianceExtendedControllers.cs:196, 207-220](../src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs)، [ComplaintsController.cs:170-180](../src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs))، بينما INT-001 يقول إنه مغلق. و`ComplaintResolvedArchiveConsumer` يُسقط الرسالة بصمت إذا فشل توليد الرقم ([ComplaintResolvedArchiveConsumer.cs:21-22](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ComplaintResolvedArchiveConsumer.cs)). وإيصال الرسالة في Payroll يُسجَّل بعد التزام المعالج ([PayrollEventConsumers.cs:310-318](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)).
- **→ PAY:** معالج التأديب في الرواتب ([InboundEventHandlers.cs:581-624](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)): قاسم 22 مثبت، ولا سقف للحسم، ولا فحص لعلامة `ActionId` قبل الإضافة، وتعطيل ملف الأجر عند الفصل (G-ER-02). يملك ER محتوى الحدث، ويملك PAY المعالجة.
- **→ SEP:** المعالج `ProcessSeparationRequestHandler` يقبل `Guid.Empty` ([InboundEventCommands.cs:360-396](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)). والانفصال من نوع `Disciplinary` يُنشأ يدوياً بلا مرجع قرار. وتسوية نهاية الخدمة مشروطة بملف أجر نشط ([InboundEventHandlers.cs:205-244](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- **→ MOV:** فحص الأهلية التأديبية في اعتماد الترفيع ([MovementsExtendedControllers.cs:952-987](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). يقدّم ER نموذج «العقوبات السارية»، ويستهلكه MOV.

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-ER-1 | سلّم العقوبات ومعاملاته: الأنواع، حدود الحسم ومدده، مدة حجب الترفيع والعلاوة، سلطة الإيقاع لكل فئة، مهل التقادم والدفاع والاستئناف، ومدة محو العقوبة | تُستمد من النص المصدّق للقانون 50/2004 وتعديلاته (`SYD-001`) وأنظمة الخدمة الخاصة. وتُدخل كتالوجاً مربوطاً بـ `LegalSourceId` والمادة، بلا قيم افتراضية في الإنتاج. وقيم مختبرية في Development فقط | ER-B، ER-C |
| D-ER-2 | آلية اعتماد القرار التأديبي | **سلسلة محلية في Compliance** بمصفوفة سلطة وفصل واجبات على الخادم، تبقي بيانات الحالة السرية خارج صندوق عمل HR.Core. ويُسحب `DISCIPLINARY-APPROVAL` و`R-DISC` كما سُحب `RECRUITMENT-HIRE`. البديل: محرك HR.Core بحدث نتيجة عابر للخدمات (يتطلب عمل WF) | ER-A، WF |
| D-ER-3 | متى يُنفَّذ الأثر | بعد أن يصبح القرار نهائياً (انقضاء مهلة الاستئناف أو البت فيه)، إلا «تنفيذاً معجّلاً» بقرار مسبَّب من السلطة إن أجازه النص | ER-A، ER-B |
| D-ER-4 | من يملك إنهاء الخدمة التأديبي والقضائي | Compliance يصدر القرار أو يسجل الحكم. HR.Contracts ينفذ الانفصال (`Disciplinary`/`Judicial`) بمرجعه. وPayroll لا يتصرف إلا عند `ISeparationCompleted` | ER-B، SEP، PAY |
| D-ER-5 | أدوار علاقات العمل ونطاقها | أدوار منفصلة: `er-intake`، `er-investigator`، `er-decision-authority` (بمستوى لكل فئة عقوبة)، `legal-officer`، `internal-auditor`. والوصول على مستوى الحالة (المسند فقط)، مع نطاق مركز/فرع | ER-A، ER-D، IAM |
| D-ER-6 | قناة الموظف والإبلاغ المجهول | ESS للشكوى والدفاع والاستئناف وإقرار التبليغ، مع خيار مجهولية بهوية محمية لا يراها إلا المحقق المسند. وتأجيل القناة الخارجية للمبلغين من غير الموظفين | ER-E |
| D-ER-7 | نطاق v1 للقضايا والرقابة الداخلية والتفتيش | v1: ربط القضية بالموظف والحكم ذي الأثر الوظيفي، ودورة الملاحظة الرقابية بدليل. وتأجيل إدارة جلسات المحاكم التفصيلية إن لم تطلبها الجهة. وتُنقل نتائج التفتيش إلى Compliance | ER-F |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 0 (يوم)     ER-A §0: احتواء الضرر (إيقاف Guid.Empty، منع Dismissal مؤقتاً، سكربتا جرد/إصلاح)
الموجة 1 (P0)      ER-A (القرار المحكوم)  ║  ER-D (السرية والربط المباشر) — متوازيتان
الموجة 2 (P0/P1)   ER-B (سلّم العقوبات والتنفيذ عبر الخدمات) — بعد D-ER-1/3/4
الموجة 3 (P1)      ER-C (التحقيق وحق الدفاع والمواعيد)
الموجة 4 (P2)      ER-E (قناة الموظف والاستئناف)  ║  ER-F (القضايا والرقابة والتقارير)
مستمر              ER-G (القبول الحي والتوثيق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|---------|---------|----------|-----------|-------------------|
| ER-A | قرار تأديبي محكوم: الحالات والاعتماد وفصل الواجبات | G-ER-01، G-ER-08 (التأديب)، G-ER-06 (التعديل وفصل الواجبات)، احتواء G-ER-02/03 | P0 | D-ER-2، D-ER-5 | 8–10 |
| ER-B | سلّم العقوبات وتنفيذ الآثار عبر الخدمات | G-ER-02، G-ER-03، G-ER-04، G-ER-06 (التعويض)، G-ER-10 | P0 | ER-A، D-ER-1، D-ER-3، D-ER-4 | 9–10 |
| ER-C | التحقيق وحق الدفاع والمواعيد | G-ER-07، G-ER-09 | P1 | ER-A | 8–9 |
| ER-D | السرية وحماية المبلِّغ وإغلاق الربط المباشر | G-ER-05، G-ER-08 (الباقي) | P1 | — (موازية لـ ER-A) | 5–6 |
| ER-E | قناة الموظف والاستئناف والتظلم الجماعي | G-ER-11، G-ER-06 (القناة) | P2 | ER-A، ER-C، D-ER-6 | 6–8 |
| ER-F | القضايا والإحالات والرقابة والتقارير | G-ER-12، G-ER-13، G-ER-14 | P2/P3 | ER-B، D-ER-7 | 7–9 |
| ER-G | التحقق والتوثيق | G-ER-15 | P1 ← مستمر | كل الخطط | 4–5 |
| **المجموع** | | | | | **≈ 47–57** |

**ER-A — قرار تأديبي محكوم.**
- §0 احتواء (يوم واحد):
  - `LegalCases.Close` لا ينشر بلا موظف وأثر وظيفي.
  - منع نوع `Dismissal` مؤقتاً.
  - سكربت يسرد ملفات الأجر المعطلة بعلامة `DisciplinaryAction-*`، وسكربت يلغي سجلات الانفصال لـ `Guid.Empty`.
- آلة حالات القرار على الخادم، وكيان تحقيق أدنى (محقق ومحضر سماع أو تنازل) شرطاً للقرار.
- مصفوفة سلطة الإيقاع وفصل الواجبات (المُنشئ ≠ المحقق ≠ المقرِّر)، ومنع `Edit` بعد `Decided`.
- نماذج إدخال لإجراءات التأديب، وسياسات صريحة لكل POST.
- سحب `DISCIPLINARY-APPROVAL` و`R-DISC` أو ربطهما (D-ER-2)، وحذف `DisciplinaryAction` من قائمة السماح.
- القبول:
  - AC-ER-Aa: إنشاء بلا أثر ولا حدث.
  - AC-ER-Ab: المحقق أو المُنشئ يقرر ← رفض. صاحب السلطة يقرر ← قبول.
  - AC-ER-Ac: قرار بلا تحقيق مغلق ← رفض.
  - AC-ER-Ad: حقول مرسلة بالربط المباشر تُتجاهل، و`Edit` بعد القرار ← رفض.
  - AC-ER-Ae: حارس المسارات بلا استثناء للتأديب.
  - AC-ER-Af: إغلاق قضية بلا موظف لا ينشئ انفصالاً.

**ER-B — سلّم العقوبات والتنفيذ.**
- كتالوج `DisciplinaryPenaltyType` مربوط قانونياً، بلا قيم افتراضية في الإنتاج.
- `IDisciplinaryActionTaken` v2 عند `Final` بمعاملات الأثر، وعدم التكرار على `ActionId`.
- Payroll: خصم من المعاملات بسقف، وإلغاء تعطيل ملف الأجر عند الفصل (→ PAY).
- قرار الفصل يتحول إلى طلب انفصال `Disciplinary` بمرجع القرار. والحكم القضائي المهيكل يتحول إلى `Judicial` لموظف محدد (→ SEP).
- حدث تعويض عند الإلغاء أو التعديل.
- نموذج «العقوبات السارية» لفحص الترفيع (→ MOV)، وقيد أرشيفي في ملف الموظف، وحالة `Suspended`.
- القبول:
  - AC-ER-Ba: خصم واحد صحيح رغم إعادة النشر.
  - AC-ER-Bb: فصل ← انفصال ← تسوية نهاية خدمة، وملف الأجر نشط قبلها.
  - AC-ER-Bc: إلغاء بالاستئناف ← تعويض.
  - AC-ER-Bd: ترفيع مع حجب سارٍ ← رفض.
  - AC-ER-Be: قيد أرشيفي بلا نص حر.
  - AC-ER-Bf: حكم لموظف محدد ← `Judicial` واحد.
  - AC-ER-Bg: عقوبة بلا ربط قانوني في الملف الشبيه بالإنتاج ← رفض.

**ER-C — التحقيق وحق الدفاع والمواعيد.**
- كيان `Investigation` كامل: تعيين بقرار وإقرار تعارض مصالح، واستدعاء وتبليغ، ومحاضر (FILE-002)، ومهلة دفاع، وتوصية.
- `SubjectEmployeeId` بدل `Against` النصي.
- انتقالات الشكوى تشترط الخطوات الإلزامية، و`PerformedBy` من الخادم.
- مهمة خلفية للمواعيد والتصعيد، وحارس تقادم، ومحو العقوبة، كلها من معاملات D-ER-1.
- القبول:
  - AC-ER-Ca: محقق متعارض ← رفض.
  - AC-ER-Cb: `Resolve` قبل الأدلة ← رفض.
  - AC-ER-Cc: قرار قبل انقضاء مهلة الدفاع دون تنازل ← رفض.
  - AC-ER-Cd: تجاوز SLA ← تصعيد مسجل، وقرار بعد التقادم ← رفض.
  - AC-ER-Ce: كل متابعة بفاعل.

**ER-D — السرية والربط المباشر.**
- وصول على مستوى الحالة (المسند والمقرِّر فقط) بالتنسيق مع IAM.
- هوية المبلِّغ في حقل محمي، و`FiledBy` اختياري للمجهول.
- تصدير محجوب، و`IComplaintResolved` بلا نص حر، وعنوان أرشيف بمرجع فقط.
- تدقيق القراءة، وتقليل البيانات في الملاحظة الناتجة عن توصية طبية.
- نماذج إدخال لكل متحكمات Compliance، وإدخال `Compliance.Web` في اختبار المعمارية.
- القبول:
  - AC-ER-Da: مستخدم `compliance` غير مسند ← 403، ولا يرى المبلِّغ.
  - AC-ER-Db: ملفات التصدير بلا هوية المبلِّغ.
  - AC-ER-Dc: Dev.Analytics والأرشيف بلا نص.
  - AC-ER-Dd: سطر تدقيق لكل قراءة.
  - AC-ER-De: `AssignedTo` المرسل بالربط المباشر يُتجاهل.
  - AC-ER-Df: اختبار المعمارية يغطي `Compliance.Web`.

**ER-E — قناة الموظف.**
- ESS: تقديم شكوى (مع المجهولية)، وتسلّم الاستدعاء والقرار مع إقرار، وتقديم الدفاع، والاستئناف ضمن المهلة.
- واجهة التظلم الجماعي بمالك ومهلة، وتفعيل `UnionEngagementGate` حيث يلزم.
- القبول:
  - AC-ER-Ea: شكوى مجهولة من ESS.
  - AC-ER-Eb: إقرار التبليغ وتقديم الدفاع.
  - AC-ER-Ec: استئناف ضمن المهلة يُقبل، وبعدها يُرفض.
  - AC-ER-Ed: تظلم جماعي لا يُحوَّل إلى المسار الفردي.

**ER-F — القضايا والرقابة والتقارير.**
- ربط القضية بالموظفين والقرار المطعون فيه، وجلسات بمواعيد، وحكم مهيكل.
- إحالة بمصدر وحالة ونتيجة، وفهرس يمنع التكرار.
- دورة الملاحظة الرقابية بدليل وفصل واجبات، وتوصيات بمتابعة.
- دورة حياة زيارة التفتيش وربطها بالملاحظات، ولوحة بمؤشرات محجوبة الخلايا الصغيرة.
- القبول:
  - AC-ER-Fa: حكم إعادة ← إلغاء العقوبة.
  - AC-ER-Fb: إحالة مكررة ← رفض.
  - AC-ER-Fc: إغلاق ملاحظة بلا دليل أو من المُسجِّل ← رفض.
  - AC-ER-Fd: زيارة ← ملاحظة.
  - AC-ER-Fe: اللوحة تستبعد الملغى وتحجب الخلايا الصغيرة.

**ER-G — التحقق والتوثيق.**
- مجلدات `acceptance-evidence/AC-ER-*` على الملف الشبيه بالإنتاج مع `ACCEPTANCE_REQUIRE_ALL=true`.
- رحلة E2E من الشكوى حتى التنفيذ.
- حذف النموذج غير المستخدم أو ربطه، واستبدال `Hcm006EmployeeRelationsTests` و`hcm006-validate.ps1` بفحوص سلوك.
- تصحيح HCM_006 وFUN-004 (ADR وExceptions وUAT وVerification) والبطاقتين 09 و04، وجرد الناشرين.
- القبول:
  - AC-ER-Ga: لا سيناريو ينجح دون فحص.
  - AC-ER-Gb: الوثائق تطابق الشيفرة.
  - AC-ER-Gc: الرحلة الشبيهة بالإنتاج ناجحة.

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: موعد D-ER-1، لأن النص المصدّق غير متاح ويمكن بناء الكتالوج فارغاً قبله. وخيار D-ER-2، إذ يضيف خيار المحرك 3–4 أيام عمل لـ WF. وحجم العمل في PAY وSEP وMOV خارج هذا التقدير.

---

## 7. معيار «اكتمال علاقات العمل والتأديب» المقترح

- [ ] **1. تُغلق G-ER-01…G-ER-08** (إلا ما يؤجَّل بقرار موثّق). ولا يوجد تعريف مسار أو قاعدة سياسة منشوران للتأديب بلا أثر: لا `DisciplinaryAction` في قائمة السماح، ولا `R-DISC` يتيم.
- [ ] **2. تنجح رحلة E2E واحدة على الأقل بإعدادات شبيهة بالإنتاج:** شكوى من ESS بمجهولية ← إسناد محقق مستقل ← محضر سماع ← توصية ← قرار من صاحب السلطة بعقوبة من كتالوج مربوط قانونياً ← تبليغ وإقرار ← استئناف يعدّل العقوبة ← تنفيذ: خصم واحد صحيح في الرواتب، ورفض ترفيع خلال الحجب، وقيد في ملف الموظف. ومعها رحلة فصل تمر عبر SEP حتى تسوية نهاية الخدمة. ويرفض الخادم في الطريق قرار المحقق وقرار من لا يملك السلطة.
- [ ] **3. السرية مثبتة بشخصيات:** مستخدم `compliance` غير مسند لا يفتح الحالة ولا يرى المبلِّغ. والتصدير والتحليلات والأرشيف خالية من الهوية والنص الحر. وكل قراءة لتفاصيل الحالة مدققة.
- [ ] **4. لا قيمة قانونية مثبتة في الشيفرة:** العقوبات والمهل والسقوف تُقرأ من كتالوج مربوط بـ `LegalSourceId`، والإنتاج يرفض العمل دون قيم معتمدة.
- [ ] **5. توجد أدلة `acceptance-evidence/AC-ER-*` لكل سيناريو موجب وسالب في §6، وتُصحَّح** HCM_006 وFUN-004 والبطاقتان 09 و04 لتعكس الواقع.
